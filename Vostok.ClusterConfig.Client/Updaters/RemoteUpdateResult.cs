using System;
using JetBrains.Annotations;
using Vostok.ClusterConfig.Client.Helpers;

namespace Vostok.ClusterConfig.Client.Updaters
{
    /// <summary>
    /// Due to possibility to change protocol in runtime we have to deal with old RemoteTree <see cref="Tree"/> in pair with new RemoteSubtrees <see cref="Subtrees"/>.
    /// So we have an invariant: at one moment only one of this two "trees" can be not null (both can be null).
    /// If, occasionally, both are not null, recommended to use an old single full tree RemoteTree <see cref="Tree"/>.
    /// </summary>
    internal class RemoteUpdateResult
    {
        public RemoteUpdateResult(
            bool changed,
            [CanBeNull] RemoteSubtrees subtrees,
            [CanBeNull] string description,
            ClusterConfigProtocolVersion? usedProtocol,
            DateTime version,
            ClusterConfigProtocolVersion? recommendedProtocol,
            PatchingFailedReason? patchingFailedReason)
        {
            Changed = changed;
            Subtrees = subtrees;
            Description = description;
            Version = version;
            RecommendedProtocol = recommendedProtocol;
            PatchingFailedReason = patchingFailedReason;
            UsedProtocol = usedProtocol;
        }

        public bool Changed { get; }

        [CanBeNull]
        public RemoteSubtrees Subtrees { get; }
        
        [CanBeNull]
        public string Description { get; }
        
        public ClusterConfigProtocolVersion? UsedProtocol { get; }

        public DateTime Version { get; }
        
        public ClusterConfigProtocolVersion? RecommendedProtocol { get; }
        
        public PatchingFailedReason? PatchingFailedReason { get; }
    }
}
