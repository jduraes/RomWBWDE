namespace RomWBWDE;

/// <summary>
/// RomWBW keeps a slice name in the third sector. A bootable slice also has
/// the 5A A5 signature, but the name is read whenever those 16 bytes are a
/// label. "Unlabeled" is the placeholder written when no name was given.
/// </summary>
public static class SliceVolume
{
    public const int PrefixLength = 1536;
    public const int SignatureOffset = 1408;
    public const int LabelOffset = 1511;
    public const int LabelLength = 16;

    public static string? Read(IBlockDevice device, long sliceOffset)
    {
        if (sliceOffset < 0 || sliceOffset + PrefixLength > device.Length)
            return null;

        byte[] prefix = new byte[PrefixLength];
        device.Read(sliceOffset, prefix, 0, prefix.Length);
        return Parse(prefix);
    }

    public static string? Parse(ReadOnlySpan<byte> prefix)
    {
        if (prefix.Length < LabelOffset + LabelLength)
            return null;

        var chars = new List<char>(LabelLength);
        for (int i = 0; i < LabelLength; i++)
        {
            byte value = prefix[LabelOffset + i];
            if (value is 0 or (byte)'$')
                break;
            if (value < 32 || value > 126)
                return null;
            chars.Add((char)value);
        }

        string label = new string(chars.ToArray()).Trim();
        if (label.Length == 0 || label.Equals("Unlabeled", StringComparison.OrdinalIgnoreCase))
            return null;
        return label;
    }

    /// <summary>Checks a name before it is written into the third sector.</summary>
    public static string Normalize(string name)
    {
        string trimmed = name.Trim();
        if (trimmed.Length == 0 || trimmed.Length > LabelLength)
            throw new IOException("A slice name is 1 to 16 characters.");
        foreach (char c in trimmed)
        {
            if (c < 32 || c > 126 || c == '$')
                throw new IOException("A slice name uses plain text, without a $ sign.");
        }
        return trimmed;
    }

    /// <summary>
    /// Writes the name into one sector of the slice. The sector begins at byte 1024.
    /// Bytes outside that sector are not touched.
    /// </summary>
    public static void Stamp(Span<byte> thirdSector, string name)
    {
        int at = LabelOffset - 1024;
        for (int i = 0; i < LabelLength; i++)
            thirdSector[at + i] = i < name.Length ? (byte)name[i] : (byte)'$';
        thirdSector[1527 - 1024] = (byte)'$';
    }
}
