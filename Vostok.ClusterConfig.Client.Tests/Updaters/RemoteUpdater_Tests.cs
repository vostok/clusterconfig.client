using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using FluentAssertions;
using FluentAssertions.Extensions;
using NSubstitute;
using NUnit.Framework;
using Vostok.Clusterclient.Core;
using Vostok.Clusterclient.Core.Model;
using Vostok.ClusterConfig.Client.Abstractions;
using Vostok.ClusterConfig.Client.Exceptions;
using Vostok.ClusterConfig.Client.Helpers;
using Vostok.ClusterConfig.Client.Updaters;
using Vostok.ClusterConfig.Core.Serialization;
using Vostok.Commons.Collections;
using Vostok.Logging.Abstractions;
using Vostok.Logging.Console;

// ReSharper disable AssignNullToNotNullAttribute

namespace Vostok.ClusterConfig.Client.Tests.Updaters
{
    [TestFixture(ClusterConfigProtocolVersion.V1)]
    [TestFixture(ClusterConfigProtocolVersion.V2)]
    internal class RemoteUpdater_Tests
    {
        private readonly ClusterConfigProtocolVersion protocol;
        private IClusterClient client;
        private ILog log;

        private RemoteUpdater enabledUpdater;
        private RemoteUpdater disabledUpdater;

        private CancellationTokenSource cancellation;

        private RemoteTree tree1;
        private RemoteTree tree2;

        private DateTime version1;
        private DateTime version2;

        public RemoteUpdater_Tests(ClusterConfigProtocolVersion protocol) => this.protocol = protocol;

        [SetUp]
        public void TestSetup()
        {
            log = new ConsoleLog();

            client = Substitute.For<IClusterClient>();

            enabledUpdater = new RemoteUpdater(true, client, new RecyclingBoundedCache<string, string>(4), log, "default");
            disabledUpdater = new RemoteUpdater(false, null, new RecyclingBoundedCache<string, string>(4), log, "default");

            cancellation = new CancellationTokenSource();

            var cache = new RecyclingBoundedCache<string, string>(4);
            tree1 = new RemoteTree(protocol, Guid.NewGuid().ToByteArray(), protocol.GetSerializer(cache), "T1 Desc");
            tree2 = new RemoteTree(protocol, Guid.NewGuid().ToByteArray(), protocol.GetSerializer(cache), "T2 Desc");

            version1 = DateTime.UtcNow;
            version1 = new DateTime(version1.Year, version1.Month, version1.Day, version1.Hour, version1.Minute, version1.Second, 0, version1.Kind);
            version2 = version1 + 1.Hours();
        }

        [TearDown]
        public void TearDown()
        {
            ConsoleLog.Flush();
        }

        [Test]
        public void Disabled_updater_should_return_a_changed_empty_tree_when_given_null_previous_result()
        {
            var result = UpdateDisabled(null);

            result.Changed.Should().BeTrue();
            result.Tree.Should().BeNull();
            result.Version.Should().Be(DateTime.MinValue);
        }

        [Test]
        public void Disabled_updater_should_return_unchanged_empty_trees_starting_from_second_call()
        {
            var result = UpdateDisabled(null);

            result = UpdateDisabled(result);

            result.Changed.Should().BeFalse();
            result.Tree.Should().BeNull();

            result = UpdateDisabled(result);

            result.Changed.Should().BeFalse();
            result.Tree.Should().BeNull();
        }

        [Test]
        public void Should_fail_with_cancellation_exception_if_given_a_canceled_token()
        {
            cancellation.Cancel();

            Action action = () => Update(null);

            action.Should().Throw<OperationCanceledException>();
        }

        [Test]
        public void Should_return_an_empty_unchanged_tree_when_no_replicas_are_resolved_and_there_is_no_previous_tree()
        {
            SetupResponse(ClusterResultStatus.ReplicasNotFound);

            var result1 = Update(null);
            var result2 = Update(result1);

            result1.Changed.Should().BeTrue();
            result1.Tree.Should().BeNull();
            result1.Version.Should().Be(DateTime.MinValue);

            result2.Changed.Should().BeFalse();
            result2.Tree.Should().BeNull();
            result2.Version.Should().Be(DateTime.MinValue);
        }

        [Test]
        public void Should_fail_with_an_error_when_no_replicas_are_resolved_and_there_is_a_not_empty_previous_tree()
        {
            SetupResponse(ClusterResultStatus.ReplicasNotFound);

            Action action = () => Update(new RemoteUpdateResult(false, tree1, version1, null, null));

            Console.Out.WriteLine(action.Should().Throw<RemoteUpdateException>().Which);
        }

        [TestCase(ClusterResultStatus.IncorrectArguments)]
        [TestCase(ClusterResultStatus.Throttled)]
        [TestCase(ClusterResultStatus.TimeExpired)]
        [TestCase(ClusterResultStatus.UnexpectedException)]
        public void Should_fail_with_an_error_on_given_failure_result_status(ClusterResultStatus status)
        {
            SetupResponse(status);

            Action action = () => Update(new RemoteUpdateResult(false, tree1, version1, null, null));

            Console.Out.WriteLine(action.Should().Throw<RemoteUpdateException>().Which);
        }

