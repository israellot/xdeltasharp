namespace XDeltaSharp;

/// <summary>Controls the number of hash buckets used by the encoder's match indexes.</summary>
public enum MemoryMode
{
    /// <summary>Double the rounded bucket count to reduce collisions.</summary>
    Default,

    /// <summary>Use the smaller power-of-two bucket count for each index.</summary>
    LessMemory
}
