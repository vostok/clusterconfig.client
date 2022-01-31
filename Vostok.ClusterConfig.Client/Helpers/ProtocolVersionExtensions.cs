using System;
using Vostok.ClusterConfig.Client.Exceptions;
using Vostok.ClusterConfig.Core.Patching;
using Vostok.ClusterConfig.Core.Serialization;

namespace Vostok.ClusterConfig.Client.Helpers;

internal static class ProtocolVersionExtensions
{
    private static readonly TreeSerializerV1 TreeSerializerV1 = new();
    private static readonly TreeSerializerV2 TreeSerializerV2 = new();
    
    public static string GetUrlPath(this ClusterConfigProtocolVersion protocol) => protocol switch
    {
        ClusterConfigProtocolVersion.V1 => "_v1/zone",
        ClusterConfigProtocolVersion.V2 => "_v2/zone",
        var x => throw new InvalidOperationException($"Unknown protocol version '{x}'")
    };

    public static ITreeSerializer GetSerializer(this ClusterConfigProtocolVersion protocol) => protocol switch
    {
        ClusterConfigProtocolVersion.V1 => TreeSerializerV1,
        ClusterConfigProtocolVersion.V2 => TreeSerializerV2,
        var x => throw new InvalidOperationException($"Unknown protocol version '{x}'")
    };

    public static IBinaryPatcher GetPatcher(this ClusterConfigProtocolVersion protocol) => protocol switch
    {
        ClusterConfigProtocolVersion.V1 => throw new NotSupportedException("Protocol V1 doesn't supports patching"),
        ClusterConfigProtocolVersion.V2 => TreeSerializerV2,
        var x => throw new InvalidOperationException($"Unknown protocol version '{x}'")
    };
}