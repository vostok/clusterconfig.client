﻿using System;
using FluentAssertions;
using FluentAssertions.Extensions;
using NUnit.Framework;
using Vostok.Clusterclient.Core.Model;
using Vostok.Clusterclient.Core.Topology;
using Vostok.ClusterConfig.Client.Abstractions;
using Vostok.Commons.Testing;
using Vostok.Commons.Testing.Observable;
using Vostok.Configuration.Abstractions.SettingsTree;
using Vostok.Logging.Console;

namespace Vostok.ClusterConfig.Client.Tests.Functional
{
    [TestFixture]
    internal class FunctionalTests
    {
        private TestServer server;
        private TestFolder folder;

        private ClusterConfigClientSettings settings;
        private ClusterConfigClient client;

        private ISettingsNode remoteTree1;
        private ISettingsNode remoteTree2;
        private ISettingsNode localTree1;
        private ISettingsNode localTree2;

        private DateTime version1;
        private DateTime version2;

        [SetUp]
        public void TestSetup()
        {
            folder = new TestFolder();

            server = new TestServer();
            server.Start();

            settings = new ClusterConfigClientSettings
            {
                EnableLocalSettings = true,
                EnableClusterSettings = true,
                LocalFolder = folder.Directory.FullName,
                Cluster = new FixedClusterProvider(new Uri(server.Url)),
                UpdatePeriod = 250.Milliseconds(),
                Log = new ConsoleLog()
            };

            client = new ClusterConfigClient(settings);

            remoteTree1 = new ObjectNode(null, new ISettingsNode[]
            {
                new ValueNode("foo", "123"), 
                new ValueNode("bar", "456")
            });

            remoteTree2 = new ObjectNode(null, new ISettingsNode[]
            {
                new ValueNode("foo", "789"),
                new ValueNode("bar", "?!@")
            });

            localTree1 = new ObjectNode(null, new ISettingsNode[]
            {
                new ValueNode("local", "value-1"),
            });

            localTree2 = new ObjectNode(null, new ISettingsNode[]
            {
                new ValueNode("local", "value-2"),
            });

            version1 = new DateTime(1990, 12, 1, 13, 5, 45);
            version2 = version1 + 2.Minutes();
        }

        [TearDown]
        public void TearDown()
        {
            client.Dispose();
            folder.Dispose();
            server.Dispose();

            ConsoleLog.Flush();
        }

        [Test]
        public void Should_receive_local_tree_when_server_settings_are_disabled()
        {
            settings.EnableClusterSettings = false;

            folder.CreateFile("local", b => b.Append("value-1"));

            VerifyResults(default, 1, localTree1);
            VerifyResults("local", 1, localTree1["local"]);
        }

        [Test]
        public void Should_reflect_updates_in_local_tree_when_server_settings_are_disabled()
        {
            settings.EnableClusterSettings = false;

            folder.CreateFile("local", b => b.Append("value-1"));

            VerifyResults(default, 1, localTree1);

            folder.CreateFile("local", b => b.Append("value-2"));

            VerifyResults(default, 2, localTree2);

            folder.CreateFile("local", b => b.Append("value-1"));

            VerifyResults(default, 3, localTree1);
        }

        [Test]
        public void Should_receive_remote_tree_when_local_settings_are_disabled()
        {
            settings.EnableLocalSettings = false;

            server.SetResponse(remoteTree1, version1);

            VerifyResults(default, 1, remoteTree1);
            VerifyResults("foo", 1, remoteTree1["foo"]);
        }

        [Test]
        public void Should_reflect_updates_in_remote_tree_when_local_settings_are_disabled()
        {
            settings.EnableLocalSettings = false;

            server.SetResponse(remoteTree1, version1);

            VerifyResults(default, 1, remoteTree1);
            VerifyResults("foo", 1, remoteTree1["foo"]);

            server.SetResponse(remoteTree2, version2);

            VerifyResults(default, 2, remoteTree2);
            VerifyResults("foo", 2, remoteTree2["foo"]);
        }

        [Test]
        public void Should_merge_local_and_remote_trees()
        {
            folder.CreateFile("local", b => b.Append("value-1"));

            server.SetResponse(remoteTree1, version1);

            VerifyResults(default, 1, remoteTree1.Merge(localTree1));
        }

