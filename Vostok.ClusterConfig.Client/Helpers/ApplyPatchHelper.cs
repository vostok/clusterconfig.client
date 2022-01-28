using System;
using Vostok.ClusterConfig.Core.Serialization;
using Vostok.Commons.Binary;

namespace Vostok.ClusterConfig.Client.Helpers;

internal static class ApplyPatchHelper
{
    private static readonly TreeSerializerV2 TreeSerializerV2 = new();

    public static byte[] ApplyV2Patch(this byte[] old, byte[] patch)
    {
        var writer = new BinaryBufferWriter(4096);

        TreeSerializerV2.ApplyPatch(new(old, 0), new(patch, 0), writer);

        var result = new byte[writer.Length];

        Buffer.BlockCopy(writer.Buffer, 0, result, 0, result.Length);

        return result;
    }
}