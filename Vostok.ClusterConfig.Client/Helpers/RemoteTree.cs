using System;
using JetBrains.Annotations;
using Vostok.ClusterConfig.Client.Abstractions;
using Vostok.ClusterConfig.Core.Serialization;
using Vostok.Commons.Binary;
using Vostok.Configuration.Abstractions.SettingsTree;

namespace Vostok.ClusterConfig.Client.Helpers
{
    internal class RemoteTree
    {
        private readonly ITreeSerializer serializer;

        public RemoteTree(ArraySegment<byte>? serialized, ITreeSerializer serializer, [CanBeNull] string description)
        {
            Serialized = serialized;
            Description = description;

            this.serializer = serializer;
        }

        public int Size => Serialized?.Count ?? 0;
        
        public ArraySegment<byte>? Serialized { get; }
        
        [CanBeNull]
        public string Description { get; }

        [CanBeNull]
        public ISettingsNode GetSettings(ClusterConfigPath path, string rootName)
        {
            var segment = Serialized.Value;
            return serializer.Deserialize(new BinaryBufferReader(segment.Array, segment.Offset), path.Segments, rootName);
        }
    }
}