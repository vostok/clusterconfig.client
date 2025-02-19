using System;
using FluentAssertions;
using FluentAssertions.Extensions;
using NSubstitute;
using NUnit.Framework;
using Vostok.Clusterclient.Core.Topology;
using Vostok.Commons.Testing;
using Vostok.Commons.Testing.Observable;
using Vostok.Configuration.Abstractions.SettingsTree;
using Vostok.Logging.Abstractions;
using Vostok.Logging.Console;

namespace Vostok.ClusterConfig.Client.Tests.Functional;

internal class FunctionalTests_V3_RecommendedProtocol
{
    private TestServer server;
    private ClusterConfigClientSettings settings;
    private ClusterConfigClient client;

    private ISettingsNode remoteTree1;
    private ISettingsNode remoteTree2;

    private DateTime version1;
    private DateTime version2;
    private ILog log;

    [SetUp]
    public void TestSetup()
    {
        var defaultServerProtocol = ClusterConfigProtocolVersion.V3_1;
        server = new TestServer(defaultServerProtocol);
        server.Start();
        server.SetRecommendedProtocol(defaultServerProtocol);

        log = Substitute.For<ILog>();
        log.IsEnabledFor(Arg.Any<LogLevel>()).ReturnsForAnyArgs(true);
        log.ForContext(Arg.Any<string>()).ReturnsForAnyArgs(log);
        log.IsEnabledFor(LogLevel.Info).Should().BeTrue();

        settings = new ClusterConfigClientSettings
        {
            AdditionalSetup = s =>
            {
                //To speed up tests which should be faced with Timeouts (default is 30 seconds)
                s.DefaultTimeout = 10.Seconds();
            },
            EnableLocalSettings = true,
            EnableClusterSettings = true,
            Cluster = new FixedClusterProvider(new Uri(server.Url)),
            UpdatePeriod = 250.Milliseconds(),
            Log = new CompositeLog(log, new SynchronousConsoleLog()),
        };

        client = new ClusterConfigClient(settings);

        remoteTree1 = new ObjectNode(
            null,
            new ISettingsNode[]
            {
                new ValueNode("foo", "123"),
                new ValueNode("bar", "456"),
                new ValueNode("baz", "1"),
                new ValueNode("bac", "abc")
                    
            });

        remoteTree2 = new ObjectNode(
            null,
            new ISettingsNode[]
            {
                new ValueNode("foo", "789"),
                new ValueNode("bar", "?!@"),
                new ValueNode("baz", "1"),
                new ValueNode("bac", "abc")
            });

        version1 = new DateTime(1990, 12, 1, 13, 5, 45);
        version2 = version1 + 2.Minutes();
    }

    [TestCase(ClusterConfigProtocolVersion.V2)]
    [TestCase(ClusterConfigProtocolVersion.V3)]
    [TestCase(ClusterConfigProtocolVersion.V3_1)]
    public void Client_should_be_able_to_downgrade_to_recommended_protocol(ClusterConfigProtocolVersion protocol)
    {
        server.SetResponse(remoteTree1, version1);

        client.Get("foo").Should().Be(remoteTree1["foo"]);

        server.SetRecommendedProtocol(protocol);
        //(deniaa) This request forces ClusterConfigClient.DefaultProtocol protocol to go to the server and received a new recommended protocol. 
        client.Get("bac").Should().Be(remoteTree1["bac"]);
            
        server.SetResponse(remoteTree2, version2);

        Action a = () =>
        {
            Action b = () =>
            {
                client.Get("foo").Should().Be(remoteTree2["foo"]);
                client.Get("bar").Should().Be(remoteTree2["bar"]);
                client.Get("baz").Should().Be(remoteTree2["baz"]);
            };
            b.Should().NotThrow();
        };
        a.ShouldPassIn(10.Seconds());
    }
}