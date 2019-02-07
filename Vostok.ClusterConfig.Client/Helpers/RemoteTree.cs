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

        public RemoteTree(byte[] serialized, ITreeSerializer serializer)
        {
            Serialized = serialized;

            this.serializer = serializer;
        }

        public int Size => Serialized?.Length ?? 0;

        public byte[] Serialized { get; }

        [CanBeNull]
        public ISettingsNode GetSettings(ClusterConfigPath path)
            => serializer.Deserialize(new BinaryBufferReader(Serialized, 0), path.Segments);
    }
}