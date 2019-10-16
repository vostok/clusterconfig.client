using System;
using JetBrains.Annotations;

namespace Vostok.ClusterConfig.Client.Helpers
{
    internal static class DefaultClusterConfigClientProvider
    {
        private static readonly object Sync = new object();

        private static volatile ClusterConfigClient client;

        public static ClusterConfigClient Get()
        {
            if (client != null)
                // ReSharper disable once InconsistentlySynchronizedField
                return client;

            lock (Sync)
            {
                client = client ?? Create();
                return client;
            }
        }

        public static bool TryConfigure([NotNull] ClusterConfigClient newClient)
        {
            if (newClient == null)
                throw new ArgumentNullException(nameof(newClient));

            if (client != null)
                return false;

            lock (Sync)
            {
                if (client != null)
                    return false;

                client = newClient;
                return true;
            }
        }

        private static ClusterConfigClient Create() => new ClusterConfigClient();
    }
}