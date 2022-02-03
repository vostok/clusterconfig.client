using System;
using Vostok.ClusterConfig.Client.Exceptions;
using Vostok.ClusterConfig.Core.Patching;
using Vostok.ClusterConfig.Core.Serialization;

namespace Vostok.ClusterConfig.Client.Helpers
{
    internal static class ProtocolVersionExtensions
    {
        private static readonly TreeSerializerV1 TreeSerializerV1 = new TreeSerializerV1();
        private static readonly TreeSerializerV2 TreeSerializerV2 = new TreeSerializerV2();

        public static string GetUrlPath(this ClusterConfigProtocolVersion protocol)
        {
            switch (protocol)
            {
                case ClusterConfigProtocolVersion.V1:
                    return "_v1/zone";
                case ClusterConfigProtocolVersion.V2:
                    return "_v2/zone";
                case var x:
                    throw new InvalidOperationException($"Unknown protocol version '{x}'");
            }
        }

        public static ITreeSerializer GetSerializer(this ClusterConfigProtocolVersion protocol)
        {
            switch (protocol)
            {
                case ClusterConfigProtocolVersion.V1:
                    return TreeSerializerV1;
                case ClusterConfigProtocolVersion.V2:
                    return TreeSerializerV2;
                case var x:
                    throw new InvalidOperationException($"Unknown protocol version '{x}'");
            }
        }

        public static IBinaryPatcher GetPatcher(this ClusterConfigProtocolVersion protocol)
        {
            switch (protocol)
            {
                case ClusterConfigProtocolVersion.V1:
                    throw new NotSupportedException("Protocol V1 doesn't supports patching");
                case ClusterConfigProtocolVersion.V2:
                    return TreeSerializerV2;
                case var x:
                    throw new InvalidOperationException($"Unknown protocol version '{x}'");
            }
        }
    }
}