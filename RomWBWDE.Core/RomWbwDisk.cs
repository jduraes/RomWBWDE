namespace RomWBWDE;

public sealed class SliceMap
{
    public required int Index { get; init; }

    public required long Offset { get; init; }

    public required long Length { get; init; }

    public int FileCount { get; init; }

    public int EmptyEntries { get; init; }

    public string? SampleName { get; init; }

    public string? Label { get; init; }

    public bool LooksFormatted { get; init; }

    public string Layout { get; init; } = "hd1k";

    public string Caption => CaptionFor(Index, LooksFormatted, FileCount, Label);

    public static string? PreferLabel(string? layout, string? identity, int fileCount)
    {
        if (layout == "minez" && fileCount > 0 && identity is null or "CP/M")
            return "Min-eZ";
        return identity;
    }

    public override string ToString() => Caption;

    public static string CaptionFor(int index, bool formatted, int fileCount, string? label)
    {
        string number = index.ToString("00");
        if (!formatted || fileCount == 0 || string.IsNullOrEmpty(label))
            return number + "    unused";
        return number + "    " + label;
    }
}

/// <summary>
/// RomWBW modern (hd1k) disk: a type 0x2E partition holding sequential 8 MiB slices.
/// A file that is itself one slice is treated as a single slice at offset 0.
/// </summary>
public sealed class RomWbwDisk : IDisposable
{
    public const int PartitionType = 0x2E;
    public const int MinEzPartitionType = 0x52;
    public const long SliceLength = 8_388_608;
    public const long PrefixLength = 1_048_576;

    private RomWbwDisk(IBlockDevice device, IReadOnlyList<SliceMap> slices, string kind)
    {
        Device = device;
        Slices = slices;
        Kind = kind;
    }

    public IBlockDevice Device { get; }

    public IReadOnlyList<SliceMap> Slices { get; }

    /// <summary>"hd1k" for a partitioned disk, "slice" for a single filesystem image.</summary>
    public string Kind { get; }

    public static RomWbwDisk Open(IBlockDevice device)
    {
        if (TryReadPartition(device, PartitionType, out long start, out long claimed))
        {
            long available = Math.Min(claimed, Math.Max(0, device.Length - start));
            int count = (int)(available / SliceLength);
            var slices = new List<SliceMap>(count);
            for (int i = 0; i < count; i++)
            {
                long offset = start + i * SliceLength;
                slices.Add(Describe(device, i, offset, CpmLayout.Hd1k));
            }

            return new RomWbwDisk(device, slices, "hd1k");
        }

        List<SliceMap> cpm = MineEzPartitions(device);
        if (cpm.Count > 0)
            return new RomWbwDisk(device, cpm, "minez");

        bool hd1k = TryReadDirectory(device, CpmLayout.Hd1k.BootLength, CpmLayout.Hd1k, out CpmFileSystem.DirectoryCheck hd);
        bool minez = TryReadDirectory(device, CpmLayout.MinEz.BootLength, CpmLayout.MinEz, out CpmFileSystem.DirectoryCheck mz);
        long length = Math.Min(device.Length, SliceLength);
        if (minez && mz.IsFormatted && mz.FileCount > (hd1k ? hd.FileCount : 0))
            return new RomWbwDisk(device, [Describe(device, 0, 0, CpmLayout.MinEz, length)], "minez");
        if (hd1k && hd.IsFormatted)
            return new RomWbwDisk(device, [Describe(device, 0, 0, CpmLayout.Hd1k, length)], "slice");
        if (minez && mz.IsFormatted)
            return new RomWbwDisk(device, [Describe(device, 0, 0, CpmLayout.MinEz, length)], "minez");

        throw new InvalidDataException(
            "This disk has no RomWBW partition (type 0x2E), no Min-eZ CP/M partition (type 0x52), and is not a single slice.");
    }

    public CpmFileSystem OpenSlice(int index)
    {
        SliceMap slice = FindSlice(index);
        var window = new WindowDevice(Device, slice.Offset, slice.Length, Device.DisplayName);
        CpmLayout layout = slice.Layout == CpmLayout.MinEz.Name ? CpmLayout.MinEz : CpmLayout.Hd1k;
        return CpmFileSystem.Open(window, slice.Index, Device.IsPhysical, layout);
    }

    /// <summary>
    /// Replaces one full slice with a full slice from another RomWBW image.
    /// Bytes outside that slice, including the partition table, are not written.
    /// </summary>
    public void ReplaceSlice(int destinationIndex, RomWbwDisk source, int sourceIndex)
    {
        SliceMap destination = FindSlice(destinationIndex);
        SliceMap origin = source.FindSlice(sourceIndex);
        if (destination.Length != SliceLength || origin.Length != SliceLength)
            throw new IOException("Only a full 8 MiB slice can be written.");
        if (!Device.CanWrite)
            throw new IOException("This disk was opened read-only.");
        if (ReferenceEquals(Device, source.Device) && destination.Offset == origin.Offset)
            throw new IOException("Choose a different slice from the one open here.");

        var window = new WindowDevice(Device, destination.Offset, destination.Length, Device.DisplayName);
        byte[] buffer = new byte[1024 * 1024];
        long offset = 0;
        while (offset < SliceLength)
        {
            int count = (int)Math.Min(buffer.Length, SliceLength - offset);
            source.Device.Read(origin.Offset + offset, buffer, 0, count);
            window.Write(offset, buffer, 0, count);
            offset += count;
        }
    }

