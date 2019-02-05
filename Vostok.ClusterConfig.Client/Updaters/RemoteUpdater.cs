using System;
using System.Threading;
using System.Threading.Tasks;
using Vostok.Clusterclient.Core;
using Vostok.Clusterclient.Core.Misc;
using Vostok.Clusterclient.Core.Model;
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
        private readonly string zone;

        public RemoteUpdater(IClusterProvider clusterProvider, ILog log, string zone)
        {
            this.log = log;
            this.zone = zone;

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

                    config.MaxReplicasUsedPerRequest = int.MaxValue;

                    config.TargetServiceName = TargetService;

                    config.SetupWeighedReplicaOrdering(
                        builder => builder.AddAdaptiveHealthModifierWithLinearDecay(TimeSpan.FromMinutes(2)));

                    // TODO(iloktionov): auxiliary headers (client identity, request timeout)

                    // TODO(iloktionov): custom response criteria (reject 404s?)
                });
        }

        // TODO(iloktionov): handle disabled case

        // TODO(iloktionov): handle the case when there are no replicas and there was no previous state (assume there's no CC deployed)

        // TODO(iloktionov): do not trust 404 response from a single replica

        // TODO(iloktionov): do not trust 304 response if a version has not been sent

        // TODO(iloktionov): protect against flapping between two versions when communicating with out-of-sync replicas

        public async Task<RemoteUpdateResult> UpdateAsync(RemoteUpdateResult lastResult, CancellationToken cancellationToken)
        {
            var request = Request.Get(zone);

            var result = await client.SendAsync(request, cancellationToken: cancellationToken).ConfigureAwait(false);

            // TODO(iloktionov): handle errors in result

            var response = result.Response;

            // TODO(iloktionov): handle different response codes

            throw new NotImplementedException();
        }

        private Request CreateRequest(RemoteUpdateResult lastResult)
        {
            var request = Request.Get(zone)
                .WithAdditionalQueryParameter("binaryProtocol", "v1")
                .WithAcceptHeader("application/octet-stream")
                .WithAcceptEncodingHeader("gzip");

            if (lastResult != null)
                request = request.WithIfModifiedSinceHeader(lastResult.Version);

            return request;
        }
    }
}
