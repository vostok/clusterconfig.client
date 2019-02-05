using System;
using System.Threading;
using System.Threading.Tasks;
using Vostok.Clusterclient.Core;
using Vostok.Clusterclient.Core.Misc;
using Vostok.Clusterclient.Core.Ordering.Weighed;
using Vostok.Clusterclient.Core.Strategies;
using Vostok.Clusterclient.Core.Topology;
using Vostok.Clusterclient.Transport;
using Vostok.Logging.Abstractions;

namespace Vostok.ClusterConfig.Client.Updaters
{
    internal class RemoteUpdater
    {
        private const string TargetService = "ClusterConfig";

        private readonly IClusterClient client;
        private readonly ILog log;

        public RemoteUpdater(IClusterProvider clusterProvider, ILog log)
        {
            this.log = log;

            client = new ClusterClient(
                log.WithMinimumLevel(LogLevel.Warn),
                config =>
                {
                    config.ClusterProvider = clusterProvider;
                    config.SetupUniversalTransport();

                    config.DefaultTimeout = TimeSpan.FromSeconds(30);
                    config.DefaultRequestStrategy = Strategy.Forking3;

                    config.Logging = new LoggingOptions
                    {
                        LogRequestDetails = false,
                        LogResultDetails = false
                    };

                    config.MaxReplicasUsedPerRequest = 3;

                    config.TargetServiceName = TargetService;

                    config.SetupWeighedReplicaOrdering(
                        builder => builder.AddAdaptiveHealthModifierWithLinearDecay(TimeSpan.FromMinutes(2)));

                    // TODO(iloktionov): auxiliary headers (client identity, request timeout)

                    // TODO(iloktionov): custom response criteria (reject 400s?)
                });
        }

        public async Task<RemoteUpdateResult> UpdateAsync(RemoteUpdateResult lastResult, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }
    }
}
