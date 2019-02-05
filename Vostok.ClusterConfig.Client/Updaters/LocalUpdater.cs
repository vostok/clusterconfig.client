using System.IO;
using JetBrains.Annotations;
using Vostok.ClusterConfig.Core.Parsers;
using Vostok.Configuration.Abstractions.SettingsTree;

namespace Vostok.ClusterConfig.Client.Updaters
{
    internal class LocalUpdater
    {
        private readonly bool enabled;
        private readonly DirectoryInfo folder;
        private readonly IZoneParser zoneParser;

        public LocalUpdater(bool enabled, DirectoryInfo folder, IZoneParser zoneParser)
        {
            this.enabled = enabled;
            this.folder = folder;
            this.zoneParser = zoneParser;
        }

        public LocalUpdateResult Update([CanBeNull] LocalUpdateResult lastResult)
        {
            var freshTree = UpdateTree();
           
            return new LocalUpdateResult(Equals(lastResult?.Tree, freshTree), freshTree);
        }

        private ISettingsNode UpdateTree()
        {
            if (!enabled)
                return null;

            folder.Refresh();

            return folder.Exists ? zoneParser.Parse(folder) : null;
        }
    }
}
