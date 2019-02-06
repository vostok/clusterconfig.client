using System;
using System.IO;
using FluentAssertions;
using NUnit.Framework;
using Vostok.ClusterConfig.Client.Helpers;

namespace Vostok.ClusterConfig.Client.Tests.Helpers
{
    [TestFixture]
    internal class FolderLocator_Tests
    {
        private string root;
        private string startingPoint;
        private string soughtFolder;

        [SetUp]
        public void TestSetup()
        {
            root = Guid.NewGuid().ToString();
            soughtFolder = Guid.NewGuid().ToString();

            Directory.CreateDirectory(startingPoint = Path.Combine(root, "1", "2", "3"));
        }

        [TearDown]
        public void TearDown()
        {
            Directory.Delete(root, true);
        }

        [Test]
        public void Should_locate_directory_at_starting_level()
        {
            var actual = Directory.CreateDirectory(Path.Combine(startingPoint, soughtFolder));

            Locate().FullName.Should().Be(actual.FullName);
        }

        [Test]
        public void Should_locate_directory_at_one_level_higher()
        {
            var actual = Directory.CreateDirectory(Path.Combine(startingPoint, "..", soughtFolder));

            Locate().FullName.Should().Be(actual.FullName);
        }

        [Test]
        public void Should_locate_directory_at_two_levels_higher()
        {
            var actual = Directory.CreateDirectory(Path.Combine(startingPoint, "..", "..", soughtFolder));

            Locate().FullName.Should().Be(actual.FullName);
        }

        [Test]
        public void Should_ignore_other_directories()
        {
            Directory.CreateDirectory(Path.Combine(startingPoint, Guid.NewGuid().ToString()));

            var actual = Directory.CreateDirectory(Path.Combine(startingPoint, "..", "..", soughtFolder));

            Locate().FullName.Should().Be(actual.FullName);
        }

        [Test]
        public void Should_locate_in_starting_point_when_max_hops_count_is_insufficient_to_reach_directory()
        {
            Directory.CreateDirectory(Path.Combine(startingPoint, "..", "..", soughtFolder));

            Locate(1).FullName.Should().Be(Path.GetFullPath(Path.Combine(startingPoint, soughtFolder)));
        }

        [Test]
        public void Should_locate_in_starting_point_when_sought_directory_does_not_exist()
        {
            Locate(1).FullName.Should().Be(Path.GetFullPath(Path.Combine(startingPoint, soughtFolder)));
        }

        private DirectoryInfo Locate(int maxOutwardHops = 3)
            => FolderLocator.Locate(startingPoint, soughtFolder, maxOutwardHops);
    }
}