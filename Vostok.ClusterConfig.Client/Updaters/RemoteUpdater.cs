using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
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
using Vostok.ClusterConfig.Core.Http;
using Vostok.ClusterConfig.Core.Serialization;
using Vostok.ClusterConfig.Core.Serialization.SubtreesProtocol;
using Vostok.ClusterConfig.Core.Utils;
using Vostok.Commons.Binary;
using Vostok.Commons.Collections;
using Vostok.Logging.Abstractions;

namespace Vostok.ClusterConfig.Client.Updaters
{
    internal class RemoteUpdater
    {
        private static readonly DateTime EmptyResultVersion = DateTime.MinValue;

        private readonly bool enabled;
        private readonly IClusterClient client;
        private readonly RecyclingBoundedCache<string, string> interningCache;
        private readonly ILog log;
        private readonly string zone;
        private readonly bool assumeClusterConfigDeployed;

        public RemoteUpdater(
            bool enabled,
            IClusterClient client,
            RecyclingBoundedCache<string, string> interningCache,
            ILog log,
            string zone,
            bool assumeClusterConfigDeployed = false)
        {
            this.enabled = enabled;
            this.client = client;
            this.interningCache = interningCache;
            this.log = log;
            this.zone = zone;
            this.assumeClusterConfigDeployed = assumeClusterConfigDeployed;
        }

        public RemoteUpdater(
            bool enabled,
            IClusterProvider cluster,
            ClusterClientSetup setup,
            RecyclingBoundedCache<string, string> interningCache,
            ILog log,
            string zone,
            TimeSpan timeout,
            bool assumeClusterConfigDeployed = false)
            : this(enabled, enabled ? CreateClient(cluster, setup, log, timeout) : null, interningCache, log, zone, assumeClusterConfigDeployed)
        {
        }

        public async Task<RemoteUpdateResult> UpdateAsync(List<ObservingSubtree> observingSubtrees, ClusterConfigProtocolVersion protocol, [CanBeNull] RemoteUpdateResult lastResult, CancellationToken cancellationToken)
        {
            if (!enabled)
                return CreateEmptyResult(lastResult);

            var protocolChanged = lastResult?.UsedProtocol is {} lastProtocol && lastProtocol != protocol;
            var request = CreateRequest(observingSubtrees, protocol, lastResult?.Version, protocolChanged, lastResult?.PatchingFailedReason);
            var requestPriority = lastResult == null ? RequestPriority.Critical : RequestPriority.Ordinary;
            var requestResult = await client.SendAsync(request, priority: requestPriority, cancellationToken: cancellationToken).ConfigureAwait(false);

            if (cancellationToken.IsCancellationRequested)
                throw new OperationCanceledException();

            var updateResult = TryHandleFailure(requestResult, lastResult);
            if (updateResult != null)
                return updateResult;

            return requestResult.Response.Code switch
            {
                ResponseCode.NotModified => HandleNotModifiedResponse(protocol, lastResult, requestResult.Response),
                ResponseCode.Ok or ResponseCode.PartialContent => HandleSuccessResponse(protocol, lastResult, requestResult.Response, requestResult.Replica, protocolChanged),
                _ => throw NoAcceptableResponseException(requestResult)
            };
        }

        internal static ClusterClient CreateClient(IClusterProvider cluster, ClusterClientSetup setup, ILog log, TimeSpan timeout)
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
            => new(
                lastResult?.Version != EmptyResultVersion, 
                null, 
                lastResult?.Version != EmptyResultVersion, 
                null, 
                lastResult?.UsedProtocol, 
                lastResult?.Version ?? EmptyResultVersion, 
                lastResult?.RecommendedProtocol, 
                lastResult?.PatchingFailedReason);

        private static RemoteUpdateResult CreatePatchingFailedResult(
            [CanBeNull] RemoteUpdateResult lastResult, 
            ClusterConfigProtocolVersion? recommendedProtocol, 
            PatchingFailedReason reason)
            => new(
                false, 
                lastResult?.Tree, 
                false, 
                lastResult?.Subtrees, 
                lastResult?.UsedProtocol, 
                lastResult?.Version ?? EmptyResultVersion, 
                recommendedProtocol, 
                reason);

