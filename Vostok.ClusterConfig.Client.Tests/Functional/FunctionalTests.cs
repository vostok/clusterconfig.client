using System;
using System.Linq;
using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Threading;
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
        private TestObserver<(ISettingsNode, long)> observer;

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

            observer = new TestObserver<(ISettingsNode, long)>();

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

            remoteTree1 = new ObjectNode(
                null,
                new ISettingsNode[]
                {
                    new ValueNode("foo", "123"),
                    new ValueNode("bar", "456"),
                    new ValueNode("baz", "1")
                });

            remoteTree2 = new ObjectNode(
                null,
                new ISettingsNode[]
                {
                    new ValueNode("foo", "789"),
                    new ValueNode("bar", "?!@"),
                    new ValueNode("baz", "1")
                });

            localTree1 = new ObjectNode(
                null,
                new ISettingsNode[]
                {
                    new ObjectNode("local", new ISettingsNode[]
                    {
                        new ValueNode(string.Empty, "value-1")
                    }),
                });

            localTree2 = new ObjectNode(
                null,
                new ISettingsNode[]
                {
                    new ObjectNode("local", new ISettingsNode[]
                    {
                        new ValueNode(string.Empty, "value-2")
                    }),
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

        [Test]
        public void Should_throw_errors_if_initial_update_fails()
        {
            VerifyError();
        }

        [Test]
        public void Should_stay_calm_and_return_cached_data_if_a_regular_update_fails()
        {
            server.SetResponse(remoteTree2, version2);

            VerifyResults("", 1, remoteTree2);

            server.SetResponse(ResponseCode.ServiceUnavailable);

            Action assertion = () => VerifyResults("", 1, remoteTree2);

            assertion.ShouldNotFailIn(3.Seconds());
        }

        [Test]
        public void Dispose_before_initial_update_should_cause_any_queries_to_fail_with_an_error()
        {
            client.Dispose();

            VerifyDisposedError();
        }

        [Test]
        public void Dispose_after_successful_initial_update_should_not_invalidate_caches()
        {
            server.SetResponse(remoteTree2, version2);

            VerifyResults("", 1, remoteTree2);

            client.Dispose();

            VerifyResults("", 1, remoteTree2, false);
        }

        [Test]
        public void Dispose_after_failed_initial_update_should_not_change_observed_error()
        {
            VerifyError();

            client.Dispose();

            VerifyError();
        }

        [Test]
        public void Observables_should_reflect_updates()
        {
            server.SetResponse(remoteTree1, version1);

            var notification1 = Notification.CreateOnNext((remoteTree1, 1L));
            var notification2 = Notification.CreateOnNext((remoteTree2, 2L));

            using (client.ObserveWithVersions(default).Subscribe(observer))
            {
                VerifyNotifications(notification1);

                server.SetResponse(remoteTree2, version2);

                VerifyNotifications(notification1, notification2);
            }
        }

        [Test]
        public void Observables_should_deduplicate_unchanged_subtrees_during_updates()
        {
            server.SetResponse(remoteTree1, version1);

            var notification = Notification.CreateOnNext((remoteTree1["baz"], 1L));

            using (client.ObserveWithVersions("baz").Subscribe(observer))
            {
                VerifyNotifications(notification);

                server.SetResponse(remoteTree2, version2);

                Action assertion = () => VerifyNotifications(notification);

                assertion.ShouldNotFailIn(2.Seconds());
            }
        }

        [Test]
        public void Observables_should_fail_with_OnError_if_initial_update_fails()
        {
            using (client.ObserveWithVersions(default).Subscribe(observer))
            {
                VerifyError();

                observer.Messages.Should().ContainSingle().Which.Kind.Should().Be(NotificationKind.OnError);
            }

            observer = new TestObserver<(ISettingsNode, long)>();

            // (iloktionov): Resubscribe should fail immediately:
            using (client.ObserveWithVersions(default).Subscribe(observer))
            {
                observer.Messages.Should().ContainSingle().Which.Kind.Should().Be(NotificationKind.OnError);
            }
        }

        [Test]
        public void Observables_should_support_unsubscribe_operation()
        {
            server.SetResponse(remoteTree1, version1);

            var notification = Notification.CreateOnNext((remoteTree1, 1L));

            using (client.ObserveWithVersions(default).Subscribe(observer))
            {
                VerifyNotifications(notification);
            }

            server.SetResponse(remoteTree2, version2);

            Action assertion = () => VerifyNotifications(notification);

            assertion.ShouldNotFailIn(2.Seconds());
        }

        [Test]
        public void Observables_should_not_produce_OnError_notifications_if_regular_updates_fail()
        {
            server.SetResponse(remoteTree1, version1);

            var notification = Notification.CreateOnNext((remoteTree1, 1L));

            using (client.ObserveWithVersions(default).Subscribe(observer))
            {
                VerifyNotifications(notification);

                server.SetResponse(ResponseCode.ServiceUnavailable);

                Action assertion = () => VerifyNotifications(notification);

                assertion.ShouldNotFailIn(2.Seconds());
            }
        }

        [Test]
        public void Observables_should_be_recoverable_with_delayed_resubscription()
        {
            using (client.ObserveWithVersions(default).Subscribe(observer))
            {
                VerifyError();

                observer.Messages.Should().ContainSingle().Which.Kind.Should().Be(NotificationKind.OnError);
            }

            server.SetResponse(remoteTree1, version1);

            VerifyResults(default, 1, remoteTree1);

            observer = new TestObserver<(ISettingsNode, long)>();

            using (client.ObserveWithVersions(default).Subscribe(observer))
            {
                VerifyNotifications(Notification.CreateOnNext((remoteTree1, 1L)));
            }
        }

        [Test, MaxTime(5000)]
        public void Observables_should_protect_internal_updater_from_hanging_observers()
        {
            var subscription = client.ObserveWithVersions(default)
                .SubscribeOn(ThreadPoolScheduler.Instance)
                .Subscribe(_ => Thread.Sleep(Timeout.Infinite));

            using (subscription)
            {
                server.SetResponse(remoteTree1, version1);

                VerifyResults(default, 1, remoteTree1, false);

                server.SetResponse(remoteTree2, version2);

                VerifyResults(default, 2, remoteTree2, false);
            }
        }

        [Test]
        public void Dispose_before_initial_update_should_terminate_all_observers_with_an_error()
        {
            client.Dispose();

            using (client.ObserveWithVersions(default).Subscribe(observer))
            {
                Action assertion = () => observer.Messages.Should().ContainSingle().Which.Exception.Should().BeOfType<ObjectDisposedException>();

                assertion.ShouldPassIn(5.Seconds());
            }
        }

        [Test]
        public void Dispose_after_successful_initial_update_should_terminate_all_observers_with_an_error()
        {
            server.SetResponse(remoteTree1, version1);

            VerifyResults(default, 1, remoteTree1);

            client.Dispose();

            using (client.ObserveWithVersions(default).Subscribe(observer))
            {
                Action assertion = () => observer.Messages.Last().Exception.Should().BeOfType<ObjectDisposedException>();

                assertion.ShouldPassIn(5.Seconds());
            }
        }

        private void VerifyResults(ClusterConfigPath path, int expectedVersion, ISettingsNode expectedTree, bool includeObservables = true)
        {
            Action assertion = () =>
            {
                try
                {
                    client.Get(path).Should().Be(expectedTree);

                    client.GetWithVersion(path).settings.Should().Be(expectedTree);
                    client.GetWithVersion(path).version.Should().Be(expectedVersion);

                    client.GetAsync(path).GetAwaiter().GetResult().Should().Be(expectedTree);

                    client.GetWithVersionAsync(path).GetAwaiter().GetResult().settings.Should().Be(expectedTree);
                    client.GetWithVersionAsync(path).GetAwaiter().GetResult().version.Should().Be(expectedVersion);

                    if (includeObservables)
                    {
                        client.Observe(path).WaitFirstValue(1.Seconds()).Should().Be(expectedTree);

                        client.ObserveWithVersions(path).WaitFirstValue(1.Seconds()).settings.Should().Be(expectedTree);
                        client.ObserveWithVersions(path).WaitFirstValue(1.Seconds()).version.Should().Be(expectedVersion);
                    }
                }
                catch (ClusterConfigClientException error)
                {
                    throw new AssertionException("", error);
                }
            };

            assertion.ShouldPassIn(10.Seconds());
        }

        private void VerifyError()
        {
            VerifyError(() => client.Get(default));
            VerifyError(() => client.GetWithVersion(default));
            VerifyError(() => client.GetAsync(default).GetAwaiter().GetResult());
            VerifyError(() => client.GetWithVersionAsync(default).GetAwaiter().GetResult());
        }

        private void VerifyDisposedError()
        {
            VerifyDisposedError(() => client.Get(default));
            VerifyDisposedError(() => client.GetWithVersion(default));
            VerifyDisposedError(() => client.GetAsync(default).GetAwaiter().GetResult());
            VerifyDisposedError(() => client.GetWithVersionAsync(default).GetAwaiter().GetResult());
        }

        private static void VerifyError(Action action)
            => action.Should().Throw<ClusterConfigClientException>().Which.ShouldBePrinted();

        private static void VerifyDisposedError(Action action)
            => action.Should().Throw<ObjectDisposedException>().Which.ShouldBePrinted();

        private void VerifyNotifications(params Notification<(ISettingsNode, long)>[] expected)
        {
            Action assertion = () => observer.Messages.Should().Equal(expected);

            assertion.ShouldPassIn(10.Seconds());
        }
    }
}
