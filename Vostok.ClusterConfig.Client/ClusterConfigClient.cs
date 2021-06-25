using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Vostok.ClusterConfig.Client.Abstractions;
using Vostok.ClusterConfig.Client.Helpers;
using Vostok.ClusterConfig.Client.Updaters;
using Vostok.ClusterConfig.Core.Parsers;
using Vostok.Commons.Collections;
using Vostok.Commons.Helpers.Observable;
using Vostok.Commons.Threading;
using Vostok.Commons.Time;
using Vostok.Configuration.Abstractions.SettingsTree;
using Vostok.Configuration.Sources.Extensions.Observable;
using Vostok.Logging.Abstractions;

// ReSharper disable InconsistentlySynchronizedField
// ReSharper disable MethodSupportsCancellation

namespace Vostok.ClusterConfig.Client
{
    /// <inheritdoc cref="IClusterConfigClient"/>
    [PublicAPI]
    public class ClusterConfigClient : IClusterConfigClient, IDisposable
    {
        private readonly ClusterConfigClientSettings settings;
        private readonly CancellationTokenSource cancellationSource;
        private readonly AtomicInt clientState;
        private readonly ILog log;

        private readonly object observablePropagationLock;

        private volatile TaskCompletionSource<ClusterConfigClientState> stateSource;
        private volatile CachingObservable<ClusterConfigClientState> stateObservable;

        /// <summary>
        /// Creates a new instance of <see cref="ClusterConfigClient"/> with given <paramref name="settings"/> merged with default settings from <see cref="DefaultSettingsProvider"/> (non-default user settings take priority).
        /// </summary>
        public ClusterConfigClient([NotNull] ClusterConfigClientSettings settings)
        {
            this.settings = settings = ConfigurationMerger.Merge(DefaultSettingsProvider.Settings, settings ?? throw new ArgumentNullException(nameof(settings)));

            log = settings.Log.ForContext<ClusterConfigClient>();

            stateSource = new TaskCompletionSource<ClusterConfigClientState>(TaskCreationOptions.RunContinuationsAsynchronously);
            stateObservable = new CachingObservable<ClusterConfigClientState>();
            clientState = new AtomicInt(State_NotStarted);
            cancellationSource = new CancellationTokenSource();
            observablePropagationLock = new object();
        }

        /// <summary>
        /// Creates a new instance of <see cref="ClusterConfigClient"/> with default settings provided by <see cref="DefaultSettingsProvider"/>.
        /// </summary>
        public ClusterConfigClient()
            : this(DefaultSettingsProvider.Settings)
        {
        }

        /// <summary>
        /// Returns a singleton instance of <see cref="ClusterConfigClient"/> with default settings obtained with <see cref="DefaultSettingsProvider"/>.
        /// </summary>
        public static ClusterConfigClient Default => DefaultClusterConfigClientProvider.Get();

        /// <summary>
        /// <para>Configures the global <see cref="Default"/> cluster config client with given instance.</para>
        /// <para>This method returns <c>false</c> when trying to overwrite a previously configured instance.</para>
        /// </summary>
        public static bool TrySetDefaultClient([NotNull] ClusterConfigClient clusterConfigClient) =>
            DefaultClusterConfigClientProvider.TryConfigure(clusterConfigClient);

        /// <summary>
        /// Returns the zone this client is operating in.
        /// </summary>
        public string Zone => settings.Zone;

        /// <summary>
        /// Returns current cached zone version (local to this <see cref="ClusterConfigClient"/> instance), or zero if initial update hasn't happened yet.
        /// </summary>
        public long Version => GetCurrentState()?.Version ?? 0L;

        /// <summary>
        /// Returns <c>true</c> if initial settings update has already been completed, or <c>false</c> otherwise.
        /// </summary>
        public bool HasInitialized => GetCurrentState() != null;

        /// <inheritdoc />
        public ISettingsNode Get(ClusterConfigPath prefix)
            => GetSettings(ObtainState(), prefix).settings;

        /// <inheritdoc />
        public (ISettingsNode settings, long version) GetWithVersion(ClusterConfigPath prefix)
            => GetSettings(ObtainState(), prefix);

