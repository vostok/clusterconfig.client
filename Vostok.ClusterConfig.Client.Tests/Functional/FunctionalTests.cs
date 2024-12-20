using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using FluentAssertions.Extensions;
using NSubstitute;
using NUnit.Framework;
using Vostok.Clusterclient.Core.Model;
using Vostok.Clusterclient.Core.Topology;
using Vostok.ClusterConfig.Client.Abstractions;
using Vostok.Commons.Testing;
using Vostok.Commons.Testing.Observable;
using Vostok.Configuration.Abstractions.SettingsTree;
using Vostok.Logging.Abstractions;
using Vostok.Logging.Console;

namespace Vostok.ClusterConfig.Client.Tests.Functional
{
    [TestFixture(ClusterConfigProtocolVersion.V1)]
    [TestFixture(ClusterConfigProtocolVersion.V2)]
    [TestFixture(ClusterConfigProtocolVersion.V3)]
    internal class FunctionalTests
    {
        private const string UpdatedTemplate = "Received new version of zone '{Zone}' from {Replica}. Size = {Size}. Version = {Version}. Protocol = {Protocol}. Patch = {IsPatch}. {ResponsesDescriptions}.";
        private const string HashMismatchTemplate = "Detected hash mismatch: {ActualHash} != {ExpectedHash}. New version is {NewVersion}, is patch: {IsPatch}. {ResponsesDescriptions}.";
        private const string ApplyPatchErrorTemplate = "Can't apply patch {PatchVersion} to {OldVersion} (protocol {Protocol}). {ResponsesDescriptions}.";

        private readonly ClusterConfigProtocolVersion protocol;
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
        private ILog log;

        public FunctionalTests(ClusterConfigProtocolVersion protocol) => this.protocol = protocol;

        [SetUp]
        public void TestSetup()
        {
            log = Substitute.For<ILog>();
            log.IsEnabledFor(Arg.Any<LogLevel>()).ReturnsForAnyArgs(true);
            log.ForContext(Arg.Any<string>()).ReturnsForAnyArgs(log);
            log.IsEnabledFor(LogLevel.Info).Should().BeTrue();
            
            folder = new TestFolder();

            server = new TestServer(protocol);
            server.Start();

            observer = new TestObserver<(ISettingsNode, long)>();

            settings = new ClusterConfigClientSettings
            {
                AdditionalSetup = s =>
                {
                    //To speed up tests which should be faced with Timeouts (default is 30 seconds)
                    s.DefaultTimeout = 10.Seconds();
                },
                EnableLocalSettings = true,
                EnableClusterSettings = true,
                LocalFolder = folder.Directory.FullName,
                Cluster = new FixedClusterProvider(new Uri(server.Url)),
                UpdatePeriod = 250.Milliseconds(),
                Log = new CompositeLog(log, new SynchronousConsoleLog()),
                ForcedProtocolVersion = protocol
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
                    new ObjectNode("local",
                        new ISettingsNode[]
                        {
                            new ValueNode(string.Empty, "value-1")
                        }),
                });

