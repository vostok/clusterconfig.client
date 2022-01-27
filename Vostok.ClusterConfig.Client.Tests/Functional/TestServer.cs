using System;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Threading.Tasks;
using Vostok.Clusterclient.Core.Model;
using Vostok.ClusterConfig.Client.Abstractions;
using Vostok.ClusterConfig.Client.Helpers;
using Vostok.Commons.Binary;
using Vostok.Commons.Helpers.Network;
using Vostok.Configuration.Abstractions.SettingsTree;

namespace Vostok.ClusterConfig.Client.Tests.Functional
{
    internal class TestServer : IDisposable
    {
        private readonly ProtocolVersion protocol;
        private readonly HttpListener listener;
        private volatile Response response;

        public TestServer(ProtocolVersion protocol)
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
            => response = Responses.Ok
                .WithHeader(HeaderNames.LastModified, version.ToString("R"))
                .WithHeader(HeaderNames.ContentEncoding, "gzip")
                .WithContent(SerializeTree(tree));

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

        private byte[] SerializeTree(ISettingsNode tree)
        {
            var writer = new BinaryBufferWriter(64);

            protocol.GetSerializer().Serialize(tree, writer);

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