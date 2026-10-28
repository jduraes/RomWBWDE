using System.Management;
using System.Runtime.InteropServices;
using System.Text;
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
    private const uint FileFlagNoBuffering = 0x20000000;
    private const uint FileFlagWriteThrough = 0x80000000;
    private const uint IoctlDiskGetDriveGeometryEx = 0x700A0;
    private const uint FsctlDismountVolume = 0x00090020;
    private const uint FsctlLockVolume = 0x00090018;
    private const uint FsctlUnlockVolume = 0x0009001C;
    private const uint IoctlVolumeGetDiskExtents = 0x00560000;
    private const uint IoctlVolumeOffline = 0x56C00C;
    private const uint IoctlVolumeOnline = 0x56C008;
    private const uint IoctlStorageQueryProperty = 0x2D1400;
    private const int ErrorNotReady = 21;

    private readonly SafeFileHandle _handle;
    private readonly int _sectorSize;
    private readonly int _maxChunk;
    private readonly List<SafeFileHandle> _heldVolumes = new();
    private bool _volumesDismounted;

    private PhysicalDrive(SafeFileHandle handle, int index, string model, long length, int sectorSize, int maxChunk, bool canWrite)
    {
        _handle = handle;
        _sectorSize = sectorSize;
        _maxChunk = maxChunk;
        Index = index;
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
        string model = ModelFor(index);
        uint access = writable ? GenericRead | GenericWrite : GenericRead;
        uint flags = FileFlagNoBuffering | (writable ? FileFlagWriteThrough : 0);
        IntPtr raw = CreateFile(
            $@"\\.\PHYSICALDRIVE{index}",
            access,
            FileShareRead | FileShareWrite,
            IntPtr.Zero,
            OpenExisting,
            flags,
            IntPtr.Zero);

        if (raw == InvalidHandle)
            throw new IOException($"Cannot open PHYSICALDRIVE{index}. {Win32()} Run the explorer as administrator.");

        var handle = new SafeFileHandle(raw, ownsHandle: true);
        (long length, int sector) = ReadGeometry(handle);
        int maxChunk = ReadMaxChunk(handle, sector);
        return new PhysicalDrive(handle, index, model, length, sector, maxChunk, writable);
    }

    public void Read(long offset, byte[] buffer, int index, int count)
    {
        if (offset < 0 || count < 0 || offset + count > Length)
            throw new ArgumentOutOfRangeException(nameof(offset), "Read extends past the end of the drive.");

        Transfer(write: false, offset, buffer, index, count);
    }

    public void Write(long offset, byte[] buffer, int index, int count)
    {
        if (!CanWrite)
            throw new IOException("This drive was opened read-only.");
        if (offset < 0 || count < 0 || offset + count > Length)
            throw new IOException("Refusing to write past the end of the drive.");

        DismountVolumes();
        Transfer(write: true, offset, buffer, index, count);
        if (!FlushFileBuffers(_handle))
            throw new IOException("Could not flush the drive. " + Win32());
    }

    public void Dispose()
    {
        foreach (SafeFileHandle handle in _heldVolumes)
        {
            try
            {
                Control(handle, IoctlVolumeOnline);
                Control(handle, FsctlUnlockVolume);
            }
            catch (ObjectDisposedException)
            {
            }
            handle.Dispose();
        }
        _heldVolumes.Clear();
        _handle.Dispose();
    }

    private void DismountVolumes()
    {
        if (_volumesDismounted)
            return;

        // The type 0x2E partition is a Windows volume even when it has no drive letter.
        // Until that volume is locked, writes to the physical disk do not reach the card.
        List<SafeFileHandle> volumes = VolumesOnThisDisk();
        try
        {
            foreach (SafeFileHandle volume in volumes)
            {
                if (!Hold(volume))
                    throw new IOException("Windows would not release the card (" + Marshal.GetLastWin32Error() + "). Close anything else using it and try again.");
                _heldVolumes.Add(volume);
            }
            // A mounted RomWBW partition refuses writes with "the device is not ready"
            // until it has been taken offline and brought back.
            SettleVolumes();
            _volumesDismounted = true;
        }
        catch
        {
            foreach (SafeFileHandle volume in volumes)
                volume.Dispose();
            _heldVolumes.Clear();
            throw;
        }
    }

    private static bool Hold(SafeFileHandle volume)
    {
        if (Control(volume, FsctlLockVolume))
        {
            Control(volume, FsctlDismountVolume);
            return true;
        }

        return Control(volume, IoctlVolumeOffline);
    }

    private void SettleVolumes()
    {
        foreach (SafeFileHandle volume in _heldVolumes)
        {
            Control(volume, IoctlVolumeOffline);
            Control(volume, IoctlVolumeOnline);
        }
    }

    private void Transfer(bool write, long offset, byte[] buffer, int index, int count)
    {
        if (count == 0)
            return;

        int sector = _sectorSize;
        long start = offset / sector * sector;
        long end = (offset + count + sector - 1) / sector * sector;
        int lead = (int)(offset - start);
        int span = (int)(end - start);
        if (lead == 0 && span == count)
        {
            Move(write, offset, buffer, index, count);
            return;
        }

        byte[] covered = new byte[span];
        Move(write: false, start, covered, 0, span);
        if (write)
        {
            Buffer.BlockCopy(buffer, index, covered, lead, count);
            Move(write: true, start, covered, 0, span);
            return;
        }

        Buffer.BlockCopy(covered, lead, buffer, index, count);
    }

    private void Move(bool write, long offset, byte[] buffer, int index, int count)
    {
        int chunk = _maxChunk;
        IntPtr raw = Marshal.AllocHGlobal(chunk + 4096);
        try
        {
            long aligned = (raw.ToInt64() + 4095) & ~4095L;
            int done = 0;
            int recovered = 0;
            while (done < count)
            {
                int n = Math.Min(chunk, count - done);
                var pointer = new IntPtr(aligned);
                if (write)
                    Marshal.Copy(buffer, index + done, pointer, n);
                if (!SetFilePointerEx(_handle, offset + done, out _, 0))
                    throw new IOException("Could not seek on the drive. " + Win32());

                bool ok = write
                    ? WriteFile(_handle, pointer, n, out int got, IntPtr.Zero)
                    : ReadFile(_handle, pointer, n, out got, IntPtr.Zero);
                if (!ok || got != n)
                {
                    int error = Marshal.GetLastWin32Error();
                    if (write && !ok && error == ErrorNotReady && recovered < 2)
                    {
                        recovered++;
                        SettleVolumes();
                        continue;
                    }
                    string detail = ok
                        ? "The drive transferred " + got + " of " + n + " bytes."
                        : new System.ComponentModel.Win32Exception(error).Message;
                    throw new IOException((write ? "Could not write the drive. " : "Could not read the drive. ") + detail);
                }
                if (!write)
                    Marshal.Copy(pointer, buffer, index + done, n);
                done += n;
            }
        }
        finally
        {
            Marshal.FreeHGlobal(raw);
        }
    }

    private List<SafeFileHandle> VolumesOnThisDisk()
    {
        var found = new List<SafeFileHandle>();
        var name = new StringBuilder(260);
        IntPtr search = FindFirstVolume(name, name.Capacity);
        if (search == InvalidHandle)
            return found;

        try
        {
            do
            {
                SafeFileHandle? volume = OpenVolume(name.ToString());
                if (volume != null)
                    found.Add(volume);
                name.Clear();
            }
            while (FindNextVolume(search, name, name.Capacity));
            return found;
        }
        catch
        {
            foreach (SafeFileHandle handle in found)
                handle.Dispose();
            throw;
        }
        finally
        {
            FindVolumeClose(search);
        }
    }

    private SafeFileHandle? OpenVolume(string volumeName)
    {
        string path = volumeName.TrimEnd('\\');
        if (path.Length == 0)
            return null;

        IntPtr probe = CreateFile(path, 0, FileShareRead | FileShareWrite, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
        if (probe == InvalidHandle)
            return null;

        using var probeHandle = new SafeFileHandle(probe, ownsHandle: true);
        if (!BelongsToThisDisk(probeHandle))
            return null;

        IntPtr raw = CreateFile(path, GenericRead | GenericWrite, FileShareRead | FileShareWrite, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
        if (raw == InvalidHandle)
            throw new IOException("Windows would not release the card (" + Marshal.GetLastWin32Error() + "). Close anything else using it and try again.");

        return new SafeFileHandle(raw, ownsHandle: true);
    }

    private bool BelongsToThisDisk(SafeFileHandle volume)
    {
        const int size = 512;
        IntPtr buffer = Marshal.AllocHGlobal(size);
        try
        {
            if (!Control(volume, IoctlVolumeGetDiskExtents, buffer, size))
                return false;

            int count = Marshal.ReadInt32(buffer);
            for (int i = 0; i < count && i < 16; i++)
            {
                int disk = Marshal.ReadInt32(buffer, 8 + i * 24);
                if (disk == Index)
                    return true;
            }
            return false;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static (long Length, int SectorSize) ReadGeometry(SafeFileHandle handle)
    {
        const int size = 256;
        IntPtr buffer = Marshal.AllocHGlobal(size);
        try
        {
            if (!Control(handle, IoctlDiskGetDriveGeometryEx, buffer, size))
                throw new IOException("Could not read the drive length. " + Win32());

            // DISK_GEOMETRY is 24 bytes: BytesPerSector is at 20, and DiskSize follows the struct.
            int sector = Marshal.ReadInt32(buffer, 20);
            long diskSize = Marshal.ReadInt64(buffer, 24);
            if (sector < 512 || (sector & (sector - 1)) != 0)
                sector = 512;
            if (diskSize <= 0)
                throw new IOException("The drive reported an empty length.");
            return (diskSize, sector);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static int ReadMaxChunk(SafeFileHandle handle, int sector)
    {
        const int fallback = 256 * 1024;
        IntPtr query = Marshal.AllocHGlobal(12);
        IntPtr output = Marshal.AllocHGlobal(64);
        try
        {
            Marshal.WriteInt32(query, 0, 1);
            Marshal.WriteInt32(query, 4, 0);
            if (!DeviceIoControl(handle, IoctlStorageQueryProperty, query, 12, output, 64, out _, IntPtr.Zero))
                return fallback;

            int max = Marshal.ReadInt32(output, 8);
            if (max < sector)
                return fallback;
            max -= max % sector;
            if (max > 1024 * 1024)
                max = 1024 * 1024;
            return max;
        }
        finally
        {
            Marshal.FreeHGlobal(query);
            Marshal.FreeHGlobal(output);
        }
    }

    private static string Win32() => new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error()).Message;

    private static bool Control(SafeFileHandle handle, uint code) =>
        Control(handle, code, IntPtr.Zero, 0);

    private static bool Control(SafeFileHandle handle, uint code, IntPtr buffer, int size) =>
        DeviceIoControl(handle, code, IntPtr.Zero, 0, buffer, size, out _, IntPtr.Zero);

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
    private static extern bool DeviceIoControl(
        SafeFileHandle hDevice,
        uint dwIoControlCode,
        IntPtr lpInBuffer,
        int nInBufferSize,
        IntPtr lpOutBuffer,
        int nOutBufferSize,
        out int lpBytesReturned,
        IntPtr lpOverlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ReadFile(SafeFileHandle hFile, IntPtr buffer, int count, out int read, IntPtr overlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool WriteFile(SafeFileHandle hFile, IntPtr buffer, int count, out int written, IntPtr overlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetFilePointerEx(SafeFileHandle hFile, long distance, out long next, uint method);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool FlushFileBuffers(SafeFileHandle hFile);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr FindFirstVolume(StringBuilder name, int capacity);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool FindNextVolume(IntPtr find, StringBuilder name, int capacity);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool FindVolumeClose(IntPtr find);

}
