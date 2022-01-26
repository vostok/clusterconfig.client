using System;
using Vostok.ClusterConfig.Client.Abstractions;
using Vostok.ClusterConfig.Core.Serialization;

namespace Vostok.ClusterConfig.Client.Helpers;

internal static class ProtocolVersionExtensions
{
    public static string GetUrlPath(this ProtocolVersion protocol) => protocol switch
    {
        ProtocolVersion.V1 => "_v1/zone",
        ProtocolVersion.V2 => "_v2/zone",
        var x => throw new InvalidOperationException($"Unknown protocol version '{x}'")
    };

    public static ITreeSerializer GetSerializer(this ProtocolVersion protocol) => protocol switch
    {
        ProtocolVersion.V1 => TreeSerializers.V1,
        ProtocolVersion.V2 => TreeSerializers.V2,
        var x => throw new InvalidOperationException($"Unknown protocol version '{x}'")
    };
}