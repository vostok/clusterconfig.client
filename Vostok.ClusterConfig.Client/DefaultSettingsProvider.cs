using JetBrains.Annotations;

namespace Vostok.ClusterConfig.Client
{
    /// <summary>
    /// <para>Provides default settings used in parameterless ctor of <see cref="ClusterConfigClient"/>.</para>
    /// <para>Looks in an optional configuration file located in <see cref="ClusterConfigClientDefaults.LocalFolder"/> and named <see cref="ClusterConfigClientDefaults.ConfigurationFile"/>.</para>
    /// </summary>
    [PublicAPI]
    public static class DefaultSettingsProvider
    {
        [NotNull]
        public static ClusterConfigClientSettings Settings { get; } = new ClusterConfigClientSettings();
    }
}
