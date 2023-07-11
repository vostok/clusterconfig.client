using System;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Threading.Tasks;
using Vostok.Clusterclient.Core.Model;
using Vostok.ClusterConfig.Client.Abstractions;
using Vostok.ClusterConfig.Client.Helpers;
using Vostok.ClusterConfig.Core.Http;
using Vostok.ClusterConfig.Core.Patching;
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
        private volatile Response response;
        private volatile Uri request;

        public TestServer(ClusterConfigProtocolVersion protocol)
        {
            this.protocol = protocol;
            Port = FreeTcpPortFinder.GetFreePort();

            listener = new HttpListener();
            listener.Prefixes.Add(Url);

            response = Responses.ServiceUnavailable;
        }

        public int Port { get; }

        public string Url => $"http://localhost:{Port}/";

        public void Start()
        {
            listener.Start();

            Task.Run(() => RequestHandlingLoop());
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
            => response = new Response(code);

        public void SetResponse(ISettingsNode tree, DateTime version)
        {
            var serialized = SerializeTree(tree, out var hash);
            
            response = Responses.Ok
                .WithHeader(HeaderNames.LastModified, version.ToString("R"))
                .WithHeader(HeaderNames.ContentEncoding, "gzip")
                .WithHeader(ClusterConfigHeaderNames.ZoneHash, hash)
                .WithContent(serialized);
        }

        public void SetPatchResponse(ISettingsNode old, ISettingsNode @new, DateTime version, bool wrongHash)
        {
            var serialized = SerializeTree(new Patcher().GetPatch(old, @new), out _);
            SerializeTree(@new, out var hash);
            
            SetPatchResponse(serialized, hash + (wrongHash ? "_WRONG_HASH" : ""), version);
        }
        
        public void SetEmptyPatchResponse(ISettingsNode tree, DateTime version)
        {
            var serialized = SerializeTree(new Patcher().GetPatch(tree, tree), out _);
            SerializeTree(tree, out var hash);
            
            SetPatchResponse(serialized, hash, version);
        }
        
        public void SetPatchResponse(byte[] patch, string hash, DateTime version)
        {
            response = Responses.PartialContent
                .WithHeader(HeaderNames.LastModified, version.ToString("R"))
                .WithHeader(HeaderNames.ContentEncoding, "gzip")
                .WithHeader(ClusterConfigHeaderNames.ZoneHash, hash)
                .WithContent(patch);
        }

        public void AsserRequest(Action<Uri> validate) => validate(request);

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
            request = context.Request.Url;
            
            context.Response.StatusCode = (int) response.Code;

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

            context.Response.Close();
        }

        private byte[] SerializeTree(ISettingsNode tree, out string hash)
        {
            var writer = new BinaryBufferWriter(64);

            protocol.GetSerializer(new RecyclingBoundedCache<string, string>(4)).Serialize(tree, writer);

            hash = writer.Buffer.GetSha256Str(0, writer.Length);
            
            var buffer = new MemoryStream();

            using (var gzip = new GZipStream(buffer, CompressionMode.Compress))
            {
                gzip.Write(writer.Buffer, 0, writer.Length);
                gzip.Flush();
            }

            return buffer.ToArray();
        }
    }
}