using System;
using FluentAssertions;
using NUnit.Framework;
using Vostok.ClusterConfig.Client.Helpers;

namespace Vostok.ClusterConfig.Client.Tests.Helpers
{
    [TestFixture]
    internal class DnsClusterProvider_Tests
    {
        [Test, Explicit("Returns 'Socket error code = 11 (TryAgain)' on appveyor.")]
        public void Should_return_an_empty_cluster_when_given_dns_record_does_not_exist()
        {
            var provider = new DnsClusterProvider(Guid.NewGuid().ToString(), ClusterConfigClientDefaults.Port);

            var cluster = provider.GetCluster();

            cluster.Should().BeEmpty();
        }
    }
}