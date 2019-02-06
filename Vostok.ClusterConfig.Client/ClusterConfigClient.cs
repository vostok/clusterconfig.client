using System;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Vostok.ClusterConfig.Client.Abstractions;
using Vostok.ClusterConfig.Client.Helpers;
using Vostok.ClusterConfig.Client.Updaters;
using Vostok.ClusterConfig.Core.Parsers;
using Vostok.Commons.Collections;
using Vostok.Commons.Threading;
using Vostok.Commons.Time;
using Vostok.Configuration.Abstractions.Merging;
using Vostok.Configuration.Abstractions.SettingsTree;
using Vostok.Configuration.Sources.Extensions.Observable;
using Vostok.Logging.Abstractions;

// ReSharper disable MethodSupportsCancellation

namespace Vostok.ClusterConfig.Client
{
    /// <inheritdoc cref="IClusterConfigClient"/>
    [PublicAPI]
    public class ClusterConfigClient : IClusterConfigClient, IDisposable
    {
        private const int State_NotStarted = 1;
        private const int State_Started = 2;
        private const int State_Disposed = 3;

        private static readonly ClusterConfigClientSettings DefaultSettings = new ClusterConfigClientSettings();

        private static readonly SettingsMergeOptions DefaultMergeOptions = new SettingsMergeOptions
        {
            ObjectMergeStyle = ObjectMergeStyle.Deep,
            ArrayMergeStyle = ArrayMergeStyle.Replace
        };

        private readonly ClusterConfigClientSettings settings;
        private readonly CancellationTokenSource cancellationSource;
        private readonly AtomicInt clientState;
        private readonly ILog log;

        private readonly object observablePropagationLock;

        private TaskCompletionSource<ClusterConfigClientState> stateSource;
        private CachingObservable<ClusterConfigClientState> stateObservable;

        public ClusterConfigClient([NotNull] ClusterConfigClientSettings settings)
        {
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));

            log = settings.Log.ForContext<ClusterConfigClient>();

            stateSource = new TaskCompletionSource<ClusterConfigClientState>(TaskCreationOptions.RunContinuationsAsynchronously);
            stateObservable = new CachingObservable<ClusterConfigClientState>();
            clientState = new AtomicInt(State_NotStarted);
            observablePropagationLock = new object();
        }

        public ClusterConfigClient()
            : this(DefaultSettings)
        {
        }

        public string Zone => settings.Zone;

        public long Version => GetCurrentState()?.Version ?? 0L;

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
            => ObtainStateObservable().Select(state => GetSettings(state, prefix).settings);

        /// <inheritdoc />
        public IObservable<(ISettingsNode settings, long version)> ObserveWithVersions(ClusterConfigPath prefix)
            => ObtainStateObservable().Select(state => GetSettings(state, prefix));

        public void Dispose()
        {
            if (clientState.TryIncreaseTo(State_Disposed))
            {
                cancellationSource.Cancel();

                PropagateError(new ObjectDisposedException(GetType().Name));
            }
        }

        private static (ISettingsNode settings, long version) GetSettings(ClusterConfigClientState state, ClusterConfigPath path)
        {
            var settings = state.Cache.Obtain(
                path,
                p =>
                {
                    var remoteSettings = state.RemoteTree?.GetSettings(p);
                    var localSettings = state.LocalTree?.ScopeTo(p.Segments);

                    return SettingsNodeMerger.Merge(remoteSettings, localSettings, DefaultMergeOptions);
                });

            return (settings, state.Version);
        }

        private ClusterConfigClientState ObtainState()
        {
            InitiatePeriodicUpdates();

            return stateSource.Task.GetAwaiter().GetResult();
        }

        private Task<ClusterConfigClientState> ObtainStateAsync()
        {
            InitiatePeriodicUpdates();

            return stateSource.Task;
        }

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
            var localUpdater = CreateLocalUpdater();
            var remoteUpdater = CreateRemoteUpdater();

            var lastLocalResult = null as LocalUpdateResult;
            var lastRemoteResult = null as RemoteUpdateResult;

            log.Info(
                "Starting updates for zone {Zone} with period {Period}. " +
                "Local settings enabled = {EnableLocalSettings} (folder = {LocalFolder}). " +
                "Cluster settings enabled = {EnableClusterSettings}.",
                settings.Zone,
                settings.UpdatePeriod,
                settings.EnableLocalSettings,
                settings.LocalFolder,
                settings.EnableClusterSettings);

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
                        PropagateError(new ClusterConfigClientException("Failure in initial settings update.", error));
                }

                await Task.Delay(budget.Remaining, cancellationToken).ConfigureAwait(false);
            }
        }

        private LocalUpdater CreateLocalUpdater()
        {
            if (settings.EnableLocalSettings)
            {
                var fileParserSettings = new FileParserSettings
                {
                    MaximumFileSize = settings.MaximumFileSize
                };

                var fileParser = new FileParser(fileParserSettings);

                var zoneParser = new ZoneParser(fileParser);

                var localFolder = FolderLocator.Locate(AppDomain.CurrentDomain.BaseDirectory, settings.LocalFolder, 3);

                if (settings.EnableLocalSettings)
                    log.Info("Resolved local settings directory path to '{LocalFolder}'.", localFolder.FullName);

                return new LocalUpdater(true, localFolder, zoneParser);
            }

            return new LocalUpdater(false, null, null);
        }

        private RemoteUpdater CreateRemoteUpdater()
            => new RemoteUpdater(settings.EnableClusterSettings, settings.Cluster, log, settings.Zone);

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

                var newStateSource = new TaskCompletionSource<ClusterConfigClientState>();

                newStateSource.SetResult(state);

                Interlocked.Exchange(ref stateSource, newStateSource);
            }

            // (iloktionov): 'stateObservable' might have been already completed by failed initial update iteration. In that case it has to be created from scratch:
            if (stateObservable.IsCompleted)
                Interlocked.Exchange(ref stateObservable, new CachingObservable<ClusterConfigClientState>());

            // (iloktionov): External observers may take indefinitely long to call, so it's best to offload their callbacks to ThreadPool:
            Task.Run(
                () =>
                {
                    // (iloktionov): Ref check on the state under lock prevent reordering of async observer notifications.
                    // (iloktionov): Older ones may get lost in the event of a race, but that's acceptable.
                    lock (observablePropagationLock)
                    {
                        if (ReferenceEquals(state, GetCurrentState()))
                            stateObservable.Next(state);
                    }
                });
        }

        private void PropagateError([NotNull] Exception error)
        {
            if (stateSource.TrySetException(error))
                Task.Run(() => stateObservable.Error(error));
        }
    }
}
