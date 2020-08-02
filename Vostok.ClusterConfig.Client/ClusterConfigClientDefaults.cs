using System;
using JetBrains.Annotations;
using Vostok.Clusterclient.Core.Topology;
using Vostok.ClusterConfig.Client.Helpers;

namespace Vostok.ClusterConfig.Client
{
    /// <summary>
    /// Common default constants used by <see cref="ClusterConfigClient"/> if not configured explicitly.
    /// </summary>
    [PublicAPI]
    public static class ClusterConfigClientDefaults
    {
        public const string Zone = "default";

        public const string Dns = "clusterconfig";

        public const string LocalFolder = "settings";

        public const string ConfigurationFile = "clusterconfig";

        public const int Port = 9000;

        public const int CacheCapacity = 75;

        public const int MaximumFileSize = 1024 * 1024;

        public static readonly TimeSpan UpdatePeriod = TimeSpan.FromSeconds(10);

        public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(45);

        public static readonly IClusterProvider Cluster = new DnsClusterProvider(Dns, Port);
    }
}
