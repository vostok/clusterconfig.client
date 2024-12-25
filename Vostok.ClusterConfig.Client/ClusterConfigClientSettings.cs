using System;
using System.Collections.Generic;
using JetBrains.Annotations;
using Vostok.Clusterclient.Core;
using Vostok.Clusterclient.Core.Topology;
using Vostok.ClusterConfig.Client.Abstractions;
using Vostok.Configuration.Abstractions.Merging;
using Vostok.Logging.Abstractions;

namespace Vostok.ClusterConfig.Client
{
    /// <summary>
    /// Represents settings governing a single <see cref="ClusterConfigClient"/> instance.
    /// </summary>
    [PublicAPI]
    public class ClusterConfigClientSettings
    {
        internal HashSet<string> ChangedSettings = new HashSet<string>();
        private string zone = ClusterConfigClientDefaults.Zone;

        /// <summary>
        /// Gets or sets whether to look for settings files locally in <see cref="LocalFolder"/>.
        /// </summary>
        public bool EnableLocalSettings { get; set; } = true;

        /// <summary>
        /// Gets or sets whether to query remote server for settings.
        /// </summary>
        public bool EnableClusterSettings { get; set; } = true;

        /// <summary>
        /// Forces client to assume that CC is deployed in current environment. This forces client to throw when it was unable to find any replicas of CC.
        /// </summary>
        public bool AssumeClusterConfigDeployed { get; set; }

        /// <summary>
        /// <para>Gets or sets the zone queried from server.</para>
        /// <para>Only relevant when <see cref="EnableClusterSettings"/> is set to <c>true</c>.</para>
        /// </summary>
        public string Zone
        {
            get => zone;
            set
            {
                zone = value;
                ChangedSettings.Add(nameof(Zone));
            }
        }

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
        /// Gets or sets the capacity of the internal cache storing interned string keys and values from subtrees corresponding to requested prefixes.
        /// If the value is less or equal to zero, no interning will be used. The default value is 0.
        /// </summary>
        public int InternedValuesCacheCapacity { get; set; } = ClusterConfigClientDefaults.InternedValuesCacheCapacity;
        
        /// <summary>
        /// Gets or sets the capacity of the structure to store and control settings subtrees to not download the full tree.
        /// Works only if <see cref="ForcedProtocolVersion"/> is <see cref="ClusterConfigProtocolVersion.V3_1"/> (it is by default).
        /// If the value is less or equal to zero, whole tree downloading will be used. The default value is <see cref="ClusterConfigClientDefaults.MaximumSubtrees"/>.
        /// If more different <see cref="ClusterConfigPath"/> are requested than the configured limit, full tree download will be used instead.
        /// Client may exceed count of subtrees by no more than two times, in case of different races.
        /// </summary>
        public int MaximumSubtrees { get; set; } = ClusterConfigClientDefaults.MaximumSubtrees;

        /// <summary>
        /// <para>Gets or sets the maximum allowed file size. Local files larger than this will be ignored.</para>
        /// <para>Only relevant when <see cref="EnableLocalSettings"/> is set to <c>true</c>.</para>
        /// </summary>
        public int MaximumFileSize { get; set; } = ClusterConfigClientDefaults.MaximumFileSize;

        /// <summary>
        /// <para>Forces <see cref="ClusterConfigProtocolVersion"/> if specified.</para>
        /// <para>Otherwise server's recommended <see cref="ClusterConfigProtocolVersion"/> will be used.</para>
        /// </summary>
        public ClusterConfigProtocolVersion? ForcedProtocolVersion { get; set; }
        
        /// <summary>
        /// <para>An optional delegate that can be used to tune underlying <see cref="IClusterClient"/> instance.</para>
        /// </summary>
        [CanBeNull]
        public ClusterClientSetup AdditionalSetup { get; set; }

        [CanBeNull]
        public SettingsMergeOptions MergeOptions { get; set; }
    }
}