        private Request CreateRequest(List<ObservingSubtree> observingSubtrees, ClusterConfigProtocolVersion protocol, DateTime? lastVersion, bool protocolChanged, PatchingFailedReason? patchingFailedReason)
        {
            var request = Request.Get(protocol.GetUrlPath())
                .WithAdditionalQueryParameter("zoneName", zone)
                .WithAcceptHeader("application/octet-stream")
                .WithAcceptEncodingHeader("gzip");

            if (protocol == ClusterConfigProtocolVersion.V3 && observingSubtrees != null)
                request = request.WithContent(SerializeRequest(observingSubtrees, patchingFailedReason != null || protocolChanged));

            if (protocol < ClusterConfigProtocolVersion.V3 && lastVersion.HasValue)
                request = request.WithIfModifiedSinceHeader(lastVersion.Value);

            if (patchingFailedReason != null)
                request = request.WithAdditionalQueryParameter(ClusterConfigQueryParameters.ForceFull, patchingFailedReason.ToString());
            else if (protocolChanged)
                request = request.WithAdditionalQueryParameter(ClusterConfigQueryParameters.ForceFull, "ProtocolChanged");

            return request;
        }

        private static Content SerializeRequest(List<ObservingSubtree> observingSubtrees, bool forceFullUpdate)
        {
            //TODO что-то сделать с размером и\или пулингом.
            var writer = new BinaryBufferWriter(1024);

            var request = new List<(string prefix, DateTime? version, bool forceFullUpdate)>(observingSubtrees.Count);
            foreach (var subtree in observingSubtrees)
                request.Add((subtree.Path.ToString(), subtree.LastVersion, forceFullUpdate));

            SubtreesRequestSerializer.Serialize(writer, request);

            return new Content(writer.Buffer, 0, (int)writer.Position);
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
                    if (lastUpdateResult?.Tree == null && lastUpdateResult?.Subtrees == null && !assumeClusterConfigDeployed)
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
        private RemoteUpdateResult HandleNotModifiedResponse(ClusterConfigProtocolVersion protocol, RemoteUpdateResult lastUpdateResult, Response response)
        {
            if (lastUpdateResult == null)
                throw UnexpectedNotModifiedResponseException();

            return new RemoteUpdateResult(false, lastUpdateResult.Tree, false, lastUpdateResult.Subtrees, protocol, lastUpdateResult.Version, GetRecommendedProtocolVersion(response), lastUpdateResult.PatchingFailedReason);
        }

        [NotNull]
        private RemoteUpdateResult HandleSuccessResponse(ClusterConfigProtocolVersion protocol, RemoteUpdateResult lastUpdateResult, Response response, Uri replica, bool protocolChanged)
        {
            if (response.Headers.LastModified == null)
                throw new RemoteUpdateException($"Received a {response.Code} response without 'LastModified' header.");

            if (protocolChanged && response.Code == ResponseCode.PartialContent)
                throw new RemoteUpdateException("Expected full zone (because protocol changed), but received patch.");

            var recommendedProtocol = GetRecommendedProtocolVersion(response);
            var responsesDescriptions = $"Responses descriptions: {{previous:{lastUpdateResult?.Tree?.Description}, current:{GetDescription(response)}}}";
            
            var version = DateTime.Parse(response.Headers.LastModified, null, DateTimeStyles.AssumeUniversal).ToUniversalTime();

            if (lastUpdateResult != null && version <= lastUpdateResult.Version && protocol < ClusterConfigProtocolVersion.V3)
            {
                if (version < lastUpdateResult.Version)
                    LogStaleVersion(version, lastUpdateResult.Version, protocol, responsesDescriptions);

                return new RemoteUpdateResult(false, lastUpdateResult.Tree, false, lastUpdateResult.Subtrees, protocol, lastUpdateResult.Version, recommendedProtocol, lastUpdateResult.PatchingFailedReason);
            }

            return response.Code switch
            {
                ResponseCode.Ok => CreateResultViaFullZoneOrV3(protocol, response, version, replica, lastUpdateResult, recommendedProtocol, responsesDescriptions),
                ResponseCode.PartialContent => CreateResultViaPatch(protocol, response, version, replica, lastUpdateResult, recommendedProtocol, responsesDescriptions),
                var code => throw new RemoteUpdateException($"Failed to update settings from server: unknown successful response code {code}")
            };
        }

        private RemoteUpdateResult CreateResultViaFullZoneOrV3(
            ClusterConfigProtocolVersion protocol, 
            Response response, 
            DateTime version,
            Uri replica, 
            RemoteUpdateResult lastResult, 
            ClusterConfigProtocolVersion? recommendedProtocol,
            string responsesDescriptions)
        {
            if (!response.HasContent)
                throw new RemoteUpdateException($"Received an empty {response.Code} response from server. Nothing to deserialize. {responsesDescriptions}.");

            var serializer = protocol.GetSerializer(interningCache);
            var description = GetDescription(response);

            if (protocol == ClusterConfigProtocolVersion.V3)
            {
                //(deniaa): Until we override BufferFactory for transport and create a buffer for gzip decompression, each Content is a new byte array. So we can refer to subsequences of it.
                var responseContent = response.Content.ToArraySegment();
                var reader = new BinaryBufferReader(responseContent.Array!, responseContent.Offset);

                var deserializedSubtrees = SubtreesResponseSerializer.Deserialize(reader, Encoding.UTF8, true);
                
                //(deniaa): Only patch applying can fail deserialization
                if (!TryDeserializeRemoteSubtrees(protocol, deserializedSubtrees, version, lastResult, serializer, description, out var remoteSubtrees))
                {
                    return CreatePatchingFailedResult(lastResult, recommendedProtocol, PatchingFailedReason.ApplyPatchFailed);
                }

                var haveAnyModifiedSubtree = deserializedSubtrees.Any(s => s.Value.WasModified);
                var treesSize = remoteSubtrees.Subtrees.Sum(x => x.Value?.Size ?? 0);

                LogReceivedNewSubtrees(treesSize, version, replica, protocol, responsesDescriptions);
                return new RemoteUpdateResult(false, null, haveAnyModifiedSubtree, haveAnyModifiedSubtree ? remoteSubtrees : null, protocol, version, recommendedProtocol, null);
            }
            else
            {
                var tree = new RemoteTree(new ArraySegment<byte>(response.Content.ToArray()), serializer, description);
                LogReceivedNewZone(tree.Size, version, replica, false, protocol, responsesDescriptions);
                return new RemoteUpdateResult(true, tree, false, null, protocol, version, recommendedProtocol, null);
            }
        }
        
        private bool TryDeserializeRemoteSubtrees(
            ClusterConfigProtocolVersion protocol,
            Dictionary<string, Subtree> deserializedSubtrees,
            DateTime version,
            RemoteUpdateResult lastResult,
            ITreeSerializer serializer,
            string description,
            out RemoteSubtrees remoteSubtrees)
        {
            var result = new Dictionary<ClusterConfigPath, RemoteTree>();
            remoteSubtrees = null;
            
            foreach (var pair in deserializedSubtrees)
            {
                var prefix = pair.Key;
                var subtree = pair.Value;
                
                if (subtree.WasModified)
                {
                    if (subtree.HasSubtree)
                    {
                        if (subtree.IsPatch)
                        {
                            if (!TryApplyPatch(protocol, subtree.Content, lastResult, version, out var patchedTree, description))
                            {
                                return false;
                            }

                            subtree.Content = patchedTree;
                        }

                        result.Add(new ClusterConfigPath(prefix), new RemoteTree(subtree.Content, serializer, description));
                    }
                    else
                    {
                        result.Add(new ClusterConfigPath(prefix), null);
                    }
                }
                else
                {
                    var path = new ClusterConfigPath(prefix);
                    if (lastResult?.Subtrees != null && lastResult.Subtrees.Subtrees.TryGetValue(path, out var previousTree))
                    {
                        result.Add(path, previousTree);
                    }
                }
            }

            remoteSubtrees = new RemoteSubtrees(result);
            return true;
        }

        private RemoteUpdateResult CreateResultViaPatch(
            ClusterConfigProtocolVersion protocol, 
            Response response, 
            DateTime version, 
            Uri replica, 
            RemoteUpdateResult lastResult, 
            ClusterConfigProtocolVersion? recommendedProtocol, 
            string responsesDescriptions)
        {
            if (protocol != ClusterConfigProtocolVersion.V2)
                throw new RemoteUpdateException($"Received 206 response from server, but it's not supported for current protocol. {responsesDescriptions}.");
            
            if (lastResult?.Tree?.Serialized == null)
                throw new RemoteUpdateException($"Received 206 response from server, but nothing to patch. {responsesDescriptions}.");

            if (protocol != lastResult.UsedProtocol)
                throw new RemoteUpdateException($"Received 206 response from server, but can't apply {protocol} patch to {lastResult.UsedProtocol} tree. {responsesDescriptions}.");
            
            if (!TryApplyPatch(protocol, response.Content.ToArraySegment(), lastResult, version, out var newZone, responsesDescriptions))
                return CreatePatchingFailedResult(lastResult, recommendedProtocol, PatchingFailedReason.ApplyPatchFailed);

            if (!EnsureHashValid(newZone, response, version, true, responsesDescriptions))
                return CreatePatchingFailedResult(lastResult, recommendedProtocol, PatchingFailedReason.HashMismatch);

            var tree = new RemoteTree(newZone, protocol.GetSerializer(interningCache), GetDescription(response));

            LogReceivedNewZone(tree.Size, version, replica, true, protocol, responsesDescriptions);

            return new RemoteUpdateResult(true, tree, false, null, protocol, version, recommendedProtocol, null);
        }
        
        private bool TryApplyPatch(ClusterConfigProtocolVersion protocol, ArraySegment<byte> patch, RemoteUpdateResult old, DateTime version, out ArraySegment<byte> newZone, string responsesDescriptions)
        {
            try
            {
                newZone = protocol.GetPatcher(interningCache).ApplyPatch(old.Tree!.Serialized!.Value, patch);
                return true;
            }
            catch (Exception e)
            {
                log.Error(e, "Can't apply patch {PatchVersion} to {OldVersion} (protocol {Protocol}). {ResponsesDescriptions}.", version, old.Version, protocol, responsesDescriptions);

                newZone = default;
                return false;
            }
        }
        
        private ClusterConfigProtocolVersion? GetRecommendedProtocolVersion(Response response) =>
            response.Headers[ClusterConfigHeaderNames.RecommendedProtocol] is {} header
                ? Enum.TryParse<ClusterConfigProtocolVersion>(header, out var value) ? value : null
                : null;

        private string GetDescription(Response response) => response.Headers[ClusterConfigHeaderNames.TreeDescription];

        private bool EnsureHashValid(ArraySegment<byte> serialized, Response response, DateTime newVersion, bool isPatch, string responsesDescriptions)
        {
            var expectedHash = response.Headers[ClusterConfigHeaderNames.ZoneHash];
            if (expectedHash == null)
                return true;

            var hash = serialized.GetSha256Str();
            if (hash == expectedHash)
                return true;
            
            log.Warn(
                "Detected hash mismatch: {ActualHash} != {ExpectedHash}. New version is {NewVersion}, is patch: {IsPatch}. {ResponsesDescriptions}.",
                hash,
                expectedHash,
                newVersion,
                isPatch,
                responsesDescriptions);
            
            return false;
        }
        
        #region Logging

        private void LogAssumingNoServer()
            => log.Info("Resolved no replicas on initial settings update. Assumming that CC is not deployed in current environment.");

        private void LogZoneDoesNotExist()
            => log.Warn("Zone '{Zone}' not found. Returning empty remote settings.", zone);

        private void LogStaleVersion(DateTime staleVersion, DateTime currentVersion, ClusterConfigProtocolVersion protocol, string responsesDescriptions) =>
            log.Warn(
                "Received response for zone '{Zone}' with stale version '{StaleVersion}'. Current version = '{CurrentVersion}'. Protocol = {Protocol}. {ResponsesDescriptions}. Will not update.",
                zone, 
                staleVersion,
                currentVersion, 
                protocol.ToString(),
                responsesDescriptions);

        private void LogReceivedNewZone(int treeSize, DateTime version, Uri replica, bool patch, ClusterConfigProtocolVersion protocol, string responsesDescriptions)
            => log.Info("Received new version of zone '{Zone}' from {Replica}. Size = {Size}. Version = {Version}. Protocol = {Protocol}. Patch = {IsPatch}. {ResponsesDescriptions}.", 
                zone, replica?.Authority, treeSize, version.ToString("R"), protocol.ToString(), patch, responsesDescriptions);
        
        private void LogReceivedNewSubtrees(int treesSize, DateTime version, Uri replica, ClusterConfigProtocolVersion protocol, string responsesDescriptions)
            => log.Info("Received new subtrees from '{Zone}' from {Replica}. Size = {Size}. Version = {Version}. Protocol = {Protocol}. {ResponsesDescriptions}.", 
                zone, replica?.Authority, treesSize, version.ToString("R"), protocol.ToString(), responsesDescriptions);

        #endregion

        #region Exceptions

        private static Exception UnexpectedNotModifiedResponseException()
            => new RemoteUpdateException("Received unexpected 'NotModified' response from server although no current version was sent in request.");

        private static Exception NoAcceptableResponseException(ClusterResult result)
            => new RemoteUpdateException($"Failed to update settings from server. Request status = '{result.Status}'. Replica responses = '{string.Join(", ", result.ReplicaResults.Select(r => r.Response.Code))}'.");

        #endregion
    }
}
