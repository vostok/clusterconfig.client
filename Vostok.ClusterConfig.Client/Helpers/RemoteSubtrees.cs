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

    public bool TryGetSettings(ClusterConfigPath path, [CanBeNull] out ISettingsNode result)
    {
        result = null;
        foreach (var pair in Subtrees)
        {
            var subtreePath = pair.Key;
            var remoteTree = pair.Value;
            if (!subtreePath.IsPrefixOf(path))
                continue;

            //TODO (deniaa) кажется это норма, когда remoteTree null. А не буффер в нём. Надо здесь это обработать.
            //TODO (deniaa) Tests
            path = path.ToString().Substring(subtreePath.ToString().Length);
            result = remoteTree.GetSettings(path);
            return true;
        }

        return false;
    }
}