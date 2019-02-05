﻿using System;
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

        public const int Port = 9000;

        public const int CacheCapacity = 25;

        public static readonly TimeSpan UpdatePeriod = TimeSpan.FromSeconds(20);
    }
}