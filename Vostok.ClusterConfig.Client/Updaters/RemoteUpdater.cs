using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Vostok.Clusterclient.Core;
using Vostok.Clusterclient.Core.Criteria;
using Vostok.Clusterclient.Core.Misc;
using Vostok.Clusterclient.Core.Model;
using Vostok.Clusterclient.Core.Ordering.Weighed;
using Vostok.Clusterclient.Core.Strategies;
using Vostok.Clusterclient.Core.Topology;
using Vostok.Clusterclient.Transport;
using Vostok.ClusterConfig.Client.Exceptions;
using Vostok.ClusterConfig.Client.Helpers;
using Vostok.ClusterConfig.Core.Serialization;
using Vostok.Logging.Abstractions;

namespace Vostok.ClusterConfig.Client.Updaters
{
    internal class RemoteUpdater
    {
        private static readonly DateTime EmptyResultVersion = DateTime.MinValue;

        private readonly bool enabled;
        private readonly IClusterClient client;
        private readonly ILog log;
        private readonly string zone;

        public RemoteUpdater(bool enabled, IClusterClient client, ILog log, string zone)
        {
            this.enabled = enabled;
            this.client = client;
            this.log = log;
            this.zone = zone;
        }

        public RemoteUpdater(bool enabled, IClusterProvider cluster, ILog log, string zone, TimeSpan timeout)
            : this(enabled, enabled ? CreateClient(cluster, log, timeout) : null, log, zone)
        {
        }

        public async Task<RemoteUpdateResult> UpdateAsync(RemoteUpdateResult lastResult, CancellationToken cancellationToken)
        {
            if (!enabled)
                return CreateEmptyResult(lastResult);

            var request = CreateRequest(lastResult?.Version);

            var requestResult = await client.SendAsync(request, cancellationToken: cancellationToken).ConfigureAwait(false);

            if (cancellationToken.IsCancellationRequested)
                throw new OperationCanceledException();

            var updateResult = TryHandleFailure(requestResult, lastResult);
            if (updateResult != null)
                return updateResult;

            switch (requestResult.Response.Code)
            {
                case ResponseCode.NotModified:
                    return HandleNotModifiedResponse(lastResult);

                case ResponseCode.Ok:
                    return HandleSuccessResponse(lastResult, requestResult.Response);
            }

            throw NoAcceptableResponseException(requestResult);
        }

        private static ClusterClient CreateClient(IClusterProvider cluster, ILog log, TimeSpan timeout)
        {
            return new ClusterClient(
                log.WithMinimumLevel(LogLevel.Warn),
                config =>
                {
                    config.ClusterProvider = cluster;
                    config.SetupUniversalTransport();

                    config.DefaultTimeout = timeout;
                    config.DefaultRequestStrategy = Strategy.Forking3;

                    config.Logging = new LoggingOptions
                    {
                        LogRequestDetails = false,
                        LogResultDetails = false
                    };

                    config.MaxReplicasUsedPerRequest = int.MaxValue;

                    config.TargetServiceName = "ClusterConfig";

                    config.SetupWeighedReplicaOrdering(
                        builder => builder.AddAdaptiveHealthModifierWithLinearDecay(TimeSpan.FromMinutes(2)));

                    config.SetupResponseCriteria(
                        new AcceptNonRetriableCriterion(),
                        new Reject404ErrorsCriterion(),
                        new RejectNetworkErrorsCriterion(),
                        new RejectServerErrorsCriterion(),
                        new RejectThrottlingErrorsCriterion(),
                        new RejectUnknownErrorsCriterion(),
                        new AlwaysAcceptCriterion());

                    config.AddResponseTransform(new GzipBodyTransform());
                });
        }

        private static RemoteUpdateResult CreateEmptyResult([CanBeNull] RemoteUpdateResult lastResult)
            => new RemoteUpdateResult(lastResult?.Version != EmptyResultVersion, null, EmptyResultVersion);

        private Request CreateRequest(DateTime? lastVersion)
        {
            var request = Request.Get("_v1/zone")
                .WithAdditionalQueryParameter("zoneName", zone)
                .WithAcceptHeader("application/octet-stream")
                .WithAcceptEncodingHeader("gzip");

            if (lastVersion.HasValue)
                request = request.WithIfModifiedSinceHeader(lastVersion.Value);

            return request;
        }

