using System;
using JetBrains.Annotations;

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

        public const int CacheCapacity = 25;

        public const int MaximumFileSize = 1024 * 1024;

        public static readonly TimeSpan UpdatePeriod = TimeSpan.FromSeconds(20);

        public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);
    }
}