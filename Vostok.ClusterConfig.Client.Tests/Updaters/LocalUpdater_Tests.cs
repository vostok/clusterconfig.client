using System;
using System.IO;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using Vostok.ClusterConfig.Client.Updaters;
using Vostok.ClusterConfig.Core.Parsers;
using Vostok.Configuration.Abstractions.SettingsTree;

namespace Vostok.ClusterConfig.Client.Tests.Updaters
{
    [TestFixture]
    internal class LocalUpdater_Tests
    {
        private ISettingsNode previousTree;
        private ISettingsNode parsedTree;
        private IZoneParser zoneParser;
        private DirectoryInfo folder;
        private LocalUpdater enabledUpdater;
        private LocalUpdater disabledUpdater;

        [SetUp]
        public void TestSetup()
        {
            previousTree = Substitute.For<ISettingsNode>();
            parsedTree = Substitute.For<ISettingsNode>();

            zoneParser = Substitute.For<IZoneParser>();
            zoneParser.Parse(Arg.Any<DirectoryInfo>()).Returns(_ => parsedTree);

            folder = new DirectoryInfo(Guid.NewGuid().ToString());
            folder.Create();

            enabledUpdater = new LocalUpdater(true, folder, zoneParser);
            disabledUpdater = new LocalUpdater(false, null, null);
        }

        [TearDown]
        public void TearDown()
        {
            if (folder.Exists)
                folder.Delete(true);
        }

        [Test]
        public void Should_return_changed_null_tree_when_disabled_and_there_is_no_previous_result()
        {
            var result = disabledUpdater.Update(null);

            result.Changed.Should().BeTrue();
            result.Tree.Should().BeNull();
        }

        [Test]
        public void Should_return_changed_null_tree_when_disabled_and_there_is_non_null_previous_result()
        {
            var result = disabledUpdater.Update(new LocalUpdateResult(false, previousTree));

            result.Changed.Should().BeTrue();
            result.Tree.Should().BeNull();
        }

        [Test]
        public void Should_return_unchanged_null_tree_when_disabled_and_there_is_a_null_previous_result()
        {
            var result = disabledUpdater.Update(new LocalUpdateResult(false, null));

            result.Changed.Should().BeFalse();
            result.Tree.Should().BeNull();
        }

        [Test]
        public void Should_return_changed_null_tree_without_parsing_zone_if_folder_does_not_exist_and_there_is_no_previous_result()
        {
            folder.Delete(true);

            var result = enabledUpdater.Update(null);

            zoneParser.ReceivedCalls().Should().BeEmpty();

            result.Changed.Should().BeTrue();
            result.Tree.Should().BeNull();
        }

        [Test]
        public void Should_return_changed_null_tree_without_parsing_zone_if_folder_does_not_exist_and_there_is_non_null_previous_result()
        {
            folder.Delete(true);

            var result = enabledUpdater.Update(new LocalUpdateResult(false, previousTree));

            zoneParser.ReceivedCalls().Should().BeEmpty();

            result.Changed.Should().BeTrue();
            result.Tree.Should().BeNull();
        }

        [Test]
        public void Should_return_unchanged_null_tree_without_parsing_zone_if_folder_does_not_exist_and_there_is_a_null_previous_result()
        {
            folder.Delete(true);

            var result = enabledUpdater.Update(new LocalUpdateResult(false, null));

            zoneParser.ReceivedCalls().Should().BeEmpty();

            result.Changed.Should().BeFalse();
            result.Tree.Should().BeNull();
        }

        [Test]
        public void Should_return_changed_parsed_tree_if_there_is_no_previous_result()
        {
            var result = enabledUpdater.Update(null);

            result.Changed.Should().BeTrue();
            result.Tree.Should().BeSameAs(parsedTree);
        }

        [Test]
        public void Should_return_changed_parsed_tree_if_it_differs_from_previous_tree()
        {
            var result = enabledUpdater.Update(new LocalUpdateResult(false, previousTree));

            result.Changed.Should().BeTrue();
            result.Tree.Should().BeSameAs(parsedTree);
        }

        [Test]
        public void Should_return_unchanged_old_tree_if_new_one_is_equal_to_it()
        {
            var result = enabledUpdater.Update(new LocalUpdateResult(false, parsedTree));

            result.Changed.Should().BeFalse();
            result.Tree.Should().BeSameAs(parsedTree);
        }
    }
}