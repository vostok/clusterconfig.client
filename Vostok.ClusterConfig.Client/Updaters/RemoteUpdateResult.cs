using System;
using JetBrains.Annotations;
using Vostok.ClusterConfig.Client.Helpers;

namespace Vostok.ClusterConfig.Client.Updaters
{
    internal class RemoteUpdateResult
    {
        //TODO а не убрать ли RemoteTree целиком внутрь RemoteSubtrees как поддерево с путём "/"?..
        public RemoteUpdateResult(
            bool changedFullTree,
            [CanBeNull] RemoteTree fullTree,
            bool changedSubtrees,
            [CanBeNull] RemoteSubtrees subtrees,
            DateTime version,
            ClusterConfigProtocolVersion? recommendedProtocol,
            PatchingFailedReason? patchingFailedReason)
        {
            Changed = changedFullTree;
            Tree = fullTree;
            ChangedSubtrees = changedSubtrees;
            Subtrees = subtrees;
            Version = version;
            RecommendedProtocol = recommendedProtocol;
            PatchingFailedReason = patchingFailedReason;
        }


        public bool Changed { get; }

        [CanBeNull]
        public RemoteTree Tree { get; }

        public bool ChangedSubtrees { get; set; }
        
        [CanBeNull]
        public RemoteSubtrees Subtrees { get; set; }

        public DateTime Version { get; }
        
        public ClusterConfigProtocolVersion? RecommendedProtocol { get; }
        
        public PatchingFailedReason? PatchingFailedReason { get; }
    }
}
