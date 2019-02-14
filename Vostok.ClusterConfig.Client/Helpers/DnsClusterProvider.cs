using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using JetBrains.Annotations;
using Vostok.Clusterclient.Core.Topology;

namespace Vostok.ClusterConfig.Client.Helpers
{
    /// <summary>
    /// Resolves given DNS name to (potentially) multiple IP addresses and returns a replica for each of them.
    /// </summary>
    [PublicAPI]
    public class DnsClusterProvider : IClusterProvider
    {
        private static readonly Uri[] EmptyCluster = {};

        public DnsClusterProvider(string dns, int port)
        {
            Dns = dns;
            Port = port;
        }

        public string Dns { get; }

        public int Port { get; }

        public IList<Uri> GetCluster()
        {
            try
            {
                return System.Net.Dns
                       .GetHostAddresses(Dns)
                       .Select(ip => new Uri($"http://{ip}:{Port}/", UriKind.Absolute))
                       .ToArray();
            }
            catch (SocketException error)
            {
                if (error.SocketErrorCode == SocketError.HostNotFound ||
                    error.SocketErrorCode == SocketError.NoData)
                {
                    return EmptyCluster;
                }

                throw new Exception($"Failed to resolve DNS name '{Dns}'. Socket error code = {error.ErrorCode} ('{error.SocketErrorCode}').", error);
            }
        }
    }
}
