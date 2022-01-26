using System;
using Vostok.ClusterConfig.Core.Serialization;
using Vostok.Commons.Binary;
using Vostok.Commons.Collections;

namespace Vostok.ClusterConfig.Client.Helpers;

public static class ApplyPatchHelper
{
    private static readonly TreeSerializerV2 TreeSerializerV2 = new();
    private static readonly UnboundedObjectPool<BinaryBufferWriter> WriterPool = new(() => new BinaryBufferWriter(4096));

    public static byte[] ApplyV2Patch(this byte[] old, byte[] patch)
    {
        using var _ = WriterPool.Acquire(out var writer);

        writer.Reset();

        TreeSerializerV2.ApplyPatch(new(old, 0), new(patch, 0), writer);

        var result = new byte[writer.Length];

        Array.Copy(writer.Buffer, 0, result, 0, result.Length);

        return result;
    }
}