using System;
using JetBrains.Annotations;
using Vostok.ClusterConfig.Client.Helpers;

namespace Vostok.ClusterConfig.Client.Updaters
{
    internal class RemoteUpdateResult
    {
        public RemoteUpdateResult(bool changed, [CanBeNull] RemoteTree tree, DateTime version)
        {
            Changed = changed;
            Tree = tree;
            Version = version;
        }

        public bool Changed { get; }

        [CanBeNull]
        public RemoteTree Tree { get; }

        public DateTime Version { get; }
    }
}
