using System;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Vostok.ClusterConfig.Client.Abstractions;
using Vostok.ClusterConfig.Client.Helpers;
using Vostok.ClusterConfig.Client.Updaters;
using Vostok.Commons.Collections;
using Vostok.Commons.Threading;
using Vostok.Configuration.Abstractions.Merging;
using Vostok.Configuration.Abstractions.SettingsTree;
using Vostok.Configuration.Sources.Extensions.Observable;
using Vostok.Logging.Abstractions;

namespace Vostok.ClusterConfig.Client
{
    // TODO(iloktionov): threading for observables

    // TODO(iloktionov): Dispose() should probably wait for periodic updater completion

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

        private TaskCompletionSource<ClusterConfigClientState> stateSource;
        private ReplayObservable<ClusterConfigClientState> stateObservable;

        public ClusterConfigClient([NotNull] ClusterConfigClientSettings settings)
        {
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));

            log = settings.Log.ForContext<ClusterConfigClient>();

            stateSource = new TaskCompletionSource<ClusterConfigClientState>(TaskCreationOptions.RunContinuationsAsynchronously);
            stateObservable = new ReplayObservable<ClusterConfigClientState>();
            clientState = new AtomicInt(State_NotStarted);
        }

        public ClusterConfigClient()
            : this(DefaultSettings)
        {
        }

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

            while (!cancellationToken.IsCancellationRequested)
            {
                var currentState = GetCurrentState();

                try
                {
                    var localUpdateResult = localUpdater.Update(lastLocalResult);
                    var remoteUpdateResult = await remoteUpdater.UpdateAsync(lastRemoteResult, cancellationToken).ConfigureAwait(false);

                    if (currentState == null || localUpdateResult.Changed || remoteUpdateResult.Changed)
                    {
                        PropagateNewState(CreateNewState(currentState, localUpdateResult, remoteUpdateResult));
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

                // TODO(iloktionov): factor in iteration length
                await Task.Delay(settings.UpdatePeriod, cancellationToken).ConfigureAwait(false);
            }
        }

        private LocalUpdater CreateLocalUpdater()
            => throw new NotImplementedException();

        private RemoteUpdater CreateRemoteUpdater()
            => throw new NotImplementedException();

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

        private void PropagateNewState([NotNull] ClusterConfigClientState state)
        {
            if (!stateSource.TrySetResult(state))
            {
                var newStateSource = new TaskCompletionSource<ClusterConfigClientState>();

                newStateSource.SetResult(state);

                Interlocked.Exchange(ref stateSource, newStateSource);
            }

            if (stateObservable.IsCompleted)
                Interlocked.Exchange(ref stateObservable, new ReplayObservable<ClusterConfigClientState>());

            stateObservable.Next(state);
        }

        private void PropagateError([NotNull] Exception error)
        {
            if (stateSource.TrySetException(error))
                stateObservable.Error(error);
        }
    }
}