        /// <inheritdoc />
        public async Task<ISettingsNode> GetAsync(ClusterConfigPath prefix)
            => GetSettings(await ObtainStateAsync().ConfigureAwait(false), prefix).settings;

        /// <inheritdoc />
        public async Task<(ISettingsNode settings, long version)> GetWithVersionAsync(ClusterConfigPath prefix)
            => GetSettings(await ObtainStateAsync().ConfigureAwait(false), prefix);

        /// <inheritdoc />
        public IObservable<ISettingsNode> Observe(ClusterConfigPath prefix)
            => ObtainStateObservable()
                .Select(state => GetSettings(state, prefix).settings)
                .DistinctUntilChanged();

        /// <inheritdoc />
        public IObservable<(ISettingsNode settings, long version)> ObserveWithVersions(ClusterConfigPath prefix)
            => ObtainStateObservable()
                .Select(state => GetSettings(state, prefix))
                .DistinctUntilChanged(tuple => tuple.settings);

        public void Dispose()
        {
            if (clientState.TryIncreaseTo(State_Disposed))
            {
                cancellationSource.Cancel();

                PropagateError(new ObjectDisposedException(GetType().Name), true);
            }
        }

        private static (ISettingsNode settings, long version) GetSettings([NotNull] ClusterConfigClientState state, ClusterConfigPath path)
        {
            try
            {
                return (TreeExtractor.Extract(state, path), state.Version);
            }
            catch (Exception error)
            {
                throw new ClusterConfigClientException($"Failed to extract subtree by path '{path}'.", error);
            }
        }

        [NotNull]
        private ClusterConfigClientState ObtainState()
        {
            InitiatePeriodicUpdates();

            return stateSource.Task.GetAwaiter().GetResult();
        }

        [NotNull]
        private Task<ClusterConfigClientState> ObtainStateAsync()
        {
            InitiatePeriodicUpdates();

            return stateSource.Task;
        }

        [NotNull]
        private IObservable<ClusterConfigClientState> ObtainStateObservable()
        {
            InitiatePeriodicUpdates();

            return stateObservable;
        }

        private void InitiatePeriodicUpdates()
        {
            if (clientState.Value != State_NotStarted)
                return;

            if (clientState.TrySet(State_Started, State_NotStarted))
            {
                using (ExecutionContext.SuppressFlow())
                {
                    Task.Run(() => PeriodicUpdatesLoop(cancellationSource.Token));
                }
            }
        }

        private async Task PeriodicUpdatesLoop(CancellationToken cancellationToken)
        {
            var localUpdater = CreateLocalUpdater(out var localFolder);
            var remoteUpdater = CreateRemoteUpdater();

            var lastLocalResult = null as LocalUpdateResult;
            var lastRemoteResult = null as RemoteUpdateResult;

            log.Info(
                "Starting updates for zone '{Zone}' with period {Period}.",
                settings.Zone,
                settings.UpdatePeriod);

            log.Info(
                "Local settings enabled = {EnableLocalSettings}. Local folder = '{LocalFolder}'.",
                settings.EnableLocalSettings,
                localFolder?.FullName);

            log.Info(
                "Cluster settings enabled = {EnableClusterSettings}. Request timeout = {RequestTimeout}.",
                settings.EnableClusterSettings,
                settings.RequestTimeout);

            while (!cancellationToken.IsCancellationRequested)
            {
                var currentState = GetCurrentState();

                var budget = TimeBudget.StartNew(settings.UpdatePeriod);

                try
                {
                    var localUpdateResult = localUpdater.Update(lastLocalResult);
                    var remoteUpdateResult = await remoteUpdater.UpdateAsync(lastRemoteResult, cancellationToken).ConfigureAwait(false);

                    if (currentState == null || localUpdateResult.Changed || remoteUpdateResult.Changed)
                    {
                        PropagateNewState(CreateNewState(currentState, localUpdateResult, remoteUpdateResult), cancellationToken);
                    }

                    lastLocalResult = localUpdateResult;
                    lastRemoteResult = remoteUpdateResult;
                }
                catch (Exception error)
                {
                    if (cancellationToken.IsCancellationRequested)
                        return;

                    log.Warn(error, "Periodical settings update has failed.");

                    if (currentState == null)
                        PropagateError(new ClusterConfigClientException("Failure in initial settings update.", error), false);
                }

                await Task.Delay(budget.Remaining, cancellationToken).ConfigureAwait(false);
            }
        }

