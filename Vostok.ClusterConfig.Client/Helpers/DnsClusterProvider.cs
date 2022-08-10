using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using JetBrains.Annotations;
using Vostok.Clusterclient.Core.Topology;
using Vostok.Logging.Abstractions;

namespace Vostok.ClusterConfig.Client.Helpers
{
    /// <summary>
    /// Resolves given DNS name to (potentially) multiple IP addresses and returns a replica for each of them.
    /// </summary>
    [PublicAPI]
    public class DnsClusterProvider : IClusterProvider
    {
        private static readonly Uri[] EmptyCluster = {};

        private volatile Uri[] cache;

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
                return cache = System.Net.Dns
                    .GetHostAddresses(Dns)
                    .Select(ip => new Uri($"http://{ip}:{Port}/", UriKind.Absolute))
                    .ToArray();
            }
            catch (SocketException error)
            {
                var socketError = error.SocketErrorCode;
                var errorMessage = $"Failed to resolve DNS name '{Dns}'. Socket error code = {error.ErrorCode} ({socketError}).";

                var currentCache = cache;
                if (currentCache != null)
                {
                    LogProvider.Get().Warn(errorMessage + " Will use cached IP addresses.");
                    return currentCache;
                }

                if (error.SocketErrorCode is SocketError.HostNotFound or SocketError.NoData or SocketError.TryAgain)
                {
                    return EmptyCluster;
                }

                throw new Exception(errorMessage, error);
            }
        }
    }
}
