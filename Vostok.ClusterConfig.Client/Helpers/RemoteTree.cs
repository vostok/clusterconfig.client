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

        public RemoteTree(ArraySegment<byte>? serialized, ITreeSerializer serializer)
        {
            Serialized = serialized;

            this.serializer = serializer;
        }

        public int Size => Serialized?.Count ?? 0;
        
        public ArraySegment<byte>? Serialized { get; }

        [CanBeNull]
        public ISettingsNode GetSettings(ClusterConfigPath path, string rootName)
        {
            return serializer.Deserialize(new ArraySegmentReader(Serialized!.Value), path.Segments, rootName);
        }
    }
}