﻿using System;
using JetBrains.Annotations;
using Vostok.Clusterclient.Core;
using Vostok.Clusterclient.Core.Topology;
using Vostok.Logging.Abstractions;

namespace Vostok.ClusterConfig.Client
{
    /// <summary>
    /// Represents settings governing a single <see cref="ClusterConfigClient"/> instance.
    /// </summary>
    [PublicAPI]
    public class ClusterConfigClientSettings
    {
        /// <summary>
        /// Gets or sets whether to look for settings files locally in <see cref="LocalFolder"/>.
        /// </summary>
        public bool EnableLocalSettings { get; set; } = true;

        /// <summary>
        /// Gets or sets whether to query remote server for settings.
        /// </summary>
        public bool EnableClusterSettings { get; set; } = true;

        /// <summary>
        /// <para>Gets or sets the zone queried from server.</para>
        /// <para>Only relevant when <see cref="EnableClusterSettings"/> is set to <c>true</c>.</para>
        /// </summary>
        public string Zone { get; set; } = ClusterConfigClientDefaults.Zone;

        /// <summary>
        /// <para>Gets or sets the path to local folder used to look for files.</para>
        /// <para>This path can be relative: it's resolved against current <see cref="System.AppDomain"/>'s <see cref="System.AppDomain.BaseDirectory"/>.</para>
        /// <para>Only relevant when <see cref="EnableLocalSettings"/> is set to <c>true</c>.</para>
        /// </summary>
        public string LocalFolder { get; set; } = ClusterConfigClientDefaults.LocalFolder;

        /// <summary>
        /// <para>Gets or sets the cluster of remote replicas used to query settings.</para>
        /// <para>Only relevant when <see cref="EnableClusterSettings"/> is set to <c>true</c>.</para>
        /// </summary>
        public IClusterProvider Cluster { get; set; } = ClusterConfigClientDefaults.Cluster;

        /// <summary>
        /// Gets or sets the period of data updates. It affects both remote queries and local folder checks.
        /// </summary>
        public TimeSpan UpdatePeriod { get; set; } = ClusterConfigClientDefaults.UpdatePeriod;

        /// <summary>
        /// <para>Gets or sets the timeout for server requests.</para>
        /// <para>Only relevant when <see cref="EnableClusterSettings"/> is set to <c>true</c>.</para>
        /// </summary>
        public TimeSpan RequestTimeout { get; set; } = ClusterConfigClientDefaults.RequestTimeout;

        /// <summary>
        /// Gets or sets the log used for internal diagnostic messages.
        /// </summary>
        public ILog Log { get; set; } = LogProvider.Get();

        /// <summary>
        /// Gets or sets the capacity of the internal cache storing subtrees corresponding to requested prefixes.
        /// </summary>
        public int CacheCapacity { get; set; } = ClusterConfigClientDefaults.CacheCapacity;

        /// <summary>
        /// <para>Gets or sets the maximum allowed file size. Local files larger than this will be ignored.</para>
        /// <para>Only relevant when <see cref="EnableLocalSettings"/> is set to <c>true</c>.</para>
        /// </summary>
        public int MaximumFileSize { get; set; } = ClusterConfigClientDefaults.MaximumFileSize;
        
        /// <summary>
        /// <para>An optional delegate that can be used to tune underlying <see cref="IClusterClient"/> instance.</para>
        /// </summary>
        [CanBeNull]
        public ClusterClientSetup AdditionalSetup { get; set; }
    }
}