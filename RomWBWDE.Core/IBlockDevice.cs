namespace RomWBWDE;

/// <summary>
/// Random-access storage. Offsets are byte offsets from the start of the device.
/// </summary>
public interface IBlockDevice : IDisposable
{
    long Length { get; }

    bool CanWrite { get; }

    /// <summary>True when this is a physical drive rather than a file.</summary>
    bool IsPhysical { get; }

    string DisplayName { get; }

    void Read(long offset, byte[] buffer, int index, int count);

    void Write(long offset, byte[] buffer, int index, int count);
}
