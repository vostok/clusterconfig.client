using JetBrains.Annotations;
using Vostok.ClusterConfig.Client.Abstractions;
using Vostok.ClusterConfig.Client.Helpers;
using Vostok.Commons.Collections;
using Vostok.Configuration.Abstractions.SettingsTree;

namespace Vostok.ClusterConfig.Client
{
    internal class ClusterConfigClientState
    {
        public ClusterConfigClientState(
            [CanBeNull] ISettingsNode localTree, 
            [CanBeNull] RemoteTree remoteTree, 
            [CanBeNull] RemoteSubtrees remoteSubtrees, 
            [NotNull] RecyclingBoundedCache<ClusterConfigPath, ISettingsNode> cache, 
            long version)
        {
            LocalTree = localTree;
            RemoteTree = remoteTree;
            RemoteSubtrees = remoteSubtrees;
            Cache = cache;
            Version = version;
        }

        [CanBeNull]
        public ISettingsNode LocalTree { get; }

        [CanBeNull]
        public RemoteTree RemoteTree { get; }

        [CanBeNull]
        public RemoteSubtrees RemoteSubtrees { get; }

        [NotNull]
        public RecyclingBoundedCache<ClusterConfigPath, ISettingsNode> Cache { get; }

        public long Version { get; }
    }
}