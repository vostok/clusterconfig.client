using System;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Vostok.ClusterConfig.Client.Abstractions;
using Vostok.Configuration.Abstractions.SettingsTree;

namespace Vostok.ClusterConfig.Client
{
    /// <inheritdoc />
    [PublicAPI]
    public class ClusterConfigClient : IClusterConfigClient
    {
        /// <inheritdoc />
        public ISettingsNode Get(ClusterConfigPath prefix) 
            => throw new NotImplementedException();

        /// <inheritdoc />
        public (ISettingsNode settings, long version) GetWithVersion(ClusterConfigPath prefix) 
            => throw new NotImplementedException();

        /// <inheritdoc />
        public Task<ISettingsNode> GetAsync(ClusterConfigPath prefix) 
            => throw new NotImplementedException();

        /// <inheritdoc />
        public Task<(ISettingsNode settings, long version)> GetWithVersionAsync(ClusterConfigPath prefix) 
            => throw new NotImplementedException();

        /// <inheritdoc />
        public IObservable<ISettingsNode> Observe(ClusterConfigPath prefix) 
            => throw new NotImplementedException();

        /// <inheritdoc />
        public IObservable<(ISettingsNode settings, long version)> ObserveWithVersions(ClusterConfigPath prefix) 
            => throw new NotImplementedException();
    }
}
