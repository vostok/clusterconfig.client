using System;
using JetBrains.Annotations;
using Vostok.ClusterConfig.Client.Abstractions;
using Vostok.ClusterConfig.Client.Helpers;

namespace Vostok.ClusterConfig.Client.Updaters
{
    internal class RemoteUpdateResult
    {
        public RemoteUpdateResult(
            bool changed,
            [CanBeNull] RemoteTree tree,
            DateTime version,
            ClusterConfigProtocolVersion? recommendedProtocol,
            PatchingFailedReason? patchingFailedReason)
        {
            Changed = changed;
            Tree = tree;
            Version = version;
            RecommendedProtocol = recommendedProtocol;
            PatchingFailedReason = patchingFailedReason;
        }

        public bool Changed { get; }

        [CanBeNull]
        public RemoteTree Tree { get; }

        public DateTime Version { get; }
        
        public ClusterConfigProtocolVersion? RecommendedProtocol { get; }
        
        public PatchingFailedReason? PatchingFailedReason { get; }
    }
}