        [Test]
        public void Should_fail_with_an_error_when_all_replicas_return_failure_responses()
        {
            SetupResponse(ClusterResultStatus.ReplicasExhausted, 
                ResponseCode.RequestTimeout,
                ResponseCode.BadRequest,
                ResponseCode.ConnectFailure,
                ResponseCode.SendFailure,
                ResponseCode.ReceiveFailure,
                ResponseCode.ServiceUnavailable);

            Action action = () => Update(new RemoteUpdateResult(false, tree1, version1, null, null));

            Console.Out.WriteLine(action.Should().Throw<RemoteUpdateException>().Which);
        }

        [Test]
        public void Should_return_an_empty_tree_when_no_replica_returns_200_or_304_but_at_least_one_returns_404()
        {
            SetupResponse(ClusterResultStatus.ReplicasExhausted,
                ResponseCode.RequestTimeout,
                ResponseCode.NotFound,
                ResponseCode.ConnectFailure);

            var result1 = Update(null);
            var result2 = Update(result1);

            result1.Changed.Should().BeTrue();
            result1.Tree.Should().BeNull();
            result1.Version.Should().Be(DateTime.MinValue);

            result2.Changed.Should().BeFalse();
            result2.Tree.Should().BeNull();
            result2.Version.Should().Be(DateTime.MinValue);
        }

        [Test]
        public void Should_fail_with_an_error_when_receiving_a_304_response_for_initial_request()
        {
            SetupResponse(ResponseCode.NotModified);

            Action action = () => Update(null);

            Console.Out.WriteLine(action.Should().Throw<RemoteUpdateException>().Which);
        }

        [Test]
        public void Should_return_un_unchanged_previous_tree_when_receiving_a_304_response()
        {
            SetupResponse(ResponseCode.NotModified);

            var result = Update(new RemoteUpdateResult(true, tree1, version1, null, null));

            result.Changed.Should().BeFalse();
            result.Tree.Should().BeSameAs(tree1);
            result.Version.Should().Be(version1);
        }

        [Test]
        public void Should_fail_with_an_error_when_receiving_a_200_response_without_content()
        {
            SetupResponse(null, version1);

            Action action = () => Update(null);

            Console.Out.WriteLine(action.Should().Throw<RemoteUpdateException>().Which);
        }

        [Test]
        public void Should_fail_with_an_error_when_receiving_a_200_response_without_version()
        {
            SetupResponse(tree1.Serialized, null);

            Action action = () => Update(null);

            Console.Out.WriteLine(action.Should().Throw<RemoteUpdateException>().Which);
        }

        [Test]
        public void Should_return_an_unchanged_tree_when_response_version_is_less_than_last_version()
        {
            SetupResponse(tree1.Serialized, version1);

            var result = Update(new RemoteUpdateResult(false, tree2, version2, null, null));

            result.Changed.Should().BeFalse();
            result.Tree.Should().BeSameAs(tree2);
            result.Version.Should().Be(version2);
        }

        [Test]
        public void Should_return_a_changed_tree_when_receiving_first_200_response()
        {
            SetupResponse(tree1.Serialized, version1);

            var result = Update(null);

            result.Changed.Should().BeTrue();
            result.Tree?.Serialized.Should().Equal(tree1.Serialized);
            result.Version.Should().Be(version1);
        }

        [Test]
        public void Should_return_a_changed_tree_when_receiving_a_new_version_of_settings()
        {
            SetupResponse(tree2.Serialized, version2);

            var result = Update(new RemoteUpdateResult(false, tree1, version1, null, null));

            result.Changed.Should().BeTrue();
            result.Tree?.Serialized.Should().Equal(tree2.Serialized);
            result.Version.Should().Be(version2);
        }

        [Test]
        public void Should_return_an_unchanged_tree_when_receiving_the_same_version_of_settings()
        {
            SetupResponse(tree2.Serialized, version2);

            var result = Update(new RemoteUpdateResult(false, tree2, version2, null, null));

            result.Changed.Should().BeFalse();
            result.Tree?.Serialized.Should().Equal(tree2.Serialized);
            result.Version.Should().Be(version2);
        }

        private RemoteUpdateResult Update(RemoteUpdateResult previous)
            => enabledUpdater.UpdateAsync(protocol, previous, cancellation.Token).GetAwaiter().GetResult();

        private RemoteUpdateResult UpdateDisabled(RemoteUpdateResult previous)
            => disabledUpdater.UpdateAsync(protocol, previous, cancellation.Token).GetAwaiter().GetResult();

        private void SetupResponse(ClusterResultStatus status, params ResponseCode[] responses)
        {
            client
                .SendAsync(Arg.Any<Request>())
                .ReturnsForAnyArgs(new ClusterResult(status, responses.Select(r => new ReplicaResult(null, new Response(r), ResponseVerdict.Reject, TimeSpan.Zero)).ToList(), null, null));
        }

        private void SetupResponse(ResponseCode code)
        {
            client
                .SendAsync(Arg.Any<Request>())
                .ReturnsForAnyArgs(new ClusterResult(ClusterResultStatus.Success, new List<ReplicaResult>(), new Response(code), null));
        }

        private void SetupResponse(byte[] content, DateTime? version)
        {
            var response = Responses.Ok;

            if (content != null)
                response = response.WithContent(content);

            if (version.HasValue)
                response = response.WithHeader(HeaderNames.LastModified, version.Value.ToString("R"));

            client
                .SendAsync(Arg.Any<Request>())
                .ReturnsForAnyArgs(new ClusterResult(ClusterResultStatus.Success, new List<ReplicaResult>(), response, null));
        }
    }
}