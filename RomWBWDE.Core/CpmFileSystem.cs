namespace RomWBWDE;

public sealed class CpmFile
{
    public required int User { get; init; }

    public required string Name { get; init; }

    public required string Type { get; init; }

    public required int Size { get; init; }

    public string DisplayName => Type.Length == 0 ? Name : Name + "." + Type;

    public string Key => User + ":" + Name + "." + Type;
}

/// <summary>
/// One RomWBW hd1k CP/M 2.2 filesystem (8 MiB, 4 KB blocks, 1024 directory entries, 2 reserved tracks).
/// Changes stay in memory until <see cref="Save"/>, which writes only the blocks that changed.
/// </summary>
public sealed class CpmFileSystem : IDisposable
{
    public const int SectorLength = 512;
    public const int SectorsPerTrack = 16;
    public const int BootTracks = 2;
    public const int BootLength = BootTracks * SectorsPerTrack * SectorLength;
    public const int BlockSize = 4096;
    public const int DirectoryEntries = 1024;
    public const int EntryLength = 32;
    public const int DirectoryLength = DirectoryEntries * EntryLength;
    public const int RecordsPerLogicalExtent = 128;
    public const int LogicalExtentSize = RecordsPerLogicalExtent * 128;
    public const int ExtentsPerEntry = 2;
    public const int BlocksPerEntry = 8;
    public const int EntryCapacity = ExtentsPerEntry * LogicalExtentSize;

    private readonly IBlockDevice _slice;
    private readonly byte[] _data;
    private readonly HashSet<int> _dirtyBlocks = [];
    private byte[] _bootSnapshot;

    private CpmFileSystem(IBlockDevice slice, int sliceIndex, bool isPhysical, byte[] data)
    {
        _slice = slice;
        _data = data;
        SliceIndex = sliceIndex;
        IsPhysical = isPhysical;
        _bootSnapshot = data.AsSpan(0, BootLength).ToArray();
        BlockCount = (data.Length - BootLength) / BlockSize;
        DirectoryBlocks = DirectoryLength / BlockSize;
    }

    public int SliceIndex { get; }

    public bool IsPhysical { get; }

    public bool IsDirty => _dirtyBlocks.Count > 0;

    public int BlockCount { get; }

    public int DirectoryBlocks { get; }

    public string DisplayName => _slice.DisplayName;

    public static CpmFileSystem Open(IBlockDevice slice, int sliceIndex, bool isPhysical)
    {
        if (slice.Length < BootLength + DirectoryLength)
            throw new InvalidDataException("The slice is too small to hold an hd1k filesystem.");

        int usable = (int)Math.Min(slice.Length, RomWbwDisk.SliceLength);
        byte[] data = new byte[usable];
        slice.Read(0, data, 0, data.Length);
        return new CpmFileSystem(slice, sliceIndex, isPhysical, data);
    }

    public IReadOnlyList<CpmFile> List()
    {
        if (!HasCpMDirectory)
            return [];

        var grouped = new Dictionary<string, List<DirEntry>>(StringComparer.Ordinal);
        var order = new List<string>();
        foreach (DirEntry entry in ReadEntries())
        {
            if (!grouped.TryGetValue(entry.Key, out List<DirEntry>? list))
            {
                list = [];
                grouped.Add(entry.Key, list);
                order.Add(entry.Key);
            }
            list.Add(entry);
        }

        var files = new List<CpmFile>(order.Count);
        foreach (string key in order)
        {
            List<DirEntry> entries = grouped[key];
            DirEntry highest = entries.OrderBy(e => e.Extent).Last();
            files.Add(new CpmFile
            {
                User = highest.User,
                Name = highest.Name,
                Type = highest.Type,
                Size = SizeOf(highest)
            });
        }

        return files;
    }

    public int FreeBytes
    {
        get
        {
            bool[] used = UsedBlocks();
            int free = 0;
            for (int block = DirectoryBlocks; block < BlockCount; block++)
            {
                if (!used[block])
                    free++;
            }
            return free * BlockSize;
        }
    }

    public bool HasCpMDirectory => InspectDirectory(_data.AsSpan(BootLength, DirectoryLength)).IsFormatted;

    /// <summary>How many 4 KB blocks fit in one hd1k slice after the system tracks.</summary>
    public const int Hd1kBlockCount = (int)((RomWbwDisk.SliceLength - BootLength) / BlockSize);

    public readonly record struct DirectoryCheck(int FileCount, int EmptyEntries, string? SampleName, bool IsFormatted, string? Label);