        [Test]
        public void Should_reflect_changes_in_both_local_and_remote_trees_at_the_same_time()
        {
            folder.CreateFile("local", b => b.Append("value-1"));

            server.SetResponse(remoteTree1, version1);

            VerifyResults(default, 1, remoteTree1.Merge(localTree1));

            server.SetResponse(remoteTree2, version2);

            VerifyResults(default, 2, remoteTree2.Merge(localTree1));

            folder.CreateFile("local", b => b.Append("value-2"));

            VerifyResults(default, 3, remoteTree2.Merge(localTree2));
        }

        [Test]
        public void Should_return_null_tree_when_everything_is_disabled()
        {
            settings.EnableLocalSettings = false;
            settings.EnableClusterSettings = false;

            VerifyResults(default, 1, null);
            VerifyResults("foo/bar", 1, null);
        }

        [Test]
        public void Should_return_null_tree_when_requesting_missing_path()
        {
            server.SetResponse(remoteTree1, version1);

            VerifyResults("foo/bar", 1, null);
        }

        [Test]
        public void Should_return_null_tree_when_requesting_missing_zone_without_local_settings()
        {
            settings.EnableLocalSettings = false;

            server.SetResponse(ResponseCode.NotFound);

            VerifyResults(default, 1, null);
        }

        [Test]
        public void Should_return_empty_tree_when_requesting_missing_zone_with_local_settings()
        {
            server.SetResponse(ResponseCode.NotFound);

            VerifyResults(default, 1, new ObjectNode(null, null));
        }

        [Test]
        public void Should_return_null_tree_when_there_are_no_replicas_without_local_settings()
        {
            settings.EnableLocalSettings = false;

            settings.Cluster = new FixedClusterProvider();

            VerifyResults(default, 1, null);
        }

        [Test]
        public void Should_return_null_tree_when_there_are_no_replicas_with_local_settings()
        {
            settings.Cluster = new FixedClusterProvider();

            VerifyResults(default, 1, new ObjectNode(null, null));
        }

        [Test]
        public void Should_return_same_tree_object_while_nothing_changes()
        {
            server.SetResponse(remoteTree1, version1);

            folder.CreateFile("local", b => b.Append("value-1"));

            var tree1 = client.Get("");
            var tree2 = client.Get("");

            tree2.Should().BeSameAs(tree1);
        }

        [Test]
        public void Should_not_downgrade_to_remote_settings_of_lesser_version()
        {
            server.SetResponse(remoteTree2, version2);

            VerifyResults("", 1, remoteTree2);

            server.SetResponse(remoteTree1, version1);

            Action assertion = () => VerifyResults("", 1, remoteTree2);

            assertion.ShouldNotFailIn(2.Seconds());
        }

        [Test]
        public void Should_not_change_existing_settings_when_server_says_nothing_has_been_modified()
        {
            server.SetResponse(remoteTree2, version2);

            VerifyResults("", 1, remoteTree2);

            server.SetResponse(ResponseCode.NotModified);

            Action assertion = () => VerifyResults("", 1, remoteTree2);

            assertion.ShouldNotFailIn(2.Seconds());
        }

        private void VerifyResults(ClusterConfigPath path, int expectedVersion, ISettingsNode expectedTree)
        {
            Action assertion = () =>
            {
                client.Get(path).Should().Be(expectedTree);

                client.GetWithVersion(path).settings.Should().Be(expectedTree);
                client.GetWithVersion(path).version.Should().Be(expectedVersion);

                client.GetAsync(path).GetAwaiter().GetResult().Should().Be(expectedTree);

                client.GetWithVersionAsync(path).GetAwaiter().GetResult().settings.Should().Be(expectedTree);
                client.GetWithVersionAsync(path).GetAwaiter().GetResult().version.Should().Be(expectedVersion);

                client.Observe(path).WaitFirstValue(1.Seconds()).Should().Be(expectedTree);

                client.ObserveWithVersions(path).WaitFirstValue(1.Seconds()).settings.Should().Be(expectedTree);
                client.ObserveWithVersions(path).WaitFirstValue(1.Seconds()).version.Should().Be(expectedVersion);
            };

            assertion.ShouldPassIn(10.Seconds());
        }
    }
}
