using System;
using System.Threading.Tasks;
using Vostok.ClusterConfig.Client.Abstractions;

namespace Vostok.ClusterConfig.Client;

internal class ObservingSubtree
{
    public ObservingSubtree(
        ClusterConfigPath path, 
        TaskCompletionSource<bool> completionSource, 
        DateTime? lastVersion)
    {
        Path = path;
        CompletionSource = completionSource;
        LastVersion = lastVersion;
    }

    public ClusterConfigPath Path { get; }
    public TaskCompletionSource<bool> CompletionSource { get; }
    public DateTime? LastVersion { get; }
}