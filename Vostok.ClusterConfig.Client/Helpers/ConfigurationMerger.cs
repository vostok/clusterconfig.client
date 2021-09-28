using System.Reflection;
using JetBrains.Annotations;

namespace Vostok.ClusterConfig.Client.Helpers
{
    internal static class ConfigurationMerger
    {
        private static readonly ClusterConfigClientSettings DefaultSettings = new ClusterConfigClientSettings();

        [NotNull]
        public static ClusterConfigClientSettings Merge(
            [NotNull] ClusterConfigClientSettings baseSettings,
            [NotNull] ClusterConfigClientSettings userSettings)
        {
            var result = new ClusterConfigClientSettings();

            foreach (var property in typeof(ClusterConfigClientSettings).GetProperties(BindingFlags.Instance | BindingFlags.Public))
            {
                var baseValue = property.GetValue(baseSettings);
                var userValue = property.GetValue(userSettings);
                var defaultValue = property.GetValue(DefaultSettings);

                property.SetValue(result, Select(baseValue, userValue, userSettings.ChangedSettings.Contains(property.Name), defaultValue));
            }

            return result;
        }

        private static object Select(object baseValue, object userValue, bool userValueChanged, object defaultValue)
            => userValueChanged || !Equals(userValue, defaultValue) ? userValue : baseValue;
    }
}
