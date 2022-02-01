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
            bool hashMismatch)
        {
            Changed = changed;
            Tree = tree;
            Version = version;
            RecommendedProtocol = recommendedProtocol;
            HashMismatch = hashMismatch;
        }

        public bool Changed { get; }

        [CanBeNull]
        public RemoteTree Tree { get; }

        public DateTime Version { get; }
        
        public ClusterConfigProtocolVersion? RecommendedProtocol { get; }
        
        public bool HashMismatch { get; }
    }
}
