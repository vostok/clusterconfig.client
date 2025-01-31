using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Vostok.Clusterclient.Core.Model;
using Vostok.ClusterConfig.Client.Helpers;
using Vostok.ClusterConfig.Core.Http;
using Vostok.ClusterConfig.Core.Patching;
using Vostok.ClusterConfig.Core.Serialization.SubtreesProtocol;
using Vostok.ClusterConfig.Core.Serialization.V2;
using Vostok.ClusterConfig.Core.Utils;
using Vostok.Commons.Binary;
using Vostok.Commons.Collections;
using Vostok.Commons.Helpers.Network;
using Vostok.Configuration.Abstractions.SettingsTree;

namespace Vostok.ClusterConfig.Client.Tests.Functional
{
    internal class TestServer : IDisposable
    {
        private readonly ClusterConfigProtocolVersion protocol;
        private readonly HttpListener listener;
        private volatile HttpListenerRequest request;
        private volatile Dictionary<string,ArraySegment<byte>> subtreesMap;
        private volatile byte[] serializedPatch;
        private volatile string hash;
        private DateTime version;
        private volatile byte[] serializedTree;
        private ClusterConfigProtocolVersion? recommendedProtocol;
        private volatile ResponseCode subtreesResponseCode;
        private volatile Response response;

        public TestServer(ClusterConfigProtocolVersion protocol)
        {
            this.protocol = protocol;
            Port = FreeTcpPortFinder.GetFreePort();

            listener = new HttpListener();
            listener.Prefixes.Add(Url);

            subtreesResponseCode = ResponseCode.ServiceUnavailable;
            response = Responses.ServiceUnavailable;
            recommendedProtocol = protocol;
        }

        public int Port { get; }

        public string Url => $"http://localhost:{Port}/";

        public void Start()
        {
            listener.Start();

            Task.Run(RequestHandlingLoop);
        }

        public void Stop()
        {
            listener.Stop();
        }

        public void Dispose()
        {
            Stop();

            listener.Close();
        }

        public void SetResponse(ResponseCode code)
        {
            subtreesResponseCode = code;
            response = new Response(code);
        }

        public void SetResponse(ISettingsNode tree, DateTime version)
        {
            var (serialized, newMap) = SerializeTree(tree, out var hash);

            this.version = version;
            subtreesMap = newMap;
            this.hash = hash;
            serializedTree = serialized;
            subtreesResponseCode = ResponseCode.Ok;
            
            response = Responses.Ok
                .WithHeader(HeaderNames.LastModified, version.ToString("R"))
                .WithHeader(HeaderNames.ContentEncoding, "gzip")
                .WithHeader(ClusterConfigHeaderNames.ZoneHash, hash)
                .WithContent(serialized);
        }

        public void SetPatchResponse(ISettingsNode old, ISettingsNode @new, DateTime version, bool wrongHash)
        {
            var (serialized, _) = SerializeTree(new Patcher().GetPatch(old, @new), out _);
            SerializeTree(@new, out var hash);
            
            SetPatchResponse(serialized, hash + (wrongHash ? "_WRONG_HASH" : ""), version);
        }
        
        public void SetEmptyPatchResponse(ISettingsNode tree, DateTime version)
        {
            var (serialized, _) = SerializeTree(new Patcher().GetPatch(tree, tree), out _);
            SerializeTree(tree, out var hash);
            
            SetPatchResponse(serialized, hash, version);
        }
        
        public void SetPatchResponse(byte[] patch, string hash, DateTime version)
        {
            this.version = version;
            serializedPatch = patch;
            this.hash = hash;
            subtreesResponseCode = ResponseCode.Ok;
            response = Responses.PartialContent
                .WithHeader(HeaderNames.LastModified, version.ToString("R"))
                .WithHeader(HeaderNames.ContentEncoding, "gzip")
                .WithHeader(ClusterConfigHeaderNames.ZoneHash, hash)
                .WithContent(patch);
        }

        public void AssertRequestUrl(Action<Uri> validate) => validate(request.Url);

        public void SetRecommendedProtocol(ClusterConfigProtocolVersion protocol)
        {
            recommendedProtocol = protocol;
        }

        private void RequestHandlingLoop()
        {
            while (true)
            {
                var context = listener.GetContext();

                Task.Run(() => RespondAsync(context));
            }
        }

        private async Task RespondAsync(HttpListenerContext context)
        {
            request = context.Request;

            if (request.Url!.AbsolutePath == "/_v3_1/subtrees")
            {
                await RespondV3(context);
            }
            else
            {
                context.Response.StatusCode = (int)response.Code;

                if (response.HasHeaders)
                {
                    foreach (var header in response.Headers)
                        context.Response.AddHeader(header.Name, header.Value);
                }

                if (response.HasContent)
                {
                    context.Response.ContentLength64 = response.Content.Length;

                    await context.Response.OutputStream.WriteAsync(response.Content.Buffer, 0, response.Content.Length);
                }
            }

            context.Response.Close();
        }

