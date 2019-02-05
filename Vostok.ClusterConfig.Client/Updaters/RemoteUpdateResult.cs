using JetBrains.Annotations;
using Vostok.ClusterConfig.Client.Helpers;

namespace Vostok.ClusterConfig.Client.Updaters
{
    internal class RemoteUpdateResult
    {
        public RemoteUpdateResult(bool changed, [CanBeNull] RemoteTree tree, [NotNull] string version)
        {
            Changed = changed;
            Tree = tree;
            Version = version;
        }

        public bool Changed { get; }

        [CanBeNull]
        public RemoteTree Tree { get; }

        [NotNull]
        public string Version { get; }
    }
}
