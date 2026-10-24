using System.Management;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace RomWBWDE;

public sealed class DriveDescription
{
    public required int Index { get; init; }

    public required string Model { get; init; }

    public required long ReportedSize { get; init; }

    public required bool MediaLoaded { get; init; }

    public string Letters { get; init; } = "";

    public string DevicePath => $@"\\.\PHYSICALDRIVE{Index}";

    public string Summary
    {
        get
        {
            string letter = string.IsNullOrEmpty(Letters) ? "" : Letters + "    ";
            return $"{letter}{Model}    {FormatSize(ReportedSize)}    PHYSICALDRIVE{Index}";
        }
    }

    public override string ToString() => Summary;

    public static string FormatSize(long bytes)
    {
        if (bytes < 1024)
            return bytes + " B";
        double value = bytes;
        string[] units = ["KB", "MB", "GB", "TB"];
        int unit = -1;
        do
        {
            value /= 1024;
            unit++;
        }
        while (value >= 1024 && unit < units.Length - 1);
        return $"{value:0.##} {units[unit]}";
    }
}

public static class DriveCatalog
{
    public static IReadOnlyList<DriveDescription> RemovableDrives()
    {
        var drives = new List<DriveDescription>();
        using var searcher = new ManagementObjectSearcher("SELECT Index, Model, MediaType, Size, MediaLoaded FROM Win32_DiskDrive");
        foreach (ManagementObject drive in searcher.Get())
        {
            string media = Convert.ToString(drive["MediaType"]) ?? "";
            if (!media.Equals("Removable Media", StringComparison.OrdinalIgnoreCase))
                continue;

            long size = 0;
            if (drive["Size"] != null)
                size = Convert.ToInt64(drive["Size"]);
            if (size <= 0)
                continue;

            drives.Add(new DriveDescription
            {
                Index = Convert.ToInt32(drive["Index"]),
                Model = Convert.ToString(drive["Model"]) ?? "Removable disk",
                ReportedSize = size,
                MediaLoaded = drive["MediaLoaded"] is bool loaded && loaded
            });
        }

        return drives.OrderBy(d => d.Index).ToList();
    }

    public static string LookupLetters(int index)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher($"SELECT * FROM Win32_DiskDrive WHERE Index = {index}");
            foreach (ManagementObject drive in searcher.Get())
            {
                var letters = new List<string>();
                using var partitions = new ManagementObjectSearcher(
                    $"associators of {{{drive.Path.RelativePath}}} where AssocClass = Win32_DiskDriveToDiskPartition");
                foreach (ManagementObject partition in partitions.Get())
                {
                    using var logicals = new ManagementObjectSearcher(
                        $"associators of {{{partition.Path.RelativePath}}} where AssocClass = Win32_LogicalDiskToPartition");
                    foreach (ManagementObject logical in logicals.Get())
                    {
                        string? name = Convert.ToString(logical["Name"]);
                        if (!string.IsNullOrWhiteSpace(name))
                            letters.Add(name);
                    }
                }
                return string.Join(", ", letters);
            }
        }
        catch (ManagementException)
        {
        }
        return "";
    }
}

public sealed class PhysicalDrive : IBlockDevice
{
    private const uint GenericRead = 0x80000000;
    private const uint GenericWrite = 0x40000000;
    private const uint FileShareRead = 0x00000001;
    private const uint FileShareWrite = 0x00000002;
    private const uint OpenExisting = 3;
    private const uint IoctlDiskGetDriveGeometryEx = 0x700A0;
    private const uint FsctlDismountVolume = 0x00090020;

    private readonly FileStream _stream;
    private readonly string _relativePath;
    private bool _volumesDismounted;

    private PhysicalDrive(FileStream stream, int index, string model, long length, string relativePath, bool canWrite)
    {
        _stream = stream;
        Index = index;
        _relativePath = relativePath;
        Length = length;
        CanWrite = canWrite;
        DisplayName = $"{model} (PHYSICALDRIVE{index})";
    }

    public int Index { get; }

    public long Length { get; }

    public bool CanWrite { get; }

    public bool IsPhysical => true;

    public string DisplayName { get; }

    public static PhysicalDrive Open(int index, bool writable)
    {
        string relative = RelativePathFor(index);
        string model = ModelFor(index);
        uint access = writable ? GenericRead | GenericWrite : GenericRead;
        IntPtr raw = CreateFile(
            $@"\\.\PHYSICALDRIVE{index}",
            access,
            FileShareRead | FileShareWrite,
            IntPtr.Zero,
            OpenExisting,
            0,
            IntPtr.Zero);

        if (raw == InvalidHandle)
            throw new IOException($"Cannot open PHYSICALDRIVE{index}. {new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error()).Message} Run the explorer as administrator.");

        long length = ReadLength(raw);
        var stream = new FileStream(new SafeFileHandle(raw, ownsHandle: true), writable ? FileAccess.ReadWrite : FileAccess.Read);
        return new PhysicalDrive(stream, index, model, length, relative, writable);
    }

