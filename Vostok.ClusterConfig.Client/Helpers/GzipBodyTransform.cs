using System.IO;
using System.IO.Compression;
using Vostok.Clusterclient.Core.Model;
using Vostok.Clusterclient.Core.Transforms;

namespace Vostok.ClusterConfig.Client.Helpers
{
    internal class GzipBodyTransform : IResponseTransform
    {
        public Response Transform(Response response)
        {
            if (response.Code != ResponseCode.Ok)
                return response;

            if (!response.HasContent)
                return response;

            if (response.Headers.ContentEncoding != "gzip")
                return response;

            var gzipStream = new GZipStream(response.Content.ToMemoryStream(), CompressionMode.Decompress);

            var bufferStream = new MemoryStream(response.Content.Length);

            gzipStream.CopyTo(bufferStream);

            return response.WithContent(bufferStream.ToArray());
        }
    }
}