    /// <summary>
    /// A CP/M directory is either mostly empty entries (0xE5) or a run of real filenames.
    /// Bytes that were never formatted fail this, so they are not shown as files.
    /// </summary>
    public static DirectoryCheck InspectDirectory(ReadOnlySpan<byte> directory)
    {
        int empty = 0;
        int validEntries = 0;
        string? sample = null;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var names = new List<(string Name, string Type)>();
        int entries = Math.Min(DirectoryEntries, directory.Length / EntryLength);
        for (int i = 0; i < entries; i++)
        {
            int at = i * EntryLength;
            if (directory[at] == 0xE5)
            {
                empty++;
                continue;
            }

            if (!TryParseName(directory, at, out int user, out string name, out string type))
                continue;
            if (!BlockPointersFit(directory, at))
                continue;

            validEntries++;
            string key = user + ":" + name + "." + type;
            if (seen.Add(key))
            {
                sample ??= type.Length == 0 ? name : name + "." + type;
                names.Add((name, type));
            }
        }

        bool formatted = empty > entries / 2 || validEntries >= 8 || (seen.Count > 0 && empty > 32);
        if (!formatted)
            return new DirectoryCheck(0, empty, null, false, null);
        return new DirectoryCheck(seen.Count, empty, sample, true, SliceIdentity.Describe(names));
    }

    public byte[] ReadFile(CpmFile file)
    {
        List<DirEntry> entries = EntriesFor(file.User, file.Name, file.Type);
        if (entries.Count == 0)
            throw new FileNotFoundException("The file is no longer in this slice.", file.DisplayName);

        int size = SizeOf(entries.OrderBy(e => e.Extent).Last());
        byte[] data = new byte[size];
        for (int pos = 0; pos < size;)
        {
            int logical = pos / LogicalExtentSize;
            DirEntry? entry = null;
            foreach (DirEntry candidate in entries)
            {
                if (candidate.Extent / ExtentsPerEntry == logical / ExtentsPerEntry)
                {
                    entry = candidate;
                    break;
                }
            }
            if (entry == null)
                throw new InvalidDataException($"{file.DisplayName} is missing extent {logical}.");

            int within = pos % EntryCapacity;
            int pointer = within / BlockSize;
            int blockOffset = within % BlockSize;
            int count = Math.Min(BlockSize - blockOffset, size - pos);
            int block = entry.Value.Blocks[pointer];
            if (block != 0)
            {
                int source = BootLength + block * BlockSize + blockOffset;
                if (source < 0 || source + count > _data.Length)
                    throw new InvalidDataException($"{file.DisplayName} points outside the slice.");
                Buffer.BlockCopy(_data, source, data, pos, count);
            }
            pos += count;
        }

        return data;
    }

    /// <summary>
    /// Fills the directory with empty entries. The 16 KB system tracks and the file data blocks are left as they are.
    /// </summary>
    public void InitializeDirectory()
    {
        Array.Fill(_data, (byte)0xE5, BootLength, DirectoryLength);
        for (int block = 0; block < DirectoryBlocks; block++)
            _dirtyBlocks.Add(block);
    }

