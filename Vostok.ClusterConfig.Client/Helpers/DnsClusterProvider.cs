using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using Vostok.Clusterclient.Core.Topology;

namespace Vostok.ClusterConfig.Client.Helpers
{
    internal class DnsClusterProvider : IClusterProvider
    {
        private static readonly Uri[] EmptyCluster = {};

        private readonly string dns;
        private readonly int port;

        public DnsClusterProvider(string dns, int port)
        {
            this.dns = dns;
            this.port = port;
        }

        public IList<Uri> GetCluster()
        {
            try
            {
                return Dns
                       .GetHostAddresses(dns)
                       .Select(ip => new Uri($"http://{ip}:{port}/", UriKind.Absolute))
                       .ToArray();
            }
            catch (SocketException error)
            {
                if (error.SocketErrorCode == SocketError.HostNotFound ||
                    error.SocketErrorCode == SocketError.NoData)
                {
                    return EmptyCluster;
                }

                throw new Exception($"Failed to resolve DNS name '{dns}'. Socket error code = {error.ErrorCode} ('{error.SocketErrorCode}').", error);
            }
        }
    }
}