        private LocalUpdater CreateLocalUpdater(out DirectoryInfo localFolder)
        {
            if (settings.EnableLocalSettings)
            {
                var fileParserSettings = new FileParserSettings
                {
                    MaximumFileSize = settings.MaximumFileSize
                };

                var fileParser = new FileParser(fileParserSettings);

                var zoneParser = new ZoneParser(fileParser);

                localFolder = FolderLocator.Locate(AppDomain.CurrentDomain.BaseDirectory, settings.LocalFolder);

                return new LocalUpdater(true, localFolder, zoneParser);
            }

            localFolder = null;

            return new LocalUpdater(false, null, null);
        }

        private RemoteUpdater CreateRemoteUpdater()
            => new RemoteUpdater(
                settings.EnableClusterSettings,
                settings.Cluster,
                settings.AdditionalSetup,
                log,
                settings.Zone,
                settings.RequestTimeout,
                settings.AssumeClusterConfigDeployed);

        [CanBeNull]
        private ClusterConfigClientState GetCurrentState()
            => stateSource.Task.Status == TaskStatus.RanToCompletion ? stateSource.Task.Result : null;

        [NotNull]
        private ClusterConfigClientState CreateNewState(
            [CanBeNull] ClusterConfigClientState oldState,
            [NotNull] LocalUpdateResult localUpdateResult,
            [NotNull] RemoteUpdateResult remoteUpdateResult)
        {
            var newLocalTree = localUpdateResult.Changed ? localUpdateResult.Tree : oldState?.LocalTree;
            var newRemoteTree = remoteUpdateResult.Changed ? remoteUpdateResult.Tree : oldState?.RemoteTree;
            var newCaches = new RecyclingBoundedCache<ClusterConfigPath, ISettingsNode>(settings.CacheCapacity);
            var newVersion = (oldState?.Version ?? 0L) + 1;

            return new ClusterConfigClientState(newLocalTree, newRemoteTree, newCaches, newVersion);
        }

        private void PropagateNewState([NotNull] ClusterConfigClientState state, CancellationToken cancellationToken)
        {
            if (!stateSource.TrySetResult(state))
            {
                // (iloktionov): No point in overwriting ObjectDisposedException stored in 'stateSource' in case the client was disposed.
                if (cancellationToken.IsCancellationRequested)
                    return;

                var newStateSource = new TaskCompletionSource<ClusterConfigClientState>(TaskCreationOptions.RunContinuationsAsynchronously);

                newStateSource.SetResult(state);

                Interlocked.Exchange(ref stateSource, newStateSource);
            }

            // (iloktionov): External observers may take indefinitely long to call, so it's best to offload their callbacks to ThreadPool:
            Task.Run(
                () =>
                {
                    // (iloktionov): Ref check on the state under lock prevents reordering of async observer notifications.
                    // (iloktionov): Older ones may get lost in the event of a race, but that's acceptable.
                    lock (observablePropagationLock)
                    {
                        // (iloktionov): 'stateObservable' might have been already completed by failed initial update iteration. In that case it has to be created from scratch:
                        if (stateObservable.IsCompleted)
                            stateObservable = new CachingObservable<ClusterConfigClientState>();

                        if (ReferenceEquals(state, GetCurrentState()))
                            stateObservable.Next(state);
                    }
                });
        }

        private void PropagateError([NotNull] Exception error, bool alwaysPushToObservable)
        {
            var pushedToSource = stateSource.TrySetException(error);
            if (pushedToSource || alwaysPushToObservable)
                Task.Run(() =>
                {
                    lock (observablePropagationLock)
                    {
                        if (GetCurrentState() == null || alwaysPushToObservable)
                            stateObservable.Error(error);
                    }
                });
        }

        // ReSharper disable InconsistentNaming
        private const int State_NotStarted = 1;
        private const int State_Started = 2;
        private const int State_Disposed = 3;
        // ReSharper restore InconsistentNaming
    }
}