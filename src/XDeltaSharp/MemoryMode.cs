namespace XDeltaSharp;

/// <summary>Controls the number of hash buckets used by the encoder's match indexes.</summary>
/// <remarks>This setting affects encoding memory and search cost. Decoder output limits are set separately.</remarks>
public enum MemoryMode
{
    /// <summary>Double the rounded bucket count to reduce collisions.</summary>
    Default,

    /// <summary>Use fewer hash buckets to reduce index memory. More candidates may share a bucket.</summary>
    LessMemory
}
