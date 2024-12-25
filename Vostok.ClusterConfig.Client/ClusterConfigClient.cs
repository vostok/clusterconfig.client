using System;
using System.Collections.Generic;
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
        private static readonly ClusterConfigProtocolVersion DefaultProtocol = ClusterConfigProtocolVersion.V2;
        
        private readonly ClusterConfigClientSettings settings;
        private readonly CancellationTokenSource cancellationSource;
        private readonly AtomicInt clientState;
        private readonly RecyclingBoundedCache<string,string> internedValuesCache;
        private readonly ILog log;
        private readonly SubtreesObservingState subtreesObservingState;

        private readonly object observablePropagationLock;

        private volatile TaskCompletionSource<ClusterConfigClientState> stateSource;
        private volatile CachingObservable<ClusterConfigClientState> stateObservable;
        private volatile TaskCompletionSource<bool> immediatelyUpdateCompletionSource;

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
            internedValuesCache = settings.InternedValuesCacheCapacity > 0 ? new RecyclingBoundedCache<string, string>(settings.InternedValuesCacheCapacity) : null;
            immediatelyUpdateCompletionSource = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            subtreesObservingState = new SubtreesObservingState(this.settings.MaximumSubtrees);
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
            => GetSettings(ObtainState(prefix), prefix).settings;

        /// <inheritdoc />
        public (ISettingsNode settings, long version) GetWithVersion(ClusterConfigPath prefix)
            => GetSettings(ObtainState(prefix), prefix);

        /// <inheritdoc />
        public async Task<ISettingsNode> GetAsync(ClusterConfigPath prefix)
            => GetSettings(await ObtainStateAsync(prefix).ConfigureAwait(false), prefix).settings;

        /// <inheritdoc />
        public async Task<(ISettingsNode settings, long version)> GetWithVersionAsync(ClusterConfigPath prefix)
            => GetSettings(await ObtainStateAsync(prefix).ConfigureAwait(false), prefix);

        /// <inheritdoc />
        public IObservable<ISettingsNode> Observe(ClusterConfigPath prefix)
            => ObtainStateObservable(prefix)
                .Select(state => GetSettings(state, prefix).settings)
                .DistinctUntilChanged();

        /// <inheritdoc />
        public IObservable<(ISettingsNode settings, long version)> ObserveWithVersions(ClusterConfigPath prefix)
            => ObtainStateObservable(prefix)
                .Select(state => GetSettings(state, prefix))
                .DistinctUntilChanged(tuple => tuple.settings);

        public void Dispose()
        {
            if (clientState.TryIncreaseTo(State_Disposed))
            {
                cancellationSource.Cancel();

                var error = new ObjectDisposedException(GetType().Name);
                var errorPropagationTask = PropagateError(error, true);
                
                subtreesObservingState.Cancel(error, true, errorPropagationTask);
            }
        }

        private (ISettingsNode settings, long version) GetSettings([NotNull] ClusterConfigClientState state, ClusterConfigPath path)
        {
            try
            {
                return (TreeExtractor.Extract(state, path, settings.MergeOptions), state.Version);
            }
            catch (Exception error)
            {
                throw new ClusterConfigClientException($"Failed to extract subtree by path '{path}'.", error);
            }
        }

        [NotNull]
        private ClusterConfigClientState ObtainState(ClusterConfigPath clusterConfigPath)
        {
            InitiatePeriodicUpdates();

            if (subtreesObservingState.TryAddSubtree(clusterConfigPath, out var tcs, out _))
            {
                immediatelyUpdateCompletionSource.TrySetResult(true);
            }
            
            tcs.Task.GetAwaiter().GetResult();
            return stateSource.Task.GetAwaiter().GetResult();
        }

        [NotNull]
        private async Task<ClusterConfigClientState> ObtainStateAsync(ClusterConfigPath clusterConfigPath)
        {
            InitiatePeriodicUpdates();

            if (subtreesObservingState.TryAddSubtree(clusterConfigPath, out var tcs, out _))
            {
                immediatelyUpdateCompletionSource.TrySetResult(true);
            }

            await tcs.Task.ConfigureAwait(false);
            return await stateSource.Task.ConfigureAwait(false);
        }

        [NotNull]
        private IObservable<ClusterConfigClientState> ObtainStateObservable(ClusterConfigPath clusterConfigPath)
        {
            InitiatePeriodicUpdates();

            if (subtreesObservingState.TryAddSubtree(clusterConfigPath, out _, out var subtreeObservable))
            {
                immediatelyUpdateCompletionSource.TrySetResult(true);
                //(deniaa): Compare with ObtainStateAsync method here we will not wait for the first settings from server for given path.
                //(deniaa): It's an observable and it will notify subscribers when a value is received.
                //(deniaa): So here we just cancel token to force the update.  
            }
            
            //(deniaa): If subtreesObservingState is cancelled and subtreeObservable is null we can return common and cancelled stateObservable.
            return subtreeObservable ?? stateObservable;
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

            var protocol = settings.ForcedProtocolVersion ?? DefaultProtocol;

            log.Info(
                "Starting updates for zone '{Zone}' with period {Period} and protocol {Protocol}.",
                settings.Zone,
                settings.UpdatePeriod,
                protocol);

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
                //(deniaa): The main idea is to setup this TCS before we go to backend.
                //(deniaa): If it is set up before going to backend then in the worst case we will just go to the backend one more time.
                //(deniaa): If it is set up before this line and we wipe that source - it's OK, we will immediately go to backend anyway.
                immediatelyUpdateCompletionSource = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var currentState = GetCurrentState();

                var budget = TimeBudget.StartNew(settings.UpdatePeriod);

                if (protocol < ClusterConfigProtocolVersion.V3)
                    subtreesObservingState.TryAddSubtree(new ClusterConfigPath(""), out _, out _);
                var observingSubtrees = subtreesObservingState.GetSubtreesToRequest();

                try
                {
                    var localUpdateResult = localUpdater.Update(lastLocalResult);

                    var remoteUpdateResult = await remoteUpdater.UpdateAsync(observingSubtrees, protocol, lastRemoteResult, cancellationToken).ConfigureAwait(false);

                    if (remoteUpdateResult.RecommendedProtocol is {} recommendedProtocol && settings.ForcedProtocolVersion == null)
                    {
                        if (protocol != recommendedProtocol)
                        {
                            log.Info("Protocol changed via server recommendation: {OldProtocol} -> {NewProtocol}", protocol, recommendedProtocol);
                            protocol = recommendedProtocol;
                        }
                    }

                    if (currentState == null || localUpdateResult.Changed || remoteUpdateResult.Changed)
                    {
                        var newState = CreateNewState(currentState, localUpdateResult, remoteUpdateResult);
                        var rootObservablePropagationTask = PropagateNewState(newState, observingSubtrees, cancellationToken);
                        
                        //(deniaa): We could make a version for each subtree and change it only if content have changed.
                        //(deniaa): So as not to do useless changes of unchanged subtree if zone has changed elsewhere.
                        subtreesObservingState.FinalizeSubtrees(observingSubtrees, remoteUpdateResult.Version, rootObservablePropagationTask, cancellationToken);
                    }


                    lastLocalResult = localUpdateResult;
                    lastRemoteResult = remoteUpdateResult;
                }
                catch (Exception error)
                {
                    if (cancellationToken.IsCancellationRequested)
                        return;

                    log.Warn(error, "Periodical settings update has failed.");

                    error = new ClusterConfigClientException("Failure in initial settings update.", error);
                    var errorPropagatingTask = Task.CompletedTask;
                    if (currentState == null)
                        errorPropagatingTask = PropagateError(error, false);
                    if (observingSubtrees != null)
                        subtreesObservingState.FailUnfinalizedSubtrees(observingSubtrees, error, errorPropagatingTask);
                }

                await Task.WhenAny(Task.Delay(budget.Remaining, cancellationToken), immediatelyUpdateCompletionSource.Task).ConfigureAwait(false);
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
                internedValuesCache,
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
            var newRemoteSubtrees = remoteUpdateResult.Changed ? remoteUpdateResult.Subtrees : oldState?.RemoteSubtrees;
            var newCaches = new RecyclingBoundedCache<ClusterConfigPath, ISettingsNode>(settings.CacheCapacity);
            var newVersion = (oldState?.Version ?? 0L) + 1;
            
            if (!settings.EnableLocalSettings && settings.EnableClusterSettings)
                newVersion = remoteUpdateResult.Changed && remoteUpdateResult.Version != DateTime.MinValue
                    ? remoteUpdateResult.Version.Ticks
                    : newVersion;

            return new ClusterConfigClientState(newLocalTree, newRemoteSubtrees, newCaches, newVersion);
        }

        private Task<CachingObservable<ClusterConfigClientState>> PropagateNewState(
            [NotNull] ClusterConfigClientState state, 
            List<ObservingSubtree> observingSubtrees, 
            CancellationToken cancellationToken)
        {
            if (!stateSource.TrySetResult(state))
            {
                // (iloktionov): No point in overwriting ObjectDisposedException stored in 'stateSource' in case the client was disposed.
                if (cancellationToken.IsCancellationRequested)
                {
                    var faultedObs = new CachingObservable<ClusterConfigClientState>();
                    faultedObs.Error(new ObjectDisposedException(GetType().Name));
                    return Task.FromResult(faultedObs);
                }

                var newStateSource = new TaskCompletionSource<ClusterConfigClientState>(TaskCreationOptions.RunContinuationsAsynchronously);

                newStateSource.SetResult(state);

                Interlocked.Exchange(ref stateSource, newStateSource);
            }

            // (iloktionov): External observers may take indefinitely long to call, so it's best to offload their callbacks to ThreadPool:
            return Task.Run(
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
                        
                        return stateObservable;
                    }
                });
        }

        private Task PropagateError([NotNull] Exception error, bool alwaysPushToObservable)
        {
            var pushedToSource = stateSource.TrySetException(error);
            if (pushedToSource || alwaysPushToObservable)
                return Task.Run(() =>
                {
                    lock (observablePropagationLock)
                    {
                        if (GetCurrentState() == null || alwaysPushToObservable)
                            stateObservable.Error(error);
                    }
                });

            return Task.CompletedTask;
        }

        // ReSharper disable InconsistentNaming
        private const int State_NotStarted = 1;
        private const int State_Started = 2;
        private const int State_Disposed = 3;
        // ReSharper restore InconsistentNaming
    }
}