using System;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using Vostok.ClusterConfig.Client.Abstractions;
using Vostok.ClusterConfig.Client.Helpers;
using Vostok.Commons.Binary;
using Vostok.Commons.Collections;
using Vostok.Configuration.Abstractions.SettingsTree;

namespace Vostok.ClusterConfig.Client.Tests.Helpers
{
    [TestFixture(ClusterConfigProtocolVersion.V1)]
    [TestFixture(ClusterConfigProtocolVersion.V2)]
    internal class TreeExtractor_SingleTree_Tests
    {
        private readonly ClusterConfigProtocolVersion protocol;
        private ISettingsNode localTree;
        private ISettingsNode remoteTree;
        private ClusterConfigClientState state;

        public TreeExtractor_SingleTree_Tests(ClusterConfigProtocolVersion protocol) => this.protocol = protocol;

        [SetUp]
        public void TestSetup()
        {
            localTree = new ObjectNode(null, new ISettingsNode[]
            {
                new ObjectNode("foo", new ISettingsNode[]
                {
                    new ObjectNode("baz", new ISettingsNode[]
                    {
                        new ValueNode("key1", "value1"), 
                        new ValueNode("key2", "value2") 
                    }),
                    new ValueNode("key3", "value3")
                }),
                new ObjectNode("bar", new ISettingsNode[]
                {
                    new ValueNode("key4", "value4")
                })
            });

            remoteTree = new ObjectNode(null, new ISettingsNode[]
            {
                new ObjectNode("foo", new ISettingsNode[]
                {
                    new ObjectNode("baz", new ISettingsNode[]
                    {
                        new ValueNode("key5", "value5"),
                        new ValueNode("key6", "value6")
                    }),
                    new ValueNode("key3", "value7")
                }),
                new ArrayNode("bar", new ISettingsNode[]
                {
                    new ValueNode("0", "value8")
                })
            });

            state = null;
        }

        [TestCase("/")]
        [TestCase("/foo")]
        [TestCase("/foo/baz")]
        [TestCase("/foo/bar")]
        [TestCase("/foo/bar/baz")]
        public void Should_return_local_tree_result_when_remote_is_null(string path)
        {
            remoteTree = null;

            Extract(path).Should().Be(localTree.ScopeTo(new ClusterConfigPath(path).Segments));
        }

        [TestCase("/")]
        [TestCase("/foo")]
        [TestCase("/foo/baz")]
        [TestCase("/foo/bar")]
        [TestCase("/foo/bar/baz")]
        [TestCase("")]
        [TestCase("foo")]
        [TestCase("foo/baz")]
        [TestCase("foo/bar")]
        [TestCase("foo/bar/baz")]
        public void Should_return_remote_tree_result_when_local_is_null(string path)
        {
            localTree = null;

            Extract(path).Should().Be(remoteTree.ScopeTo(new ClusterConfigPath(path).Segments));
        }

        [Test]
        public void Local_settings_should_take_precedence_over_remote()
        {
            Extract("/").Should().Be(remoteTree.Merge(localTree));

            (Extract("/bar/key4")?.Value).Should().Be("value4");
            (Extract("/foo/key3")?.Value).Should().Be("value3");
            (Extract("/foo/baz/key1")?.Value).Should().Be("value1");
            (Extract("/foo/baz/key2")?.Value).Should().Be("value2");
            (Extract("/foo/baz/key5")?.Value).Should().Be("value5");
            (Extract("/foo/baz/key6")?.Value).Should().Be("value6");
        }

        [Test]
        public void Should_cache_values()
        {
            var tree1 = Extract("foo");
            var tree2 = Extract("foo");

            tree2.Should().BeSameAs(tree1).And.NotBeNull();
        }

        [Test]
        public void Should_deduplicate_trees_in_cache_by_prefix_to_save_memory()
        {
            var tree = Extract("foo");

            var subTree = Extract("foo/baz");

            subTree.Should().BeSameAs(tree["baz"]);
        }

        private ISettingsNode Extract(ClusterConfigPath path)
        {
            if (state == null)
            {
                RemoteTree remote;

                if (remoteTree == null)
                {
                    remote = null;
                }
                else
                {
                    var writer = new BinaryBufferWriter(64);
                    var cache = new RecyclingBoundedCache<string, string>(4);
                    protocol.GetSerializer(cache).Serialize(remoteTree, writer);

                    remote = new RemoteTree(new ArraySegment<byte>(writer.Buffer, 0, writer.Length), protocol.GetSerializer(cache), "Desc");
                }

                state = new ClusterConfigClientState(localTree, remote, null, new RecyclingBoundedCache<ClusterConfigPath, ISettingsNode>(10), Int64.MaxValue);
            }

            return TreeExtractor.Extract(state, path, null);
        }
    }
}