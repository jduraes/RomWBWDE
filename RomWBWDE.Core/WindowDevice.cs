namespace RomWBWDE;

/// <summary>
/// A bounded window onto another device. Writes outside the window are refused.
/// </summary>
public sealed class WindowDevice : IBlockDevice
{
    private readonly IBlockDevice _inner;
    private readonly long _offset;

    public WindowDevice(IBlockDevice inner, long offset, long length, string displayName)
    {
        if (offset < 0 || length < 0 || offset + length > inner.Length)
            throw new ArgumentOutOfRangeException(nameof(length), "Slice window does not fit on the device.");

        _inner = inner;
        _offset = offset;
        Length = length;
        DisplayName = displayName;
    }

    public long Length { get; }

    public bool CanWrite => _inner.CanWrite;

    public bool IsPhysical => _inner.IsPhysical;

    public string DisplayName { get; }

    public long AbsoluteOffset => _offset;

    public void Read(long offset, byte[] buffer, int index, int count)
    {
        EnsureInside(offset, count);
        _inner.Read(_offset + offset, buffer, index, count);
    }

    public void Write(long offset, byte[] buffer, int index, int count)
    {
        EnsureInside(offset, count);
        _inner.Write(_offset + offset, buffer, index, count);
    }

    public void Dispose()
    {
        // The owning disk disposes the underlying device.
    }

    private void EnsureInside(long offset, int count)
    {
        if (offset < 0 || count < 0 || offset + count > Length)
            throw new IOException("Refusing to access bytes outside the selected slice.");
    }
}
