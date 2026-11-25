namespace VCDiffSharp;

/// <summary>Controls the number of source hash buckets used during encoding.</summary>
public enum MemoryMode
{
    /// <summary>Double the rounded bucket count to reduce collisions.</summary>
    Default,

    /// <summary>Round the number of eight-byte source blocks up to a power of two.</summary>
    LessMemory
}