    public void Read(long offset, byte[] buffer, int index, int count)
    {
        if (offset < 0 || count < 0 || offset + count > Length)
            throw new ArgumentOutOfRangeException(nameof(offset), "Read extends past the end of the drive.");

        _stream.Position = offset;
        int got = 0;
        while (got < count)
        {
            int n = _stream.Read(buffer, index + got, count - got);
            if (n == 0)
                throw new EndOfStreamException("Short read from the drive.");
            got += n;
        }
    }

    public void Write(long offset, byte[] buffer, int index, int count)
    {
        if (!CanWrite)
            throw new IOException("This drive was opened read-only.");
        if (offset < 0 || count < 0 || offset + count > Length)
            throw new IOException("Refusing to write past the end of the drive.");

        DismountVolumes();
        _stream.Position = offset;
        _stream.Write(buffer, index, count);
        _stream.Flush();
    }

    public void Dispose() => _stream.Dispose();

    private void DismountVolumes()
    {
        if (_volumesDismounted)
            return;

        using var partitionQuery = new ManagementObjectSearcher(
            $"associators of {{{_relativePath}}} where AssocClass = Win32_DiskDriveToDiskPartition");
        foreach (ManagementObject partition in partitionQuery.Get())
        {
            using var logicalQuery = new ManagementObjectSearcher(
                $"associators of {{{partition.Path.RelativePath}}} where AssocClass = Win32_LogicalDiskToPartition");
            foreach (ManagementObject logical in logicalQuery.Get())
            {
                string? letter = Convert.ToString(logical["Name"]);
                if (string.IsNullOrWhiteSpace(letter))
                    continue;
                DismountVolume(letter);
            }
        }

        _volumesDismounted = true;
    }

    private static void DismountVolume(string driveName)
    {
        IntPtr volume = CreateFile(
            $@"\\.\{driveName}",
            GenericRead | GenericWrite,
            FileShareRead | FileShareWrite,
            IntPtr.Zero,
            OpenExisting,
            0,
            IntPtr.Zero);
        if (volume == InvalidHandle)
            return;

        try
        {
            // Best effort. A raw RomWBW partition often has no filesystem for Windows to lock.
            DeviceIoControl(volume, FsctlDismountVolume, IntPtr.Zero, 0, IntPtr.Zero, 0, out _, IntPtr.Zero);
        }
        finally
        {
            CloseHandle(volume);
        }
    }

    private static long ReadLength(IntPtr handle)
    {
        const int size = 256;
        IntPtr buffer = Marshal.AllocHGlobal(size);
        try
        {
            bool ok = DeviceIoControl(handle, IoctlDiskGetDriveGeometryEx, IntPtr.Zero, 0, buffer, size, out _, IntPtr.Zero);
            if (!ok)
                throw new IOException("Could not read the drive length. " + new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error()).Message);

            // DISK_GEOMETRY is 24 bytes; DiskSize follows it. The IOCTL also returns partition data after that.
            long diskSize = Marshal.ReadInt64(buffer, 24);
            if (diskSize <= 0)
                throw new IOException("The drive reported an empty length.");
            return diskSize;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static string RelativePathFor(int index)
    {
        using var searcher = new ManagementObjectSearcher($"SELECT * FROM Win32_DiskDrive WHERE Index = {index}");
        foreach (ManagementObject drive in searcher.Get())
            return drive.Path.RelativePath;
        throw new IOException($"PHYSICALDRIVE{index} was not found.");
    }

    private static string ModelFor(int index)
    {
        using var searcher = new ManagementObjectSearcher($"SELECT Model FROM Win32_DiskDrive WHERE Index = {index}");
        foreach (ManagementObject drive in searcher.Get())
            return Convert.ToString(drive["Model"]) ?? "Disk";
        return "Disk";
    }

    private static readonly IntPtr InvalidHandle = new(-1);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateFile(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        IntPtr lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(
        IntPtr hDevice,
        uint dwIoControlCode,
        IntPtr lpInBuffer,
        int nInBufferSize,
        IntPtr lpOutBuffer,
        int nOutBufferSize,
        out int lpBytesReturned,
        IntPtr lpOverlapped);

}
