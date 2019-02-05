using System;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Vostok.ClusterConfig.Client.Abstractions;
using Vostok.ClusterConfig.Client.Helpers;
using Vostok.Configuration.Abstractions.Merging;
using Vostok.Configuration.Abstractions.SettingsTree;
using Vostok.Configuration.Sources.Extensions.Observable;

namespace Vostok.ClusterConfig.Client
{
    /// <inheritdoc />
    [PublicAPI]
    public class ClusterConfigClient : IClusterConfigClient
    {
        private static readonly ClusterConfigClientSettings DefaultSettings = new ClusterConfigClientSettings();

        private static readonly SettingsMergeOptions DefaultMergeOptions = new SettingsMergeOptions
        {
            ObjectMergeStyle = ObjectMergeStyle.Deep,
            ArrayMergeStyle = ArrayMergeStyle.Replace
        };

        private readonly Func<ClusterConfigClientSettings> settingsProvider;
        private readonly CancellationTokenSource cancellationSource;

        private TaskCompletionSource<ClusterConfigClientState> stateSource;
        private ReplayObservable<ClusterConfigClientState> stateObservable;

        public ClusterConfigClient([NotNull] Func<ClusterConfigClientSettings> settingsProvider)
        {
            this.settingsProvider = settingsProvider ?? throw new ArgumentNullException(nameof(settingsProvider));

            stateSource = new TaskCompletionSource<ClusterConfigClientState>();
            stateObservable = new ReplayObservable<ClusterConfigClientState>();
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
            // TODO(iloktionov): impement
        }
    }
}
