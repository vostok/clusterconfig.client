using System;
using FluentAssertions.Extensions;
using NUnit.Framework;
using Vostok.Clusterclient.Core.Topology;
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
        }

        [TearDown]
        public void TearDown()
        {
            folder.Dispose();
            server.Dispose();

            ConsoleLog.Flush();
        }
    }
}
