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

        public RemoteTree(ProtocolVersion protocol, byte[] serialized, ITreeSerializer serializer)
        {
            Protocol = protocol;
            Serialized = serialized;

            this.serializer = serializer;
        }

        public int Size => Serialized?.Length ?? 0;

        public ProtocolVersion Protocol { get; }
        
        public byte[] Serialized { get; }

        [CanBeNull]
        public ISettingsNode GetSettings(ClusterConfigPath path)
            => serializer.Deserialize(new BinaryBufferReader(Serialized, 0), path.Segments);
    }
}