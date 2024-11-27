using System.Collections.Generic;
using System.Linq;
using JetBrains.Annotations;
using Vostok.ClusterConfig.Client.Abstractions;
using Vostok.Configuration.Abstractions.SettingsTree;

namespace Vostok.ClusterConfig.Client.Helpers;

internal class RemoteSubtrees
{
    public RemoteSubtrees([NotNull] Dictionary<ClusterConfigPath, RemoteTree> subtrees)
    {
        Subtrees = subtrees;
    }
    
    public Dictionary<ClusterConfigPath, RemoteTree> Subtrees { get; }

    public static RemoteSubtrees FromSingleFullTree(RemoteTree singleFullTree)
    {
        return new RemoteSubtrees(new Dictionary<ClusterConfigPath, RemoteTree> {{new ClusterConfigPath("/"), singleFullTree}});
    }

    public ISettingsNode GetSettings(ClusterConfigPath path)
    {
        foreach (var pair in Subtrees)
        {
            var subtreePath = pair.Key;
            if (!path.TryScopeTo(subtreePath, out var remainingPath))
                continue;
            
            var remoteTree = pair.Value;
            if (remoteTree == null)
            {
                return null;
            }

            //TODO (deniaa): Replace all Segments with SegmentsAsMemory if it is possible.
#if NET6_0_OR_GREATER
            var rootName = path.SegmentsAsMemory.LastOrDefault().ToString();       
#else
            var rootName = path.Segments.LastOrDefault();
#endif
            if (rootName == string.Empty)
                rootName = null;
            return remoteTree.GetSettings(remainingPath, rootName);
        }

        return null;
    }
}