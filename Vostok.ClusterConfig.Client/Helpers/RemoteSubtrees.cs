using System.Collections.Generic;
using JetBrains.Annotations;
using Vostok.ClusterConfig.Client.Abstractions;
using Vostok.Configuration.Abstractions.SettingsTree;

namespace Vostok.ClusterConfig.Client.Helpers;

internal class RemoteSubtrees
{
    public RemoteSubtrees([NotNull] List<(ClusterConfigPath, RemoteTree)> subtrees)
    {
        Subtrees = subtrees;
    }
    
    public List<(ClusterConfigPath, RemoteTree)> Subtrees { get; }

    public bool TryGetSettings(ClusterConfigPath path, [CanBeNull] out ISettingsNode result)
    {
        result = null;
        foreach (var (subtreePath, remoteTree) in Subtrees)
        {
            //TODO IsPrefixOf переписать на спанах
            if (!subtreePath.IsPrefixOf(path))
                continue;
            
            //TODO откусить часть path, которую нашли в префиксе?
            result = remoteTree.GetSettings(path);
            return true;
        }

        return false;
    }
}