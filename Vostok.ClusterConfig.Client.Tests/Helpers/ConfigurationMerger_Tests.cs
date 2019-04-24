using System;
using System.Collections.Generic;
using FluentAssertions;
using FluentAssertions.Extensions;
using NUnit.Framework;
using Vostok.Clusterclient.Core;
using Vostok.Clusterclient.Core.Topology;
using Vostok.ClusterConfig.Client.Helpers;
using Vostok.Logging.Console;

namespace Vostok.ClusterConfig.Client.Tests.Helpers
{
    [TestFixture]
    internal class ConfigurationMerger_Tests
    {
        [Test]
        public void Should_correctly_merge_two_default_settings_instances()
        {
            ConfigurationMerger.Merge(new ClusterConfigClientSettings(), new ClusterConfigClientSettings())
                .Should().BeEquivalentTo(new ClusterConfigClientSettings());
        }

        [Test]
        public void Should_preserve_non_default_values_from_base_settings()
        {
            foreach (var (propertyName, nonDefaultValue, _) in EnumerateNonDefaultValues())
            {
                var baseSettings = new ClusterConfigClientSettings();
                var userSettings = new ClusterConfigClientSettings();

                typeof(ClusterConfigClientSettings).GetProperty(propertyName).SetValue(baseSettings, nonDefaultValue);

                var mergedSettings = ConfigurationMerger.Merge(baseSettings, userSettings);

                typeof(ClusterConfigClientSettings).GetProperty(propertyName).GetValue(mergedSettings).Should().Be(nonDefaultValue);
            }
        }

        [Test]
        public void Should_preserve_non_default_values_from_user_settings()
        {
            foreach (var (propertyName, nonDefaultValue, _) in EnumerateNonDefaultValues())
            {
                var baseSettings = new ClusterConfigClientSettings();
                var userSettings = new ClusterConfigClientSettings();

                typeof(ClusterConfigClientSettings).GetProperty(propertyName).SetValue(userSettings, nonDefaultValue);

                var mergedSettings = ConfigurationMerger.Merge(baseSettings, userSettings);

                typeof(ClusterConfigClientSettings).GetProperty(propertyName).GetValue(mergedSettings).Should().Be(nonDefaultValue);
            }
        }

        [Test]
        public void Should_prefer_non_default_values_from_user_settings()
        {
            foreach (var (propertyName, nonDefaultValue, anotherNonDefaultValue) in EnumerateNonDefaultValues())
            {
                var baseSettings = new ClusterConfigClientSettings();
                var userSettings = new ClusterConfigClientSettings();

                typeof(ClusterConfigClientSettings).GetProperty(propertyName).SetValue(baseSettings, nonDefaultValue);
                typeof(ClusterConfigClientSettings).GetProperty(propertyName).SetValue(userSettings, anotherNonDefaultValue);

                var mergedSettings = ConfigurationMerger.Merge(baseSettings, userSettings);

                typeof(ClusterConfigClientSettings).GetProperty(propertyName).GetValue(mergedSettings).Should().Be(anotherNonDefaultValue);
            }
        }

        private IEnumerable<(string, object, object)> EnumerateNonDefaultValues()
        {
            yield return (nameof(ClusterConfigClientSettings.Zone), "my-zone", "my-zone-2");
            yield return (nameof(ClusterConfigClientSettings.AdditionalSetup), (ClusterClientSetup) (config => {}), (ClusterClientSetup)(config => { }));
            yield return (nameof(ClusterConfigClientSettings.Cluster), new FixedClusterProvider("http://localhost:123/"), new FixedClusterProvider(new string[]{}));
            yield return (nameof(ClusterConfigClientSettings.EnableLocalSettings), false, false);
            yield return (nameof(ClusterConfigClientSettings.EnableClusterSettings), false, false);
            yield return (nameof(ClusterConfigClientSettings.Log), new SynchronousConsoleLog(), new ConsoleLog());
            yield return (nameof(ClusterConfigClientSettings.CacheCapacity), 43534, 5345345);
            yield return (nameof(ClusterConfigClientSettings.LocalFolder), AppDomain.CurrentDomain.BaseDirectory, "folder");
            yield return (nameof(ClusterConfigClientSettings.MaximumFileSize), 435345, 14345);
            yield return (nameof(ClusterConfigClientSettings.RequestTimeout), 50.Seconds(), 51.Seconds());
            yield return (nameof(ClusterConfigClientSettings.UpdatePeriod), 1.Hours(), 2.Hours());
        }
    }
}