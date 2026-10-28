namespace RomWBWDE;

/// <summary>
/// CP/M 2.2 geometry for one slice. Block numbers above 255 use 16-bit allocation pointers.
/// </summary>
public readonly record struct CpmLayout
{
    public string Name { get; init; }

    public int BootLength { get; init; }

    public int BlockSize { get; init; }

    public int DirectoryEntries { get; init; }

    public int ExtentsPerEntry { get; init; }

    public int BlockCount { get; init; }

    public int SliceBytes { get; init; }

    public int DirectoryLength => DirectoryEntries * 32;

    public int DirectoryBlocks => DirectoryLength / BlockSize;

    public bool WidePointers => BlockCount > 256;

    public int BlocksPerEntry => WidePointers ? 8 : 16;

    public int EntryCapacity => ExtentsPerEntry * 16384;

    /// <summary>RomWBW hd1k: 16 KB system tracks, 4 KB blocks, 1024 directory entries.</summary>
    public static CpmLayout Hd1k { get; } = new()
    {
        Name = "hd1k",
        BootLength = CpmFileSystem.BootLength,
        BlockSize = CpmFileSystem.BlockSize,
        DirectoryEntries = CpmFileSystem.DirectoryEntries,
        ExtentsPerEntry = CpmFileSystem.ExtentsPerEntry,
        BlockCount = CpmFileSystem.Hd1kBlockCount,
        SliceBytes = (int)RomWbwDisk.SliceLength
    };

    /// <summary>RomWBW hd512: 128 KB system tracks, 4 KB blocks, 512 directory entries, 8,519,680-byte slices.</summary>
    public static CpmLayout Hd512 { get; } = new()
    {
        Name = "hd512",
        BootLength = 16 * 16 * 512,
        BlockSize = 4096,
        DirectoryEntries = 512,
        ExtentsPerEntry = 2,
        BlockCount = 2048,
        SliceBytes = (int)RomWbwDisk.Hd512SliceLength
    };

    /// <summary>Min-eZ 8 MB partition: no system tracks, 8 KB blocks, 768 directory entries.</summary>
    public static CpmLayout MinEz { get; } = new()
    {
        Name = "minez",
        BootLength = 0,
        BlockSize = 8192,
        DirectoryEntries = 768,
        ExtentsPerEntry = 4,
        BlockCount = 8388608 / 8192,
        SliceBytes = 8388608
    };

    public static CpmLayout For(string name) => name switch
    {
        "hd512" => Hd512,
        "minez" => MinEz,
        _ => Hd1k
    };
}
