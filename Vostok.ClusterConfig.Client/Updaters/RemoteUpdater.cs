﻿using System;
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
using Vostok.Clusterclient.Core.Retry;
using Vostok.Clusterclient.Core.Strategies;
using Vostok.Clusterclient.Core.Topology;
using Vostok.Clusterclient.Transport;
using Vostok.ClusterConfig.Client.Abstractions;
using Vostok.ClusterConfig.Client.Exceptions;
using Vostok.ClusterConfig.Client.Helpers;
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
        private readonly bool assumeClusterConfigDeployed;

        public RemoteUpdater(bool enabled, IClusterClient client, ILog log, string zone, bool assumeClusterConfigDeployed = false)
        {
            this.enabled = enabled;
            this.client = client;
            this.log = log;
            this.zone = zone;
            this.assumeClusterConfigDeployed = assumeClusterConfigDeployed;
        }

        public RemoteUpdater(bool enabled, IClusterProvider cluster, ClusterClientSetup setup, ILog log, string zone, TimeSpan timeout, bool assumeClusterConfigDeployed = false)
            : this(enabled, enabled ? CreateClient(cluster, setup, log, timeout) : null, log, zone, assumeClusterConfigDeployed)
        {
        }

        public async Task<RemoteUpdateResult> UpdateAsync(ProtocolVersion protocol, RemoteUpdateResult lastResult, CancellationToken cancellationToken)
        {
            if (!enabled)
                return CreateEmptyResult(lastResult);

            var request = CreateRequest(protocol, lastResult?.Version, lastResult?.Tree?.Protocol);
            var requestPriority = lastResult == null ? RequestPriority.Critical : RequestPriority.Ordinary;
            var requestResult = await client.SendAsync(request, priority: requestPriority, cancellationToken: cancellationToken).ConfigureAwait(false);

            if (cancellationToken.IsCancellationRequested)
                throw new OperationCanceledException();

            if (TryHandleFailure(requestResult, lastResult) is {} updateResult)
                return updateResult;

            return requestResult.Response.Code switch
            {
                ResponseCode.NotModified => HandleNotModifiedResponse(lastResult, requestResult.Response),
                ResponseCode.Ok or ResponseCode.PartialContent => HandleSuccessResponse(protocol, lastResult, requestResult.Response, requestResult.Replica),
                _ => throw NoAcceptableResponseException(requestResult)
            };
        }

        private static ClusterClient CreateClient(IClusterProvider cluster, ClusterClientSetup setup, ILog log, TimeSpan timeout)
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

                    config.RetryPolicy = new RemoteRetryPolicy();
                    config.RetryStrategy = new LinearBackoffRetryStrategy(5, 
                        TimeSpan.FromSeconds(5),
                        TimeSpan.FromSeconds(15),
                        TimeSpan.FromSeconds(3));
                    
                    setup?.Invoke(config);
                });
        }

        private static RemoteUpdateResult CreateEmptyResult([CanBeNull] RemoteUpdateResult lastResult)
            => new(lastResult?.Version != EmptyResultVersion, null, EmptyResultVersion, lastResult?.RecommendedProtocol);

        private Request CreateRequest(ProtocolVersion protocol, DateTime? lastVersion, ProtocolVersion? lastProtocol)
        {
            var request = Request.Get(protocol.GetUrlPath())
                .WithAdditionalQueryParameter("zoneName", zone)
                .WithAcceptHeader("application/octet-stream")
                .WithAcceptEncodingHeader("gzip");

            if (lastVersion.HasValue)
                request = request.WithIfModifiedSinceHeader(lastVersion.Value);

            if (lastProtocol == null)
                request = request.WithAdditionalQueryParameter(ClusterConfigQueryParameters.ForceFullQueryKey, ClusterConfigQueryParameters.ForceFullReasonNoPrevious);
            else if (protocol != lastProtocol)
                request = request.WithAdditionalQueryParameter(ClusterConfigQueryParameters.ForceFullQueryKey, ClusterConfigQueryParameters.ForceFullReasonProtocolChanged);

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

                    // (tsup): We make assumptions above in case we are not forced to assume that cluster config is deployed.
                    if (lastUpdateResult?.Tree == null && !assumeClusterConfigDeployed)
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
        private RemoteUpdateResult HandleNotModifiedResponse(RemoteUpdateResult lastUpdateResult, Response response)
        {
            if (lastUpdateResult == null)
                throw UnexpectedNotModifiedResponseException();

            return new RemoteUpdateResult(false, lastUpdateResult.Tree, lastUpdateResult.Version, GetRecommendedProtocolVersion(response));
        }

        [NotNull]
        private RemoteUpdateResult HandleSuccessResponse(ProtocolVersion protocol, RemoteUpdateResult lastUpdateResult, Response response, Uri replica)
        {
            if (!response.HasContent)
                throw new RemoteUpdateException($"Received an empty {response.Code} response from server. Nothing to deserialize.");

            if (response.Headers.LastModified == null)
                throw new RemoteUpdateException($"Received a {response.Code} response without 'LastModified' header.");

            var recommendedProtocol = GetRecommendedProtocolVersion(response);
            
            var version = DateTime.Parse(response.Headers.LastModified, null, DateTimeStyles.AssumeUniversal).ToUniversalTime();

            if (lastUpdateResult != null && version <= lastUpdateResult.Version)
            {
                if (version < lastUpdateResult.Version)
                    LogStaleVersion(version, lastUpdateResult.Version, protocol);

                return new RemoteUpdateResult(false, lastUpdateResult.Tree, lastUpdateResult.Version, recommendedProtocol);
            }

            return response.Code switch
            {
                ResponseCode.Ok => CreateResultViaFullZone(protocol, response, version, replica, recommendedProtocol),
                ResponseCode.PartialContent => CreateResultViaPatch(protocol, response, version, replica, lastUpdateResult, recommendedProtocol),
                var code => throw new RemoteUpdateException($"Failed to update settings from server: unknown successful response code {code}")
            };
        }

        private RemoteUpdateResult CreateResultViaFullZone(ProtocolVersion protocol, Response response, DateTime version, Uri replica, ProtocolVersion? recommendedProtocol)
        {
            var tree = new RemoteTree(protocol, response.Content.ToArray(), protocol.GetSerializer());

            LogReceivedNewZone(tree, version, replica, false, protocol);

            return new RemoteUpdateResult(true, tree, version, recommendedProtocol);
        }

        private RemoteUpdateResult CreateResultViaPatch(ProtocolVersion protocol, Response response, DateTime version, Uri replica, RemoteUpdateResult lastResult, ProtocolVersion? recommendedProtocol)
        {
            if (protocol != ProtocolVersion.V2)
                throw new RemoteUpdateException("Received 206 response from server, but it's not supported for current protocol.");
            
            if (lastResult?.Tree?.Serialized == null)
                throw new RemoteUpdateException("Received 206 response from server, but nothing to patch.");

            if (protocol != lastResult.Tree.Protocol)
                throw new RemoteUpdateException($"Received 206 response from server, but can't apply {protocol} patch to {lastResult.Tree.Protocol} tree.");
            
            var patch = response.Content.ToArray();
            var tree = new RemoteTree(protocol, lastResult.Tree.Serialized.ApplyV2Patch(patch), protocol.GetSerializer());

            LogReceivedNewZone(tree, version, replica, true, protocol);

            return new RemoteUpdateResult(true, tree, version, recommendedProtocol);
        }

        private ProtocolVersion? GetRecommendedProtocolVersion(Response response) =>
            response.Headers[ClusterConfigHeaderNames.RecommendedProtocol] is {} header
                ? Enum.TryParse<ProtocolVersion>(header, out var value) ? value : null
                : null;

        #region Logging

        private void LogAssumingNoServer()
            => log.Info("Resolved no replicas on initial settings update. Assumming that CC is not deployed in current environment.");

        private void LogZoneDoesNotExist()
            => log.Warn("Zone '{Zone}' not found. Returning empty remote settings.", zone);

        private void LogStaleVersion(DateTime staleVersion, DateTime currentVersion, ProtocolVersion protocol)
            => log.Warn("Received response for zone '{Zone}' with stale version '{StaleVersion}'. Current version = '{CurrentVersion}'. Protocol = {Protocol}. Will not update.",
                zone, staleVersion, currentVersion, protocol.ToString());

        private void LogReceivedNewZone(RemoteTree tree, DateTime version, Uri replica, bool patch, ProtocolVersion protocol)
            => log.Info("Received new version of zone '{Zone}' from {Replica}. Size = {Size}. Version = {Version}. Protocol = {Protocol}. Patch = {IsPatch}.", 
                zone, replica?.Authority, tree.Size, version.ToString("R"), protocol.ToString(), patch);

        #endregion

        #region Exceptions

        private static Exception UnexpectedNotModifiedResponseException()
            => new RemoteUpdateException("Received unexpected 'NotModified' response from server although no current version was sent in request.");

        private static Exception NoAcceptableResponseException(ClusterResult result)
            => new RemoteUpdateException($"Failed to update settings from server. Request status = '{result.Status}'. Replica responses = '{string.Join(", ", result.ReplicaResults.Select(r => r.Response.Code))}'.");

        #endregion
    }
}