    public void WriteFile(int user, string name, string type, byte[] data)
    {
        if (user is < 0 or > 31)
            throw new ArgumentOutOfRangeException(nameof(user), "CP/M user number must be 0 to 31.");
        name = NormalizeComponent(name, 8);
        type = NormalizeComponent(type, 3);
        if (name.Length == 0)
            throw new ArgumentException("The file name is empty.", nameof(name));

        int blocksNeeded = data.Length == 0 ? 0 : (data.Length + BlockSize - 1) / BlockSize;
        int entriesNeeded = data.Length == 0 ? 1 : (data.Length + EntryCapacity - 1) / EntryCapacity;
        bool stamped = UsesDateStamps();

        var reusableSlots = new List<int>();
        var reusableBlocks = new HashSet<int>();
        foreach (DirEntry entry in ReadEntries())
        {
            if (entry.User == user && entry.Name == name && entry.Type == type)
            {
                reusableSlots.Add(entry.Index);
                foreach (int block in entry.Blocks)
                {
                    if (block >= DirectoryBlocks)
                        reusableBlocks.Add(block);
                }
            }
        }

        var freeSlots = new List<int>();
        for (int i = 0; i < DirectoryEntries && freeSlots.Count < entriesNeeded; i++)
        {
            if (stamped && i % 4 == 3)
                continue;
            byte status = _data[BootLength + i * EntryLength];
            if (status == 0xE5 || reusableSlots.Contains(i))
                freeSlots.Add(i);
        }
        if (freeSlots.Count < entriesNeeded)
            throw new IOException($"The directory on this slice has no room for {name}.{type}.");

        bool[] used = UsedBlocks();
        foreach (int block in reusableBlocks)
            used[block] = false;
        var freeBlocks = new List<int>();
        for (int block = DirectoryBlocks; block < BlockCount && freeBlocks.Count < blocksNeeded; block++)
        {
            if (!used[block])
                freeBlocks.Add(block);
        }
        if (freeBlocks.Count < blocksNeeded)
            throw new IOException($"Slice does not have {blocksNeeded * BlockSize} free bytes for {name}.{type}.");

        foreach (int slot in reusableSlots)
            Array.Fill(_data, (byte)0xE5, BootLength + slot * EntryLength, EntryLength);

        int dataOffset = 0;
        int blockCursor = 0;
        int slotCursor = 0;
        while (slotCursor < entriesNeeded)
        {
            int chunk = data.Length == 0 ? 0 : Math.Min(EntryCapacity, data.Length - dataOffset);
            int chunkBlocks = chunk == 0 ? 0 : (chunk + BlockSize - 1) / BlockSize;
            var pointers = new int[BlocksPerEntry];
            for (int i = 0; i < chunkBlocks; i++)
            {
                int block = freeBlocks[blockCursor++];
                pointers[i] = block;
                int destination = BootLength + block * BlockSize;
                Array.Clear(_data, destination, BlockSize);
                int copy = Math.Min(BlockSize, chunk - i * BlockSize);
                Buffer.BlockCopy(data, dataOffset + i * BlockSize, _data, destination, copy);
                _dirtyBlocks.Add(block);
            }

            int extent;
            int recordCount;
            int lastRecord;
            if (chunk == 0)
            {
                extent = 0;
                recordCount = 0;
                lastRecord = 0;
            }
            else
            {
                int end = dataOffset + chunk;
                extent = (end - 1) / LogicalExtentSize;
                recordCount = ((end - 1) % LogicalExtentSize) / 128 + 1;
                lastRecord = end % 128;
            }

            WriteEntry(freeSlots[slotCursor], user, name, type, extent, recordCount, lastRecord, pointers);
            dataOffset += chunk;
            slotCursor++;
            if (data.Length == 0)
                break;
        }
    }

    public void DeleteFile(int user, string name, string type)
    {
        bool found = false;
        foreach (DirEntry entry in ReadEntries())
        {
            if (entry.User != user || entry.Name != name || entry.Type != type)
                continue;
            found = true;
            Array.Fill(_data, (byte)0xE5, BootLength + entry.Index * EntryLength, EntryLength);
            MarkDirectorySlot(entry.Index);
        }
        if (!found)
            throw new FileNotFoundException("The file is no longer in this slice.", name + "." + type);
    }

    public void Save()
    {
        if (_dirtyBlocks.Count == 0)
            return;
        if (!_slice.CanWrite)
            throw new IOException("This slice was opened read-only.");

        if (!BootUntouched())
            throw new IOException("The system tracks of this slice were modified. The write was cancelled.");

        byte[] block = new byte[BlockSize];
        foreach (int index in _dirtyBlocks.OrderBy(i => i))
        {
            if (index < 0 || index >= BlockCount)
                throw new IOException("A dirty block is outside this slice.");
            int source = BootLength + index * BlockSize;
            Buffer.BlockCopy(_data, source, block, 0, BlockSize);
            _slice.Write(source, block, 0, BlockSize);
        }
        _dirtyBlocks.Clear();
    }

    public void Dispose()
    {
        if (_slice is WindowDevice)
            _slice.Dispose();
    }

    public static bool TryParseName(ReadOnlySpan<byte> directory, int at, out int user, out string name, out string type)
    {
        user = directory[at];
        name = "";
        type = "";
        if (user > 31)
            return false;

        Span<char> chars = stackalloc char[11];
        for (int i = 0; i < 11; i++)
        {
            int raw = directory[at + 1 + i];
            // Only the three filetype bytes carry CP/M attribute flags in bit 7.
            // Stripping that bit on the filename turns random data into fake names.
            int value = i < 8 ? raw : raw & 0x7F;
            if (value < 32 || value > 126)
                return false;
            chars[i] = (char)value;
        }

        name = new string(chars[..8]).TrimEnd();
        type = new string(chars[8..]).TrimEnd();
        return name.Length > 0;
    }

    public static (string Name, string Type) SplitHostName(string fileName)
    {
        string bare = Path.GetFileName(fileName);
        string name = Path.GetFileNameWithoutExtension(bare);
        string type = Path.GetExtension(bare);
        if (type.StartsWith('.'))
            type = type[1..];
        return (NormalizeComponent(name, 8), NormalizeComponent(type, 3));
    }

