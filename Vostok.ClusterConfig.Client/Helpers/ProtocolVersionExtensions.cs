using System;
using Vostok.ClusterConfig.Client.Exceptions;
using Vostok.ClusterConfig.Core.Patching;
using Vostok.ClusterConfig.Core.Serialization;
using Vostok.Commons.Collections;

namespace Vostok.ClusterConfig.Client.Helpers
{
    internal static class ProtocolVersionExtensions
    {
        private static readonly TreeSerializerV1 TreeSerializerV1 = new TreeSerializerV1();

        public static string GetUrlPath(this ClusterConfigProtocolVersion protocol)
        {
            switch (protocol)
            {
                case ClusterConfigProtocolVersion.V1:
                    return "_v1/zone";
                case ClusterConfigProtocolVersion.V2:
                    return "_v2/zone";
                case ClusterConfigProtocolVersion.V3:
                    return "_v3/subtrees";
                case ClusterConfigProtocolVersion.V3_1:
                    return "_v3_1/subtrees";
                case var x:
                    throw new InvalidOperationException($"Unknown protocol version '{x}'");
            }
        }

        public static ITreeSerializer GetSerializer(
            this ClusterConfigProtocolVersion protocol, 
            RecyclingBoundedCache<string, string> interningCache)
        {
            return protocol switch
            {
                ClusterConfigProtocolVersion.V1 => TreeSerializerV1,
                ClusterConfigProtocolVersion.V2 => new TreeSerializerV2(interningCache),
                ClusterConfigProtocolVersion.V3 => new TreeSerializerV2(interningCache),
                ClusterConfigProtocolVersion.V3_1 => new TreeSerializerV2(interningCache),
                var x => throw new InvalidOperationException($"Unknown protocol version '{x}'")
            };
        }

        public static IBinaryPatcher GetPatcher(
            this ClusterConfigProtocolVersion protocol, 
            RecyclingBoundedCache<string, string> interningCache)
        {
            return protocol switch
            {
                ClusterConfigProtocolVersion.V1 => throw new NotSupportedException("Protocol V1 doesn't supports patching"),
                ClusterConfigProtocolVersion.V2 => new TreeSerializerV2(interningCache),
                ClusterConfigProtocolVersion.V3 => new TreeSerializerV2(interningCache),
                ClusterConfigProtocolVersion.V3_1 => new TreeSerializerV2(interningCache),
                var x => throw new InvalidOperationException($"Unknown protocol version '{x}'")
            };
        }
    }
}