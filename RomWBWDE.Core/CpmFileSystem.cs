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
    private readonly CpmLayout _layout;
    private readonly byte[] _data;
    private readonly HashSet<int> _dirtyBlocks = [];
    private byte[] _bootSnapshot;

    private CpmFileSystem(IBlockDevice slice, int sliceIndex, bool isPhysical, CpmLayout layout, byte[] data)
    {
        _slice = slice;
        _layout = layout;
        _data = data;
        SliceIndex = sliceIndex;
        IsPhysical = isPhysical;
        _bootSnapshot = layout.BootLength == 0 ? [] : data.AsSpan(0, layout.BootLength).ToArray();
        int fitted = (data.Length - layout.BootLength) / layout.BlockSize;
        BlockCount = Math.Min(layout.BlockCount, fitted);
        DirectoryBlocks = layout.DirectoryLength / layout.BlockSize;
    }

    public int SliceIndex { get; }

    public bool IsPhysical { get; }

    public bool IsDirty => _dirtyBlocks.Count > 0;

    public int BlockCount { get; }

    public int DirectoryBlocks { get; }

    public string DisplayName => _slice.DisplayName;

    public static CpmFileSystem Open(IBlockDevice slice, int sliceIndex, bool isPhysical)
        => Open(slice, sliceIndex, isPhysical, CpmLayout.Hd1k);

    public static CpmFileSystem Open(IBlockDevice slice, int sliceIndex, bool isPhysical, CpmLayout layout)
    {
        if (slice.Length < layout.BootLength + layout.DirectoryLength)
            throw new InvalidDataException("The slice is too small to hold this CP/M filesystem.");

        int usable = (int)Math.Min(slice.Length, RomWbwDisk.SliceLength);
        byte[] data = new byte[usable];
        slice.Read(0, data, 0, data.Length);
        return new CpmFileSystem(slice, sliceIndex, isPhysical, layout, data);
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
            return free * _layout.BlockSize;
        }
    }

    public bool HasCpMDirectory => InspectDirectory(_data.AsSpan(_layout.BootLength, _layout.DirectoryLength), _layout).IsFormatted;

    /// <summary>How many 4 KB blocks fit in one hd1k slice after the system tracks.</summary>
    public const int Hd1kBlockCount = (int)((RomWbwDisk.SliceLength - BootLength) / BlockSize);

    public readonly record struct DirectoryCheck(int FileCount, int EmptyEntries, string? SampleName, bool IsFormatted, string? Label);

    /// <summary>
    /// A CP/M directory is either mostly empty entries (0xE5) or a run of real filenames.
    /// Bytes that were never formatted fail this, so they are not shown as files.
    /// </summary>
    public static DirectoryCheck InspectDirectory(ReadOnlySpan<byte> directory)
        => InspectDirectory(directory, CpmLayout.Hd1k);

    public static DirectoryCheck InspectDirectory(ReadOnlySpan<byte> directory, CpmLayout layout)
    {
        int empty = 0;
        int validEntries = 0;
        string? sample = null;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var names = new List<(string Name, string Type)>();
        int entries = Math.Min(layout.DirectoryEntries, directory.Length / EntryLength);
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
            if (!BlockPointersFit(directory, at, layout))
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
                if (candidate.Extent / _layout.ExtentsPerEntry == logical / _layout.ExtentsPerEntry)
                {
                    entry = candidate;
                    break;
                }
            }
            if (entry == null)
                throw new InvalidDataException($"{file.DisplayName} is missing extent {logical}.");

            int within = pos % _layout.EntryCapacity;
            int pointer = within / _layout.BlockSize;
            int blockOffset = within % _layout.BlockSize;
            int count = Math.Min(_layout.BlockSize - blockOffset, size - pos);
            int block = entry.Value.Blocks[pointer];
            if (block != 0)
            {
                int source = _layout.BootLength + block * _layout.BlockSize + blockOffset;
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
        Array.Fill(_data, (byte)0xE5, _layout.BootLength, _layout.DirectoryLength);
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

        int blocksNeeded = data.Length == 0 ? 0 : (data.Length + _layout.BlockSize - 1) / _layout.BlockSize;
        int entriesNeeded = data.Length == 0 ? 1 : (data.Length + _layout.EntryCapacity - 1) / _layout.EntryCapacity;
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
        for (int i = 0; i < _layout.DirectoryEntries && freeSlots.Count < entriesNeeded; i++)
        {
            if (stamped && i % 4 == 3)
                continue;
            byte status = _data[_layout.BootLength + i * EntryLength];
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
            throw new IOException($"Slice does not have {blocksNeeded * _layout.BlockSize} free bytes for {name}.{type}.");

        foreach (int slot in reusableSlots)
            Array.Fill(_data, (byte)0xE5, _layout.BootLength + slot * EntryLength, EntryLength);

        int dataOffset = 0;
        int blockCursor = 0;
        int slotCursor = 0;
        while (slotCursor < entriesNeeded)
        {
            int chunk = data.Length == 0 ? 0 : Math.Min(_layout.EntryCapacity, data.Length - dataOffset);
            int chunkBlocks = chunk == 0 ? 0 : (chunk + _layout.BlockSize - 1) / _layout.BlockSize;
            var pointers = new int[_layout.BlocksPerEntry];
            for (int i = 0; i < chunkBlocks; i++)
            {
                int block = freeBlocks[blockCursor++];
                pointers[i] = block;
                int destination = _layout.BootLength + block * _layout.BlockSize;
                Array.Clear(_data, destination, _layout.BlockSize);
                int copy = Math.Min(_layout.BlockSize, chunk - i * _layout.BlockSize);
                Buffer.BlockCopy(data, dataOffset + i * _layout.BlockSize, _data, destination, copy);
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
            Array.Fill(_data, (byte)0xE5, _layout.BootLength + entry.Index * EntryLength, EntryLength);
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

        byte[] block = new byte[_layout.BlockSize];
        foreach (int index in _dirtyBlocks.OrderBy(i => i))
        {
            if (index < 0 || index >= BlockCount)
                throw new IOException("A dirty block is outside this slice.");
            int source = _layout.BootLength + index * _layout.BlockSize;
            Buffer.BlockCopy(_data, source, block, 0, _layout.BlockSize);
            _slice.Write(source, block, 0, _layout.BlockSize);
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
        for (int i = 0; i < _layout.BootLength; i++)
        {
            if (_data[i] != _bootSnapshot[i])
                return false;
        }
        return true;
    }

    private bool UsesDateStamps()
    {
        for (int i = 3; i < _layout.DirectoryEntries; i += 4)
        {
            if (_data[_layout.BootLength + i * EntryLength] == 0x21)
                return true;
        }
        return false;
    }

    private void WriteEntry(int index, int user, string name, string type, int extent, int recordCount, int lastRecord, int[] pointers)
    {
        int at = _layout.BootLength + index * EntryLength;
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
        for (int i = 0; i < _layout.BlocksPerEntry; i++)
            WritePointer(_data, at, i, pointers[i], _layout);
        MarkDirectorySlot(index);
    }

    private void MarkDirectorySlot(int index)
    {
        _dirtyBlocks.Add((index * EntryLength) / _layout.BlockSize);
    }

    private static bool BlockPointersFit(ReadOnlySpan<byte> directory, int at, CpmLayout layout)
    {
        for (int b = 0; b < layout.BlocksPerEntry; b++)
        {
            if (ReadPointer(directory, at, b, layout) >= layout.BlockCount)
                return false;
        }
        return true;
    }

    private static int ReadPointer(ReadOnlySpan<byte> directory, int at, int slot, CpmLayout layout)
    {
        int width = layout.WidePointers ? 2 : 1;
        int block = directory[at + 16 + slot * width];
        if (layout.WidePointers)
            block |= directory[at + 17 + slot * width] << 8;
        return block;
    }

    private static void WritePointer(byte[] data, int at, int slot, int block, CpmLayout layout)
    {
        int width = layout.WidePointers ? 2 : 1;
        data[at + 16 + slot * width] = (byte)(block & 0xFF);
        if (layout.WidePointers)
            data[at + 17 + slot * width] = (byte)((block >> 8) & 0xFF);
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
        for (int i = 0; i < _layout.DirectoryEntries; i++)
        {
            int at = _layout.BootLength + i * EntryLength;
            if (!TryParseName(_data, at, out int user, out string name, out string type))
                continue;
            if (!BlockPointersFit(_data, at, _layout))
                continue;

            int extent = (_data[at + 12] & 0x1F) | ((_data[at + 14] & 0x3F) << 5);
            var blocks = new int[_layout.BlocksPerEntry];
            for (int b = 0; b < _layout.BlocksPerEntry; b++)
                blocks[b] = ReadPointer(_data, at, b, _layout);

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
