﻿using System;
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

        public RemoteTree(ClusterConfigProtocolVersion protocol, byte[] serialized, ITreeSerializer serializer, [CanBeNull] string description)
        {
            Protocol = protocol;
            Serialized = serialized;
            Description = description;

            this.serializer = serializer;
        }

        public int Size => Serialized?.Length ?? 0;

        public ClusterConfigProtocolVersion Protocol { get; }
        
        public byte[] Serialized { get; }
        
        [CanBeNull]
        public string Description { get; }

        [CanBeNull]
        public ISettingsNode GetSettings(ClusterConfigPath path)
            => serializer.Deserialize(new BinaryBufferReader(Serialized, 0), path.Segments);
    }
}