using System;
using System.IO;
using System.IO.Compression;
using FluentAssertions;
using NUnit.Framework;
using Vostok.Clusterclient.Core.Model;
using Vostok.ClusterConfig.Client.Helpers;

namespace Vostok.ClusterConfig.Client.Tests.Helpers
{
    [TestFixture]
    internal class GzipBodyTransform_Tests
    {
        private GzipBodyTransform transform;

        [SetUp]
        public void TestSetup()
        {
            transform = new GzipBodyTransform();
        }

        [TestCase(ResponseCode.NotFound)]
        [TestCase(ResponseCode.NotModified)]
        [TestCase(ResponseCode.ConnectFailure)]
        public void Should_not_alter_responses_with_codes_other_than_ok(ResponseCode code)
        {
            var response = new Response(code).WithContent("foo");

            transform.Transform(response).Should().BeSameAs(response);
        }

        [Test]
        public void Should_not_alter_responses_without_body()
        {
            var response = new Response(ResponseCode.Ok).WithHeader(HeaderNames.ContentEncoding, "gzip");

            transform.Transform(response).Should().BeSameAs(response);
        }

        [Test]
        public void Should_not_alter_responses_without_gzip_content_encoding()
        {
            var response = new Response(ResponseCode.Ok).WithContent("foo");

            transform.Transform(response).Should().BeSameAs(response);
        }

        [Test]
        public void Should_decompress_body_for_ok_responses_with_body_and_gzip_content_encoding()
        {
            var content = new byte[94534];

            new Random(Guid.NewGuid().GetHashCode()).NextBytes(content);

            var compressBuffer = new MemoryStream();
            var compressStream = new GZipStream(compressBuffer, CompressionMode.Compress);

            compressStream.Write(content, 0, content.Length);
            compressStream.Flush();

            var response = new Response(ResponseCode.Ok)
                .WithHeader(HeaderNames.ContentEncoding, "gzip")
                .WithContent(compressBuffer.ToArray());

            var transformedResponse = transform.Transform(response);

            transformedResponse.Code.Should().Be(response.Code);
            transformedResponse.Content.ToArray().Should().Equal(content);
        }
    }
}