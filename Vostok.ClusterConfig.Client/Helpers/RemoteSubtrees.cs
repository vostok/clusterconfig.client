using System.Collections.Generic;
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

    //TODO (deniaa): tests (on path modification especially)
    public ISettingsNode GetSettings(ClusterConfigPath path)
    {
        foreach (var pair in Subtrees)
        {
            var subtreePath = pair.Key;
            if (!subtreePath.IsPrefixOf(path))
                continue;
            
            var remoteTree = pair.Value;
            if (remoteTree == null)
            {
                return null;
            }
            path = path.ToString().Substring(subtreePath.ToString().Length);
            return remoteTree.GetSettings(path);
        }

        return null;
    }
}