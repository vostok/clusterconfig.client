using System;
using System.IO;
using System.Threading;
using FluentAssertions;
using NUnit.Framework;
using Vostok.ClusterConfig.Client.Helpers;

namespace Vostok.ClusterConfig.Client.Tests
{
    [TestFixture]
    internal class DefaultSettingsProvider_Tests
    {
        private DirectoryInfo settingsFolder;

        [SetUp]
        public void TestSetup()
        {
            DefaultSettingsProvider.Reset();

            settingsFolder = new DirectoryInfo(ClusterConfigClientDefaults.LocalFolder);

            EnsureDirectory();
        }

        [TearDown]
        public void TearDown()
        {
            DefaultSettingsProvider.Reset();

            RemoveDirectory();
        }

        [Test]
        public void Should_return_default_settings_if_settings_folder_does_not_exist()
        {
            RemoveDirectory();

            DefaultSettingsProvider.Settings.Should().BeEquivalentTo(new ClusterConfigClientSettings());
        }

        [Test]
        public void Should_return_default_settings_if_settings_file_does_not_exist()
        {
            DefaultSettingsProvider.Settings.Should().BeEquivalentTo(new ClusterConfigClientSettings());
        }

        [Test, Combinatorial]
        public void Should_parse_enable_local_settings_flag(
            [Values(true, false)] bool value,
            [Values("EnableLocalSettings", "enableLocalSettings")] string name)
        {
            CreateFile($"{name} = {value}");

            DefaultSettingsProvider.Settings.Should().BeEquivalentTo(new ClusterConfigClientSettings { EnableLocalSettings = value });
        }

        [Test, Combinatorial]
        public void Should_parse_enable_cluster_settings_flag(
            [Values(true, false)] bool value,
            [Values("EnableClusterSettings", "enableClusterSettings")] string name)
        {
            CreateFile($"{name} = {value}");

            DefaultSettingsProvider.Settings.Should().BeEquivalentTo(new ClusterConfigClientSettings { EnableClusterSettings = value });
        }

        [Test, Combinatorial]
        public void Should_parse_zone(
            [Values("default", "forms-25")] string value,
            [Values("Zone", "clusterSettingsZoneName")] string name)
        {
            CreateFile($"{name} = {value}");

            DefaultSettingsProvider.Settings.Should().BeEquivalentTo(new ClusterConfigClientSettings { Zone = value });
        }

        [Test, Combinatorial]
        public void Should_parse_local_folder(
            [Values("settings", "my-settings")] string value,
            [Values("LocalFolder", "localSettingsDirectory")] string name)
        {
            CreateFile($"{name} = {value}");

            DefaultSettingsProvider.Settings.Should().BeEquivalentTo(new ClusterConfigClientSettings { LocalFolder = value });
        }

        [Test, Combinatorial]
        public void Should_parse_update_period(
            [Values("00:01:05")] string value,
            [Values("UpdatePeriod", "refreshPeriod")] string name)
        {
            var parsedValue = TimeSpan.Parse(value);

            CreateFile($"{name} = {value}");

            DefaultSettingsProvider.Settings.Should().BeEquivalentTo(new ClusterConfigClientSettings { UpdatePeriod = parsedValue });
        }

        [Test, Combinatorial]
        public void Should_parse_request_timeout(
            [Values("00:01:05")] string value,
            [Values("RequestTimeout", "requestTimeout")] string name)
        {
            var parsedValue = TimeSpan.Parse(value);

            CreateFile($"{name} = {value}");

            DefaultSettingsProvider.Settings.Should().BeEquivalentTo(new ClusterConfigClientSettings { RequestTimeout = parsedValue });
        }

        [Test]
        public void Should_parse_legacy_format_dns_endpoint()
        {
            CreateFile("clusterConfigHost = foo.bar:12345");

            var cluster = DefaultSettingsProvider.Settings.Cluster.Should().BeOfType<DnsClusterProvider>().Which;

            cluster.Dns.Should().Be("foo.bar");
            cluster.Port.Should().Be(12345);
        }

        [Test]
        public void Should_parse_server_dns()
        {
            CreateFile("ServerDNS = foo.bar");

            var cluster = DefaultSettingsProvider.Settings.Cluster.Should().BeOfType<DnsClusterProvider>().Which;

            cluster.Dns.Should().Be("foo.bar");
            cluster.Port.Should().Be(ClusterConfigClientDefaults.Port);
        }

        [Test]
        public void Should_parse_server_port()
        {
            CreateFile("ServerPort = 12345");

            var cluster = DefaultSettingsProvider.Settings.Cluster.Should().BeOfType<DnsClusterProvider>().Which;

            cluster.Dns.Should().Be(ClusterConfigClientDefaults.Dns);
            cluster.Port.Should().Be(12345);
        }

        [Test]
        public void Should_parse_server_dns_and_port_together()
        {
            CreateFile("ServerPort = 12345" + Environment.NewLine + "ServerDNS = foo.bar");

            var cluster = DefaultSettingsProvider.Settings.Cluster.Should().BeOfType<DnsClusterProvider>().Which;

            cluster.Dns.Should().Be("foo.bar");
            cluster.Port.Should().Be(12345);
        }

        [Test]
        public void Should_cache_returned_value()
        {
            var settings1 = DefaultSettingsProvider.Settings;
            var settings2 = DefaultSettingsProvider.Settings;

            settings2.Should().BeSameAs(settings1);
        }

        private void EnsureDirectory()
        {
            settingsFolder.Refresh();

            if (!settingsFolder.Exists)
                settingsFolder.Create();
        }

        private void RemoveDirectory()
        {
            settingsFolder.Refresh();

            for (var i = 0; i < 5; i++)
            {
                try
                {
                    if (settingsFolder.Exists)
                        settingsFolder.Delete(true);

                    return;
                }
                catch (UnauthorizedAccessException)
                {
                    Thread.Sleep(200);
                }
            }
        }

        private void CreateFile(string content)
        {
            File.WriteAllText(Path.Combine(settingsFolder.FullName, ClusterConfigClientDefaults.ConfigurationFile), content);
        }
    }
}