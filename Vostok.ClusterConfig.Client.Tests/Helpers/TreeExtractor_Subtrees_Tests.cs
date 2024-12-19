using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Disposables;
using System.Text;
using FluentAssertions;
using NUnit.Framework;
using Vostok.ClusterConfig.Client.Abstractions;
using Vostok.ClusterConfig.Client.Helpers;
using Vostok.ClusterConfig.Core.Serialization.V2;
using Vostok.Commons.Binary;
using Vostok.Commons.Collections;
using Vostok.Configuration.Abstractions.SettingsTree;

namespace Vostok.ClusterConfig.Client.Tests.Helpers;

[TestFixture]
internal class TreeExtractor_Subtrees_Tests
{
    private ClusterConfigClientState state;
    private ISettingsNode remoteTree;
    private ISettingsNode localTree;
    private HashSet<string> observingSubtrees;
    private const ClusterConfigProtocolVersion Protocol = ClusterConfigProtocolVersion.V3;

    [SetUp]
    public void SetUp()
    {
        localTree = new ObjectNode(null, new ISettingsNode[]
        {
            new ObjectNode("foo", new ISettingsNode[]
            {
                new ObjectNode("baz", new ISettingsNode[]
                {
                    new ValueNode("key1", "value1"), 
                    new ValueNode("key2", "value2"),
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
                    new ValueNode("key6", "value6"),
                
                    new ObjectNode("nesting1", new ISettingsNode[]
                    {
                        new ObjectNode("nesting2", new ISettingsNode[]
                        {
                            new ValueNode("key10", "value10"), 
                        }),
                    }),
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
    [TestCase("")]
    [TestCase("foo")]
    [TestCase("foo/baz")]
    [TestCase("foo/bar")]
    [TestCase("foo/bar/baz")]
    public void Should_work_as_single_tree_if_we_have_only_root(string path)
    {
        localTree = null;
        observingSubtrees = new HashSet<string> {""};

        var extracted = Extract(path);
        var expected = remoteTree.ScopeTo(new ClusterConfigPath(path).Segments);
        extracted.Should().Be(expected);
    }

    [TestCase("", false)]
    [TestCase("foo", true)]
    [TestCase("foo/baz", true)]
    [TestCase("foo/baz/nesting1", true)]
    [TestCase("foo/baz/nesting1/nesting2", true)]
    [TestCase("foo/bar", true)]
    [TestCase("foo/bar/baz", true)]
    [TestCase("bar", false)]
    public void Should_return_all_subtrees_of_stored_subtree(string path, bool exists)
    {
        localTree = null;
        observingSubtrees = new HashSet<string> {"foo"};

        var settingsNode = Extract(path);
        if (!exists)
        {
            settingsNode.Should().BeNull();
        }
        else
        {
            var expected = remoteTree.ScopeTo(new ClusterConfigPath(path).Segments);
            settingsNode.Should().Be(expected);
        }
    }

    private ISettingsNode Extract(ClusterConfigPath path)
    {
        if (state == null)
        {
            RemoteSubtrees remoteSubtrees;

            if (remoteTree == null)
            {
                remoteSubtrees = null;
            }
            else
            {
                var writer = new BinaryBufferWriter(64);
                var cache = new RecyclingBoundedCache<string, string>(4);
                var treeSerializer = Protocol.GetSerializer(cache);
                
                treeSerializer.Serialize(remoteTree, writer);
                
                var nodeReader = new SubtreesMapBuilder(new ArraySegmentReader(new ArraySegment<byte>(writer.Buffer)), Encoding.UTF8, null);
                var map = nodeReader.BuildMap();

                var dictionary = new Dictionary<ClusterConfigPath, RemoteTree>();
                foreach (var pair in map.Where(p => observingSubtrees.Contains(p.Key)))
                {
                    dictionary[new ClusterConfigPath(pair.Key)] = new RemoteTree(pair.Value, treeSerializer);
                }

                remoteSubtrees = new RemoteSubtrees(dictionary);
            }

            state = new ClusterConfigClientState(localTree, remoteSubtrees, new RecyclingBoundedCache<ClusterConfigPath, ISettingsNode>(10), Int64.MaxValue);
        }

        return TreeExtractor.Extract(state, path, null);
    }
}