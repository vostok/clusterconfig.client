using System;
using System.Threading;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using Vostok.Clusterclient.Core;
using Vostok.ClusterConfig.Client.Updaters;
using Vostok.Logging.Abstractions;
using Vostok.Logging.Console;

namespace Vostok.ClusterConfig.Client.Tests.Updaters
{
    [TestFixture]
    internal class RemoteUpdater_Tests
    {
        private IClusterClient client;
        private ILog log;

        private RemoteUpdater enabledUpdater;
        private RemoteUpdater disabledUpdater;

        private CancellationTokenSource cancellation;

        [SetUp]
        public void TestSetup()
        {
            log = new ConsoleLog();

            client = Substitute.For<IClusterClient>();

            enabledUpdater = new RemoteUpdater(true, client, log, "default");
            disabledUpdater = new RemoteUpdater(false, null, log, "default");

            cancellation = new CancellationTokenSource();
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

        private RemoteUpdateResult Update(RemoteUpdateResult previous)
            => enabledUpdater.UpdateAsync(previous, cancellation.Token).GetAwaiter().GetResult();

        private RemoteUpdateResult UpdateDisabled(RemoteUpdateResult previous)
            => disabledUpdater.UpdateAsync(previous, cancellation.Token).GetAwaiter().GetResult();
    }
}