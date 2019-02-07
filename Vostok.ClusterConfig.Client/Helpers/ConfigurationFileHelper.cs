using System;
using JetBrains.Annotations;
using Vostok.Configuration.Abstractions.SettingsTree;

namespace Vostok.ClusterConfig.Client.Helpers
{
    internal static class ConfigurationFileHelper
    {
        public static void Apply([NotNull] ISettingsNode content, [NotNull] ClusterConfigClientSettings settings)
        {
            if (TryGet<bool>(content, nameof(ClusterConfigClientSettings.EnableLocalSettings), "enableLocalSettings", bool.TryParse, out var enableLocalSettings))
                settings.EnableLocalSettings = enableLocalSettings;

            if (TryGet<bool>(content, nameof(ClusterConfigClientSettings.EnableClusterSettings), "enableClusterSettings", bool.TryParse, out var enableClusterSettings))
                settings.EnableClusterSettings = enableClusterSettings;

            if (TryGet<TimeSpan>(content, nameof(ClusterConfigClientSettings.UpdatePeriod), "refreshPeriod", TimeSpan.TryParse, out var updatePeriod))
                settings.UpdatePeriod = updatePeriod;

            if (TryGet<TimeSpan>(content, nameof(ClusterConfigClientSettings.RequestTimeout), "requestTimeout", TimeSpan.TryParse, out var requestTimeout))
                settings.RequestTimeout = requestTimeout;

            if (TryGet(content, nameof(ClusterConfigClientSettings.Zone), "clusterSettingsZoneName", out var zone))
                settings.Zone = zone;

            if (TryGet(content, nameof(ClusterConfigClientSettings.LocalFolder), "localSettingsDirectory", out var localFolder))
                settings.LocalFolder = localFolder;

            if (TryGet(content, "clusterConfigHost", out var dnsEndpoint))
            {
                var parts = dnsEndpoint.Split(':');
                if (parts.Length == 2 && !string.IsNullOrEmpty(parts[0]) && int.TryParse(parts[1], out var port))
                {
                    settings.Cluster = new DnsClusterProvider(parts[0], port);
                }
            }
            else
            {
                int port = default;

                if (TryGet(content, "ServerDNS", out var dns) || TryGet(content, "ServerPort", int.TryParse, out port))
                {
                    settings.Cluster = new DnsClusterProvider(
                        dns ?? ClusterConfigClientDefaults.Dns,
                        port == default ? ClusterConfigClientDefaults.Port : port);
                }
            }
        }

        private static bool TryGet<T>(ISettingsNode settings, string primaryName, string reserveName, TryParse<T> parser, out T item)
            => parser(settings[primaryName]?.Value, out item) || parser(settings[reserveName]?.Value, out item);

        private static bool TryGet<T>(ISettingsNode settings, string name, TryParse<T> parser, out T item)
            => parser(settings[name]?.Value, out item);

        private static bool TryGet(ISettingsNode settings, string primaryName, string reserveName, out string item)
            => (item = settings[primaryName]?.Value ?? settings[reserveName]?.Value) != null;

        private static bool TryGet(ISettingsNode settings, string name, out string item)
            => (item = settings[name]?.Value) != null;

        private delegate bool TryParse<T>(string input, out T output);
    }
}
