using System;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Vostok.ClusterConfig.Client.Abstractions;
using Vostok.ClusterConfig.Client.Helpers;
using Vostok.Commons.Threading;
using Vostok.Configuration.Abstractions.Merging;
using Vostok.Configuration.Abstractions.SettingsTree;
using Vostok.Configuration.Sources.Extensions.Observable;

namespace Vostok.ClusterConfig.Client
{
    // TODO(iloktionov): threading for observables

    // TODO(iloktionov): "warmness" of ClusterConfigClientSettings: do we need it?

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

        private readonly Func<ClusterConfigClientSettings> settingsProvider;
        private readonly CancellationTokenSource cancellationSource;
        private readonly AtomicInt clientState;

        private TaskCompletionSource<ClusterConfigClientState> stateSource;
        private ReplayObservable<ClusterConfigClientState> stateObservable;

        public ClusterConfigClient([NotNull] Func<ClusterConfigClientSettings> settingsProvider)
        {
            this.settingsProvider = settingsProvider ?? throw new ArgumentNullException(nameof(settingsProvider));

            stateSource = new TaskCompletionSource<ClusterConfigClientState>();
            stateObservable = new ReplayObservable<ClusterConfigClientState>();
            clientState = new AtomicInt(State_NotStarted);
        }

        public ClusterConfigClient()
            : this(() => DefaultSettings)
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

                var disposedError = new ObjectDisposedException(GetType().Name);

                if (stateSource.TrySetException(disposedError))
                    stateObservable.Error(disposedError);
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
                Task.Run(() => PeriodicUpdatesLoop(cancellationSource.Token));
            }
        }

        private async Task PeriodicUpdatesLoop(CancellationToken cancellationToken)
        {
            while (true)
            {
                await Task.Delay(1000).ConfigureAwait(false);
            }
        }
    }
}
