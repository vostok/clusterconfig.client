using System;
using System.Threading.Tasks;
using Vostok.ClusterConfig.Client.Abstractions;

namespace Vostok.ClusterConfig.Client;

internal class ObservingSubtree
{
    public ObservingSubtree(ClusterConfigPath path)
    {
        Path = path;
        AtLeastOnceObtaining = new TaskCompletionSource<bool>();
        LastVersion = null;
    }

    public ClusterConfigPath Path { get; }
    public TaskCompletionSource<bool> AtLeastOnceObtaining { get; }
    public DateTime? LastVersion { get; set; }
}