            localTree2 = new ObjectNode(
                null,
                new ISettingsNode[]
                {
                    new ObjectNode("local",
                        new ISettingsNode[]
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
        }

        [Test]
        public void Should_receive_local_tree_when_server_settings_are_disabled()
        {
            ModifySettings(s => s.EnableClusterSettings = false);

            folder.CreateFile("local", b => b.Append("value-1"));

            VerifyResults(default, 1, localTree1);
            VerifyResults("local", 1, localTree1["local"]);
        }

        [Test]
        public void Should_reflect_updates_in_local_tree_when_server_settings_are_disabled()
        {
            ModifySettings(s => s.EnableClusterSettings = false);

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
            using var _ = ShouldNotLog(LogLevel.Warn, LogLevel.Error, LogLevel.Fatal);

            ModifySettings(s => s.EnableLocalSettings = false);

            server.SetResponse(remoteTree1, version1);

            VerifyResults(default, version1.Ticks, remoteTree1);
            VerifyResults("foo", version1.Ticks, remoteTree1["foo"]);
        }

        [Test]
        public void Should_reflect_updates_in_remote_tree_when_local_settings_are_disabled()
        {
            using var _ = ShouldNotLog(LogLevel.Warn, LogLevel.Error, LogLevel.Fatal);

            ModifySettings(s => s.EnableLocalSettings = false);

            server.SetResponse(remoteTree1, version1);

            VerifyResults(default, version1.Ticks, remoteTree1);
            VerifyResults("foo", version1.Ticks, remoteTree1["foo"]);

            server.SetResponse(remoteTree2, version2);

            VerifyResults(default, version2.Ticks, remoteTree2);
            VerifyResults("foo", version2.Ticks, remoteTree2["foo"]);
        }

        [Test]
        public void Should_merge_local_and_remote_trees()
        {
            using var _ = ShouldNotLog(LogLevel.Warn, LogLevel.Error, LogLevel.Fatal);

            folder.CreateFile("local", b => b.Append("value-1"));

            server.SetResponse(remoteTree1, version1);

            VerifyResults(default, 1, remoteTree1.Merge(localTree1));
        }

        [Test]
        public void Should_reflect_changes_in_both_local_and_remote_trees_at_the_same_time()
        {
            using var _ = ShouldNotLog(LogLevel.Warn, LogLevel.Error, LogLevel.Fatal);

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
            ModifySettings(s =>
            {
                s.EnableLocalSettings = false;
                s.EnableClusterSettings = false;
            });

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
            ModifySettings(s => s.EnableLocalSettings = false);

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
            ModifySettings(s =>
            {
                s.EnableLocalSettings = false;
                s.Cluster = new FixedClusterProvider(Array.Empty<string>());
            });

            VerifyResults(default, 1, null);
        }

        [Test]
        public void Should_return_null_tree_when_there_are_no_replicas_with_local_settings()
        {
            ModifySettings(s => s.Cluster = new FixedClusterProvider(Array.Empty<string>()));

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
            using var _ = ShouldNotLog(LogLevel.Warn, LogLevel.Error, LogLevel.Fatal);

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
        public void Should_retry_initial_remote_update()
        {   
            var b = () =>
            {
                client.Get(ClusterConfigPath.Empty).Should().NotBeNull();
            };
            b.Should().Throw<ClusterConfigClientException>();
            
            Task.Delay(5.Seconds()).ContinueWith(_ => server.SetResponse(remoteTree1, version1));

            var a = () =>
            {
                b.Should().NotThrow();
            };
            //(deniaa): As we set http-timeout = 10 seconds and client used strategies with retry which can consume whole timeout, we definitely should give him two "attempts" (20 seconds). 
            a.ShouldPassIn(20.Seconds());
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

                VerifyObserverCaughtError();
            }

            observer = new TestObserver<(ISettingsNode, long)>();

            // (iloktionov): Resubscribe should fail immediately:
            using (client.ObserveWithVersions(default).Subscribe(observer))
            {
                VerifyObserverCaughtError();
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

                VerifyObserverCaughtError();
            }

            server.SetResponse(remoteTree1, version1);

            VerifyResults(default, 1, remoteTree1);

            observer = new TestObserver<(ISettingsNode, long)>();

            using (client.ObserveWithVersions(default).Subscribe(observer))
            {
                VerifyNotifications(Notification.CreateOnNext((remoteTree1, 1L)));
            }
        }

        [Test, MaxTime(8000)]
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
        
        [Test]
        public void Should_obtain_update_via_patch()
        {
            if (protocol != ClusterConfigProtocolVersion.V2)
                return;

            using var _ = ShouldNotLog(LogLevel.Warn, LogLevel.Error, LogLevel.Fatal);

            folder.CreateFile("local", b => b.Append("value-1"));

            server.SetResponse(remoteTree1, version1);

            VerifyResults(default, 1, remoteTree1.Merge(localTree1));
            
            server.SetPatchResponse(remoteTree1, remoteTree2, version2, false);
            
            VerifyResults(default, 2, remoteTree2.Merge(localTree1));
            
            log.Received().Log(Arg.Is<LogEvent>(e => e.Level == LogLevel.Info && e.MessageTemplate == UpdatedTemplate && e.Properties["IsPatch"].ToString() == "True"));
        }
        
        [Test]
        public void Should_obtain_update_via_full_zone_when_patch_hash_mismatched()
        {
            if (protocol != ClusterConfigProtocolVersion.V2)
                return;
            
            folder.CreateFile("local", b => b.Append("value-1"));

            server.SetResponse(remoteTree1, version1);

            VerifyResults(default, 1, remoteTree1.Merge(localTree1));
            
            server.SetPatchResponse(remoteTree1, remoteTree2, version2, true);
            
            ((Action) (() => log.Received().Log(Arg.Is<LogEvent>(e => e.Level == LogLevel.Warn && e.MessageTemplate == HashMismatchTemplate)))).ShouldPassIn(10.Seconds());
            
            ((Action) (() => server.AssertRequestUrl(r => r.Query.Should().Contain("forceFull=HashMismatch")))).ShouldPassIn(10.Seconds());
            
            log.ClearReceivedCalls();
            
            server.SetResponse(remoteTree2, version2);
            
            VerifyResults(default, 2, remoteTree2.Merge(localTree1));
            
            log.Received().Log(Arg.Is<LogEvent>(e => e.Level == LogLevel.Info && e.MessageTemplate == UpdatedTemplate && e.Properties["IsPatch"].ToString() == "False"));
        }
        
        [Test]
        public void Should_obtain_update_via_full_zone_when_patch_is_broken()
        {
            if (protocol != ClusterConfigProtocolVersion.V2)
                return;
            
            folder.CreateFile("local", b => b.Append("value-1"));

            server.SetResponse(remoteTree1, version1);

            VerifyResults(default, 1, remoteTree1.Merge(localTree1));
            
            var buffer = new MemoryStream();

            using (var gzip = new GZipStream(buffer, CompressionMode.Compress))
            {
                gzip.Write(Enumerable.Range(0, 100).Select(i => (byte) i).ToArray(), 0, 100);
                gzip.Flush();
            }

            server.SetPatchResponse(buffer.ToArray(), "blablabla", version2);
            
            ((Action) (() => log.Received().Log(Arg.Is<LogEvent>(e => e.Level == LogLevel.Error && e.MessageTemplate == ApplyPatchErrorTemplate && e.Exception != null)))).ShouldPassIn(10.Seconds());
            
            ((Action) (() => server.AssertRequestUrl(r => r.Query.Should().Contain("forceFull=ApplyPatchFailed")))).ShouldPassIn(10.Seconds());
            
            log.ClearReceivedCalls();
            
            server.SetResponse(remoteTree2, version2);
            
            VerifyResults(default, 2, remoteTree2.Merge(localTree1));
            
            log.Received().Log(Arg.Is<LogEvent>(e => e.Level == LogLevel.Info && e.MessageTemplate == UpdatedTemplate && e.Properties["IsPatch"].ToString() == "False"));
        }

        [Test]
        public void Observables_should_reflect_empty_update()
        {
            if (protocol != ClusterConfigProtocolVersion.V2)
                return;

            using var _ = ShouldNotLog(LogLevel.Warn, LogLevel.Error, LogLevel.Fatal);

            folder.CreateFile("local", b => b.Append("value-1"));

            server.SetResponse(remoteTree1, version1);

            VerifyResults(default, 1, remoteTree1.Merge(localTree1));
            
            server.SetEmptyPatchResponse(remoteTree1, version2);
            
            VerifyResults(default, 2, remoteTree1.Merge(localTree1));
            
            log.Received().Log(Arg.Is<LogEvent>(e => e.Level == LogLevel.Info && e.MessageTemplate == UpdatedTemplate && e.Properties["IsPatch"].ToString() == "True"));
        }

        [Test]
        [Repeat(10)]
        //(deniaa): There were a races between several observables even in sequential call. This probability test check that we have no such races.
        public void Test_V3_sequential_observe()
        {
            server.SetResponse(remoteTree1, version1);

            var fooObs = client.Observe("foo");
            fooObs.WaitFirstValue(5.Seconds()).Should().Be(remoteTree1["foo"]);
            
            var barObs = client.Observe("bar");
            barObs.WaitFirstValue(5.Seconds()).Should().Be(remoteTree1["bar"]);
            
            var bazObs = client.Observe("baz");
            bazObs.WaitFirstValue(5.Seconds()).Should().Be(remoteTree1["baz"]);
        }

        private void ModifySettings(Action<ClusterConfigClientSettings> modify)
        {
            modify(settings);

            client = new ClusterConfigClient(settings);
        }

        private void VerifyResults(ClusterConfigPath path, long expectedVersion, ISettingsNode expectedTree, bool includeObservables = true)
        {
            Action assertion = () =>
            {
                var action = new Action(() =>
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
                });

                action.Should().NotThrow();
            };

            assertion.ShouldPassIn(20.Seconds(), 100.Milliseconds());
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

        private void VerifyObserverCaughtError()
        {
            Action assertion = () => observer.Messages.Should().ContainSingle().Which.Kind.Should().Be(NotificationKind.OnError);

            assertion.ShouldPassIn(10.Seconds());
        }

        private IDisposable ShouldNotLog(params LogLevel[] deniedLevels)
        {
            log.ClearReceivedCalls();

            return new ActionDisposable(() => log.DidNotReceive().Log(Arg.Is<LogEvent>(e => deniedLevels.Contains(e.Level))));
        }

        private class ActionDisposable : IDisposable
        {
            private readonly Action action;

            public ActionDisposable(Action action) => this.action = action;

            public void Dispose() => action();
        }
    }
}