    private bool BootUntouched()
    {
        for (int i = 0; i < BootLength; i++)
        {
            if (_data[i] != _bootSnapshot[i])
                return false;
        }
        return true;
    }

    private bool UsesDateStamps()
    {
        for (int i = 3; i < DirectoryEntries; i += 4)
        {
            if (_data[BootLength + i * EntryLength] == 0x21)
                return true;
        }
        return false;
    }

    private void WriteEntry(int index, int user, string name, string type, int extent, int recordCount, int lastRecord, int[] pointers)
    {
        int at = BootLength + index * EntryLength;
        Array.Clear(_data, at, EntryLength);
        _data[at] = (byte)user;
        for (int i = 0; i < 8; i++)
            _data[at + 1 + i] = (byte)(i < name.Length ? name[i] : ' ');
        for (int i = 0; i < 3; i++)
            _data[at + 9 + i] = (byte)(i < type.Length ? type[i] : ' ');
        _data[at + 12] = (byte)(extent & 0x1F);
        _data[at + 13] = (byte)(lastRecord & 0x7F);
        _data[at + 14] = (byte)((extent >> 5) & 0x3F);
        _data[at + 15] = (byte)recordCount;
        for (int i = 0; i < BlocksPerEntry; i++)
        {
            _data[at + 16 + i * 2] = (byte)(pointers[i] & 0xFF);
            _data[at + 17 + i * 2] = (byte)((pointers[i] >> 8) & 0xFF);
        }
        MarkDirectorySlot(index);
    }

    private void MarkDirectorySlot(int index)
    {
        _dirtyBlocks.Add((index * EntryLength) / BlockSize);
    }

    private static bool BlockPointersFit(ReadOnlySpan<byte> directory, int at)
    {
        for (int b = 0; b < BlocksPerEntry; b++)
        {
            int block = directory[at + 16 + b * 2] | (directory[at + 17 + b * 2] << 8);
            if (block >= Hd1kBlockCount)
                return false;
        }
        return true;
    }

    private static int SizeOf(DirEntry highest)
    {
        if (highest.RecordCount == 0)
            return highest.Extent * LogicalExtentSize;

        int size = highest.Extent * LogicalExtentSize;
        size += (highest.RecordCount - 1) * 128;
        size += highest.LastRecordLength == 0 ? 128 : highest.LastRecordLength;
        return size;
    }

    private bool[] UsedBlocks()
    {
        var used = new bool[BlockCount];
        for (int i = 0; i < DirectoryBlocks && i < used.Length; i++)
            used[i] = true;
        foreach (DirEntry entry in ReadEntries())
        {
            foreach (int block in entry.Blocks)
            {
                if (block >= DirectoryBlocks && block < used.Length)
                    used[block] = true;
            }
        }
        return used;
    }

    private List<DirEntry> EntriesFor(int user, string name, string type)
    {
        var matches = new List<DirEntry>();
        foreach (DirEntry entry in ReadEntries())
        {
            if (entry.User == user && entry.Name == name && entry.Type == type)
                matches.Add(entry);
        }
        return matches;
    }

    private IEnumerable<DirEntry> ReadEntries()
    {
        for (int i = 0; i < DirectoryEntries; i++)
        {
            int at = BootLength + i * EntryLength;
            if (!TryParseName(_data, at, out int user, out string name, out string type))
                continue;
            if (!BlockPointersFit(_data, at))
                continue;

            int extent = (_data[at + 12] & 0x1F) | ((_data[at + 14] & 0x3F) << 5);
            var blocks = new int[BlocksPerEntry];
            for (int b = 0; b < BlocksPerEntry; b++)
                blocks[b] = _data[at + 16 + b * 2] | (_data[at + 17 + b * 2] << 8);

            yield return new DirEntry(
                i,
                user,
                name,
                type,
                extent,
                _data[at + 15],
                _data[at + 13],
                blocks);
        }
    }

    private static string NormalizeComponent(string value, int length)
    {
        var chars = new List<char>(length);
        foreach (char c in value.ToUpperInvariant())
        {
            if (chars.Count == length)
                break;
            if (c is >= 'A' and <= 'Z' or >= '0' and <= '9' or '!' or '#' or '$' or '%' or '&' or '\'' or '(' or ')' or '-' or '@' or '^' or '_' or '{' or '}' or '~')
                chars.Add(c);
        }
        return new string(chars.ToArray());
    }

    private readonly record struct DirEntry(
        int Index,
        int User,
        string Name,
        string Type,
        int Extent,
        int RecordCount,
        int LastRecordLength,
        int[] Blocks)
    {
        public string Key => User + ":" + Name + "." + Type;
    }
}
