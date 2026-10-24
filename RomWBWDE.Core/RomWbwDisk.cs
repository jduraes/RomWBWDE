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

    public string Caption => CaptionFor(Index, LooksFormatted, FileCount, Label);

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
        if (TryReadPartition(device, out long start, out long claimed))
        {
            long available = Math.Min(claimed, Math.Max(0, device.Length - start));
            int count = (int)(available / SliceLength);
            var slices = new List<SliceMap>(count);
            for (int i = 0; i < count; i++)
            {
                long offset = start + i * SliceLength;
                slices.Add(Describe(device, i, offset));
            }

            return new RomWbwDisk(device, slices, "hd1k");
        }

        if (device.Length >= CpmFileSystem.BootLength + CpmFileSystem.DirectoryLength
            && LooksLikeDirectory(device, CpmFileSystem.BootLength))
        {
            long length = Math.Min(device.Length, SliceLength);
            return new RomWbwDisk(device, [Describe(device, 0, 0, length)], "slice");
        }

        throw new InvalidDataException(
            "This disk has no RomWBW partition (type 0x2E) and is not a single hd1k slice.");
    }

    public CpmFileSystem OpenSlice(int index)
    {
        SliceMap slice = FindSlice(index);
        var window = new WindowDevice(Device, slice.Offset, slice.Length, Device.DisplayName);
        return CpmFileSystem.Open(window, slice.Index, Device.IsPhysical);
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

    private static bool TryReadPartition(IBlockDevice device, out long start, out long claimed)
    {
        start = 0;
        claimed = 0;
        if (device.Length < 512)
            return false;

        byte[] mbr = new byte[512];
        device.Read(0, mbr, 0, 512);
        if (mbr[510] != 0x55 || mbr[511] != 0xAA)
            return false;

        for (int i = 0; i < 4; i++)
        {
            int entry = 446 + i * 16;
            if (mbr[entry + 4] != PartitionType)
                continue;

            uint lba = BitConverter.ToUInt32(mbr, entry + 8);
            uint sectors = BitConverter.ToUInt32(mbr, entry + 12);
            if (lba == 0 || sectors == 0)
                continue;

            start = (long)lba * 512;
            claimed = (long)sectors * 512;
            if (start >= device.Length)
                return false;
            return true;
        }

        return false;
    }

    private static SliceMap Describe(IBlockDevice device, int index, long offset, long length = SliceLength)
    {
        byte[] directory = new byte[CpmFileSystem.DirectoryLength];
        device.Read(offset + CpmFileSystem.BootLength, directory, 0, directory.Length);
        return DescribeDirectory(index, offset, length, directory);
    }

    internal static SliceMap DescribeDirectory(int index, long offset, long length, byte[] directory)
    {
        CpmFileSystem.DirectoryCheck check = CpmFileSystem.InspectDirectory(directory);
        return new SliceMap
        {
            Index = index,
            Offset = offset,
            Length = length,
            FileCount = check.FileCount,
            EmptyEntries = check.EmptyEntries,
            SampleName = check.SampleName,
            Label = check.Label,
            LooksFormatted = check.IsFormatted
        };
    }

    private static bool LooksLikeDirectory(IBlockDevice device, long directoryOffset)
    {
        if (directoryOffset + CpmFileSystem.DirectoryLength > device.Length)
            return false;

        byte[] directory = new byte[CpmFileSystem.DirectoryLength];
        device.Read(directoryOffset, directory, 0, directory.Length);
        SliceMap map = DescribeDirectory(0, 0, device.Length, directory);
        return map.LooksFormatted;
    }
}
