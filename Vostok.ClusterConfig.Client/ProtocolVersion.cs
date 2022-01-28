using System;
using JetBrains.Annotations;

namespace Vostok.ClusterConfig.Client
{
    /// <summary>
    /// ClusterConfig communication protocols.
    /// </summary>
    [PublicAPI]
    public enum ProtocolVersion
    {
        // V0 = 0, // reserved for legacy protocol
        
        /// <summary>
        /// First binary protocol. Not very effective and now considered obsolete. 
        /// </summary>
        [Obsolete("Use V2 instead")]
        V1 = 1,
        
        /// <summary>
        /// Second binary protocol. Very effective: send patches for zone instead full zone.
        /// </summary>
        V2 = 2
    }
}