        [CanBeNull]
        private RemoteUpdateResult TryHandleFailure(ClusterResult requestResult, RemoteUpdateResult lastUpdateResult)
        {
            switch (requestResult.Status)
            {
                case ClusterResultStatus.ReplicasNotFound:
                    // (iloktionov): If no replicas were resolved during update and we haven't seen any non-trivial settings earlier, 
                    // (iloktionov): we just silently assume CC is not deployed in current environment and return empty settings.
                    if (lastUpdateResult?.Tree == null)
                    {
                        if (lastUpdateResult == null)
                            LogAssumingNoServer();

                        return CreateEmptyResult(lastUpdateResult);
                    }

                    break;

                case ClusterResultStatus.ReplicasExhausted:
                    // (iloktionov): Getting here means that no replica returned a 200 or 304 response.
                    // (iloktionov): If at least some of them responded with 404, it's reasonably safe to assume that zone does not exist.
                    if (requestResult.ReplicaResults.Any(r => r.Response.Code == ResponseCode.NotFound))
                    {
                        var updateResult = CreateEmptyResult(lastUpdateResult);
                        if (updateResult.Changed)
                            LogZoneDoesNotExist();

                        return updateResult;
                    }

                    break;
            }

            return null;
        }

        [NotNull]
        private RemoteUpdateResult HandleNotModifiedResponse(RemoteUpdateResult lastUpdateResult)
        {
            if (lastUpdateResult == null)
                throw UnexpectedNotModifiedResponseException();

            return new RemoteUpdateResult(false, lastUpdateResult.Tree, lastUpdateResult.Version);
        }

        [NotNull]
        private RemoteUpdateResult HandleSuccessResponse(RemoteUpdateResult lastUpdateResult, Response response)
        {
            if (!response.HasContent)
                throw Empty200ResponseException();

            if (response.Headers.LastModified == null)
                throw MissingLastModifiedHeaderException();

            var version = DateTime.Parse(response.Headers.LastModified, null, DateTimeStyles.AssumeUniversal).ToUniversalTime();

            if (lastUpdateResult != null && version <= lastUpdateResult.Version)
            {
                if (version < lastUpdateResult.Version)
                    LogStaleVersion(version, lastUpdateResult.Version);

                return new RemoteUpdateResult(false, lastUpdateResult.Tree, lastUpdateResult.Version);
            }

            var tree = new RemoteTree(response.Content.ToArray(), TreeSerializers.V1);

            LogReceivedNewZone(tree, version);

            return new RemoteUpdateResult(true, tree, version);
        }

        #region Logging

        private void LogAssumingNoServer()
            => log.Info("Resolved no replicas on initial settings update. Assumming that CC is not deployed in current environment.");

        private void LogZoneDoesNotExist()
            => log.Warn("Zone '{Zone}' not found. Returning empty remote settings.", zone);

        private void LogStaleVersion(DateTime staleVersion, DateTime currentVersion)
            => log.Warn("Received response for zone '{Zone}' with stale version '{StaleVersion}'. Current version = '{CurrentVersion}'. Will not update.",
                zone, staleVersion, currentVersion);

        private void LogReceivedNewZone(RemoteTree tree, DateTime version)
            => log.Info("Received new version of zone '{Zone}'. Size = {Size}. Version = {Version}.", zone, tree.Size, version.ToString("R"));

        #endregion

        #region Exceptions

        private static Exception UnexpectedNotModifiedResponseException()
            => new RemoteUpdateException("Received unexpected 'NotModified' response from server although no current version was sent in request.");

        private static Exception Empty200ResponseException()
            => new RemoteUpdateException("Received an empty 200 response from server. Nothing to deserialize.");

        private static Exception MissingLastModifiedHeaderException() 
            => new RemoteUpdateException("Received a 200 response without 'LastModified' header.");

        private static Exception NoAcceptableResponseException(ClusterResult result)
            => new RemoteUpdateException($"Failed to update settings from server. Request status = '{result.Status}'. Replica responses = '{string.Join(", ", result.ReplicaResults.Select(r => r.Response.Code))}'.");

        #endregion
    }
}
