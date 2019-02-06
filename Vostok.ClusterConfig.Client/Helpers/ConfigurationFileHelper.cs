using JetBrains.Annotations;
using Vostok.Configuration.Abstractions.SettingsTree;

namespace Vostok.ClusterConfig.Client.Helpers
{
    internal static class ConfigurationFileHelper
    {
        public static void Apply([NotNull] ISettingsNode content, [NotNull] ClusterConfigClientSettings settings)
        {
            // TODO(iloktionov): implement
        }
    }
}