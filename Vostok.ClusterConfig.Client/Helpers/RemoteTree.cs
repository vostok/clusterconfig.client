using JetBrains.Annotations;
using Vostok.ClusterConfig.Client.Abstractions;
using Vostok.ClusterConfig.Core.Serialization;
using Vostok.Commons.Binary;
using Vostok.Configuration.Abstractions.SettingsTree;

namespace Vostok.ClusterConfig.Client.Helpers
{
    internal class RemoteTree
    {
        private readonly byte[] serialized;
        private readonly ITreeSerializer serializer;

        public RemoteTree(byte[] serialized, ITreeSerializer serializer)
        {
            this.serialized = serialized;
            this.serializer = serializer;
        }

        [CanBeNull]
        public ISettingsNode GetSettings(ClusterConfigPath path)
            => serializer.Deserialize(new BinaryBufferReader(serialized, 0), path.Segments);
    }
}