namespace RomWBWDE;

public sealed class FileBlockDevice : IBlockDevice
{
    private readonly FileStream _stream;

    public FileBlockDevice(string path, bool writable)
    {
        Path = path;
        _stream = new FileStream(
            path,
            FileMode.Open,
            writable ? FileAccess.ReadWrite : FileAccess.Read,
            FileShare.Read);
        CanWrite = writable;
    }

    public string Path { get; }

    public long Length => _stream.Length;

    public bool CanWrite { get; }

    public bool IsPhysical => false;

    public string DisplayName => System.IO.Path.GetFileName(Path);

    public void Read(long offset, byte[] buffer, int index, int count)
    {
        if (offset < 0 || count < 0 || offset + count > Length)
            throw new ArgumentOutOfRangeException(nameof(offset), "Read extends past the end of the image.");

        _stream.Position = offset;
        int got = 0;
        while (got < count)
        {
            int n = _stream.Read(buffer, index + got, count - got);
            if (n == 0)
                throw new EndOfStreamException("Short read from image.");
            got += n;
        }
    }

    public void Write(long offset, byte[] buffer, int index, int count)
    {
        if (!CanWrite)
            throw new IOException("This image was opened read-only.");
        if (offset < 0 || count < 0 || offset + count > Length)
            throw new ArgumentOutOfRangeException(nameof(offset), "Write extends past the end of the image.");

        _stream.Position = offset;
        _stream.Write(buffer, index, count);
        _stream.Flush();
    }

    public void Dispose() => _stream.Dispose();
}