    /// <summary>
    /// Erases one full slice. The partition table and every other slice are not written.
    /// </summary>
    public void ClearSlice(int index)
    {
        SliceMap slice = FindSlice(index);
        if (slice.Length != SliceLength)
            throw new IOException("Only a full 8 MiB slice can be erased.");
        if (!Device.CanWrite)
            throw new IOException("This disk was opened read-only.");

        var window = new WindowDevice(Device, slice.Offset, slice.Length, Device.DisplayName);
        byte[] buffer = new byte[1024 * 1024];
        Array.Fill(buffer, (byte)0xE5);
        long offset = 0;
        while (offset < SliceLength)
        {
            int count = (int)Math.Min(buffer.Length, SliceLength - offset);
            window.Write(offset, buffer, 0, count);
            offset += count;
        }
    }

    /// <summary>
    /// Writes one slice to a new 8 MiB image. The image is the slice itself, which RomWBW opens as a single slice.
    /// Nothing on the source disk is written.
    /// </summary>
    public void SaveSlice(int index, string path)
    {
        SliceMap slice = FindSlice(index);
        if (slice.Length != SliceLength)
            throw new IOException("Only a full 8 MiB slice can be saved.");

        var window = new WindowDevice(Device, slice.Offset, slice.Length, Device.DisplayName);
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        byte[] buffer = new byte[1024 * 1024];
        long offset = 0;
        while (offset < SliceLength)
        {
            int count = (int)Math.Min(buffer.Length, SliceLength - offset);
            window.Read(offset, buffer, 0, count);
            stream.Write(buffer, 0, count);
            offset += count;
        }
    }

    private SliceMap FindSlice(int index) =>
        Slices.FirstOrDefault(slice => slice.Index == index)
        ?? throw new ArgumentOutOfRangeException(nameof(index), "That slice is not on this disk.");

    public void Dispose() => Device.Dispose();

    private static bool TryReadPartition(IBlockDevice device, int type, out long start, out long claimed)
    {
        start = 0;
        claimed = 0;
        foreach ((long offset, long length) in Partitions(device, type))
        {
            start = offset;
            claimed = length;
            return start < device.Length;
        }

        return false;
    }

    private static List<SliceMap> MineEzPartitions(IBlockDevice device)
    {
        var slices = new List<SliceMap>();
        foreach ((long offset, long length) in Partitions(device, MinEzPartitionType))
        {
            if (length != SliceLength || offset + length > device.Length)
                continue;
            slices.Add(Describe(device, slices.Count, offset, CpmLayout.MinEz));
        }

        return slices;
    }

    private static List<(long Offset, long Length)> Partitions(IBlockDevice device, int type)
    {
        var found = new List<(long, long)>();
        if (device.Length < 512)
            return found;

        byte[] mbr = new byte[512];
        device.Read(0, mbr, 0, 512);
        if (mbr[510] != 0x55 || mbr[511] != 0xAA)
            return found;

        for (int i = 0; i < 4; i++)
        {
            int entry = 446 + i * 16;
            if (mbr[entry + 4] != type)
                continue;

            uint lba = BitConverter.ToUInt32(mbr, entry + 8);
            uint sectors = BitConverter.ToUInt32(mbr, entry + 12);
            if (lba == 0 || sectors == 0)
                continue;

            found.Add(((long)lba * 512, (long)sectors * 512));
        }

        return found;
    }

    private static SliceMap Describe(IBlockDevice device, int index, long offset, CpmLayout layout, long length = SliceLength)
    {
        byte[] directory = new byte[layout.DirectoryLength];
        device.Read(offset + layout.BootLength, directory, 0, directory.Length);
        return DescribeDirectory(index, offset, length, directory, layout);
    }

    internal static SliceMap DescribeDirectory(int index, long offset, long length, byte[] directory, CpmLayout layout)
    {
        CpmFileSystem.DirectoryCheck check = CpmFileSystem.InspectDirectory(directory, layout);
        return new SliceMap
        {
            Index = index,
            Offset = offset,
            Length = length,
            FileCount = check.FileCount,
            EmptyEntries = check.EmptyEntries,
            SampleName = check.SampleName,
            Label = SliceMap.PreferLabel(layout.Name, check.Label, check.FileCount),
            LooksFormatted = check.IsFormatted,
            Layout = layout.Name
        };
    }

    private static bool TryReadDirectory(IBlockDevice device, long directoryOffset, CpmLayout layout, out CpmFileSystem.DirectoryCheck check)
    {
        check = default;
        if (directoryOffset < 0 || directoryOffset + layout.DirectoryLength > device.Length)
            return false;

        byte[] directory = new byte[layout.DirectoryLength];
        device.Read(directoryOffset, directory, 0, directory.Length);
        check = CpmFileSystem.InspectDirectory(directory, layout);
        return true;
    }
}