        private async Task RespondV3(HttpListenerContext context)
        {
            if (subtreesResponseCode != ResponseCode.Ok)
            {
                context.Response.StatusCode = (int) subtreesResponseCode;
                return;
            }
            
            var listenerRequest = context.Request;
            var contentLength = (int)listenerRequest.ContentLength64;
            var requestBuffer = new byte[contentLength];
            var offset = 0;
            int readBytes;
            while ((readBytes = await listenerRequest.InputStream.ReadAsync(requestBuffer, offset, contentLength - offset)) > 0)
            {
                offset += readBytes;
            }

            var reader = new ArraySegmentReader(new ArraySegment<byte>(requestBuffer));
            var subtreeRequests = SubtreesRequestSerializer.Deserialize(reader, Encoding.UTF8);
            
            var responses = GetSubtreesResponse(subtreeRequests);
            var writer = new BinaryBufferWriter(100);
            SubtreesResponseSerializer.Serialize(writer, responses);

            context.Response.StatusCode = (int) ResponseCode.Ok;
            
            if (recommendedProtocol != null)
                context.Response.AddHeader(ClusterConfigHeaderNames.RecommendedProtocol, recommendedProtocol.ToString());
            context.Response.AddHeader(HeaderNames.LastModified, version.ToString("R"));
            
            context.Response.ContentLength64 = writer.Position;
            await context.Response.OutputStream.WriteAsync(writer.Buffer, 0, (int)writer.Position);
        }

        private Dictionary<string, Subtree> GetSubtreesResponse(List<SubtreeRequest> subtreeRequests)
        {
            var responses = new Dictionary<string, Subtree>();
            foreach (var subtreeRequest in subtreeRequests)
            {
                bool zoneModified = false;
                bool hasSubtree = false;
                bool isPatch = false;
                bool isCompressed = false;
                ArraySegment<byte> value = default;

                //(deniaa): Subtree was modified since previous version (or client send his first request).
                if (!subtreeRequest.Version.HasValue || (zoneModified = subtreeRequest.Version.Value < version))
                {
                    zoneModified = true;
                    //(deniaa): While we support patches only for full zone we handle exactly this case separately.
                    //(deniaa): Also we have a special logic to fallback to the root and we don't want to compress it on each request 
                    if (subtreeRequest.Prefix == "" || subtreeRequest.Prefix == "/")
                    {
                        if (!subtreeRequest.ForceFullUpdate && !HasReasonToFullUpdate(subtreeRequest.Version))
                        {
                            //(deniaa): Patch is always compressed
                            isCompressed = true;
                            isPatch = true;
                            hasSubtree = true;
                            value = new ArraySegment<byte>(serializedPatch);
                        }
                        else
                        {
                            isCompressed = true;
                            hasSubtree = true;
                            value = new ArraySegment<byte>(serializedTree);
                        }
                    }
                    //(deniaa): Other subtrees we handle as a full zone due to their small size (we believe).
                    else
                    {
                        //(deniaa): Here we assume that all prefixes in requests are normalized: no leading slashes, no slash sequences.
                        //(deniaa): This is guaranteed by code in ClusterConfig.Client. 
                        hasSubtree = subtreesMap.TryGetValue(subtreeRequest.Prefix, out value);
                    }
                }


                responses.Add(subtreeRequest.Prefix, new Subtree(zoneModified, hasSubtree, isPatch, isCompressed, value));
            }

            return responses;
        }

        private bool HasReasonToFullUpdate(DateTime? subtreeRequestVersion)
        {
            return subtreeRequestVersion == null || serializedPatch == null;
        }

        private (byte[], Dictionary<string, ArraySegment<byte>>) SerializeTree(ISettingsNode tree, out string hash)
        {
            var writer = new BinaryBufferWriter(64);

            protocol.GetSerializer(new RecyclingBoundedCache<string, string>(4)).Serialize(tree, writer);

            hash = writer.Buffer.GetSha256Str(0, writer.Length);

            var map = new Dictionary<string, ArraySegment<byte>>();
            if (protocol != ClusterConfigProtocolVersion.V1)
            {
                var buffer = new byte[writer.Length];
                Buffer.BlockCopy(writer.Buffer, 0, buffer, 0, writer.Length);
                var nodeReader = new SubtreesMapBuilder(new ArraySegmentReader(new ArraySegment<byte>(buffer)), Encoding.UTF8, null);
                map = nodeReader.BuildMap();
            }

            var stream = new MemoryStream();

            using (var gzip = new GZipStream(stream, CompressionMode.Compress))
            {
                gzip.Write(writer.Buffer, 0, writer.Length);
                gzip.Flush();
            }

            return (stream.ToArray(), map);
        }
    }
}