using JetBrains.Annotations;
using Vostok.Configuration.Abstractions.SettingsTree;

namespace Vostok.ClusterConfig.Client.Updaters
{
    internal class LocalUpdateResult
    {
        public LocalUpdateResult(bool changed, [CanBeNull] ISettingsNode tree)
        {
            Changed = changed;
            Tree = tree;
        }

        public bool Changed { get; }

        [CanBeNull]
        public ISettingsNode Tree { get; }
    }
}