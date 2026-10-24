using RomWBWDE;

namespace RomWBWDE.Tests;

internal static class Program
{
    private static int _failed;

    public static int Main()
    {
        string slicePath = Path.Combine(Path.GetTempPath(), "rc2014-slice0.img");
        if (!File.Exists(slicePath))
        {
            Console.Error.WriteLine("Missing " + slicePath);
            return 1;
        }

        ReadRealSlice(slicePath);
        SliceLabels();
        UnusedBytesAreNotFiles(slicePath);
        RoundTripOnCopy(slicePath);
        PartitionWindowDoesNotEscape(slicePath);
        SliceImageStaysInsideItsWindow(slicePath);
        ClearingOneSliceLeavesTheOthers(slicePath);
        ExtractedSliceIsARomWbwImage(slicePath);
        DateStampSlotsStayReserved();
        Console.WriteLine(_failed == 0 ? "All checks passed." : _failed + " checks failed.");
        return _failed == 0 ? 0 : 1;
    }

    private static void ReadRealSlice(string slicePath)
    {
        using var device = new FileBlockDevice(slicePath, writable: false);
        using RomWbwDisk disk = RomWbwDisk.Open(device);
        Check(disk.Kind == "slice", "raw slice image is one filesystem");
        Check(disk.Slices.Count == 1, "one slice");
        using CpmFileSystem fs = disk.OpenSlice(0);
        IReadOnlyList<CpmFile> files = fs.List();
        Check(files.Count == 362, "slice 0 has 362 files, got " + files.Count);
        Check(disk.Slices[0].Label == "NZ-COM", "slice label " + disk.Slices[0].Label);
        Check(disk.Slices[0].Caption == "00    NZ-COM", "slice caption " + disk.Slices[0].Caption);

        CpmFile nzcom = Find(files, 0, "NZCOM", "COM");
        Check(nzcom.Size == 10624, "NZCOM.COM size " + nzcom.Size);
        byte[] body = fs.ReadFile(nzcom);
        Check(body.Length == 10624, "NZCOM.COM read length");
        Check(body.Take(3).SequenceEqual(new byte[] { 0xC3, 0x0C, 0x01 }) || body.Any(b => b != 0), "NZCOM.COM has payload");

        CpmFile profile = Find(files, 0, "PROFILE", "SUB");
        Check(profile.Size == 18, "PROFILE.SUB size " + profile.Size);
        Check(fs.ReadFile(profile).Length == 18, "PROFILE.SUB read length");

        CpmFile fat = Find(files, 15, "FAT", "COM");
        Check(fat.Size == 40442, "FAT.COM size " + fat.Size);
        Check(fs.ReadFile(fat).Length == 40442, "FAT.COM read length");

        CpmFile zde = Find(files, 10, "ZDE", "DOC");
        Check(zde.Size == 47791, "ZDE.DOC size " + zde.Size);
    }

    private static void UnusedBytesAreNotFiles(string slicePath)
    {
        byte[] noise = new byte[CpmFileSystem.DirectoryLength];
        new Random(9).NextBytes(noise);
        CpmFileSystem.DirectoryCheck noiseCheck = CpmFileSystem.InspectDirectory(noise);
        Check(!noiseCheck.IsFormatted && noiseCheck.FileCount == 0, "random bytes are not a directory, files=" + noiseCheck.FileCount);

        byte[] slice = File.ReadAllBytes(slicePath);
        CpmFileSystem.DirectoryCheck real = CpmFileSystem.InspectDirectory(slice.AsSpan(CpmFileSystem.BootLength, CpmFileSystem.DirectoryLength));
        Check(real.IsFormatted && real.FileCount == 362, "slice 0 still has 362 files, got " + real.FileCount);
        Check(real.Label == "NZ-COM", "inspected label " + real.Label);
    }

    private static void SliceLabels()
    {
        Check(SliceIdentity.Describe([("NZCOM", "COM"), ("PIP", "COM"), ("STAT", "COM")]) == "NZ-COM", "nz-com");
        Check(SliceIdentity.Describe([("ZPM3", "COM")]) == "ZPM3", "zpm3");
        Check(SliceIdentity.Describe([("Z3PLUS", "COM"), ("CPMLDR", "COM"), ("BIOS3", "SPR")]) == "Z3PLUS", "z3plus");
        Check(SliceIdentity.Describe([("DOS65", "COM"), ("ALLOC", "CO6")]) == "DOS/65", "dos65");
        Check(SliceIdentity.Describe([("TURBO", "OVR"), ("HELLO", "PAS")]) == "Turbo Pascal", "turbo");
        Check(SliceIdentity.Describe([("SLR180", "DOC"), ("180FIG", "COM")]) == "SLR Z80", "slr");
        Check(SliceIdentity.Describe([("ZORK1", "COM"), ("HITCH", "COM"), ("SARGON", "COM")]) == "Games", "games");
        Check(SliceIdentity.Describe([("ZFILER", "COM"), ("ARUNZ", "COM")]) == "Z-System", "zsystem");
        Check(SliceIdentity.Describe([("MSX-US", "ROM"), ("AYJOY", "COM")]) == "MSX", "msx");
        Check(SliceIdentity.Describe([("CPMLDR", "COM"), ("BIOS3", "SPR")]) == "CP/M 3", "cpm3");
        Check(SliceIdentity.Describe([("MOVCPM", "COM"), ("PIP", "COM"), ("STAT", "COM")]) == "CP/M 2.2", "cpm22");
        var roms = Enumerable.Range(0, 8).Select(i => ($"GAME{i:00}", "ROM"));
        Check(SliceIdentity.Describe(roms) == "ROM images", "roms " + SliceIdentity.Describe(roms));
        Check(SliceIdentity.Describe([]) == null, "no files");
    }

    private static void RoundTripOnCopy(string slicePath)
    {
        string work = Path.Combine(Path.GetTempPath(), "rc2014-slice0-work.img");
        File.Copy(slicePath, work, overwrite: true);
        byte[] beforeBoot;
        byte[] beforeNzcom;
        using (var device = new FileBlockDevice(work, writable: true))
        using (CpmFileSystem fs = CpmFileSystem.Open(device, 0, false))
        {
            beforeBoot = new byte[CpmFileSystem.BootLength];
            device.Read(0, beforeBoot, 0, beforeBoot.Length);
            beforeNzcom = fs.ReadFile(Find(fs.List(), 0, "NZCOM", "COM"));

            byte[] small = Enumerable.Range(0, 100).Select(i => (byte)i).ToArray();
            byte[] medium = new byte[40 * 1024];
            for (int i = 0; i < medium.Length; i++)
                medium[i] = (byte)(i * 17);
            fs.WriteFile(0, "HELLO", "COM", small);
            fs.WriteFile(0, "BIG", "BIN", medium);
            fs.Save();
        }

        using (var device = new FileBlockDevice(work, writable: true))
        using (CpmFileSystem fs = CpmFileSystem.Open(device, 0, false))
        {
            byte[] boot = new byte[CpmFileSystem.BootLength];
            device.Read(0, boot, 0, boot.Length);
            Check(boot.SequenceEqual(beforeBoot), "system tracks were not rewritten");
            Check(fs.ReadFile(Find(fs.List(), 0, "NZCOM", "COM")).SequenceEqual(beforeNzcom), "existing file bytes unchanged");

            byte[] small = fs.ReadFile(Find(fs.List(), 0, "HELLO", "COM"));
            Check(small.Length == 100 && small[0] == 0 && small[99] == 99, "100 byte file round-trip");
            byte[] medium = fs.ReadFile(Find(fs.List(), 0, "BIG", "BIN"));
            int expected = (1000 * 17) & 0xFF;
            Check(medium.Length == 40 * 1024 && medium[1000] == expected, "40 KB file round-trip");

            fs.DeleteFile(0, "HELLO", "COM");
            fs.DeleteFile(0, "BIG", "BIN");
            fs.Save();
        }

        using (var device = new FileBlockDevice(work, writable: false))
        using (CpmFileSystem fs = CpmFileSystem.Open(device, 0, false))
        {
            Check(fs.List().All(f => !(f.User == 0 && f.Name == "HELLO" && f.Type == "COM")), "HELLO.COM removed");
            Check(fs.List().All(f => !(f.User == 0 && f.Name == "BIG" && f.Type == "BIN")), "BIG.BIN removed");
            Check(fs.ReadFile(Find(fs.List(), 0, "NZCOM", "COM")).SequenceEqual(beforeNzcom), "NZCOM.COM still intact after delete");
        }
    }

    private static void PartitionWindowDoesNotEscape(string slicePath)
    {
        byte[] slice = File.ReadAllBytes(slicePath);
        var image = new byte[RomWbwDisk.PrefixLength + slice.Length + 4096];
        image[510] = 0x55;
        image[511] = 0xAA;
        // Partition entry 0, type 0x2E, start LBA 2048, size two slices (only one fits).
        image[446 + 4] = 0x2E;
        BitConverter.GetBytes(2048).CopyTo(image, 446 + 8);
        BitConverter.GetBytes((int)((RomWbwDisk.SliceLength * 2) / 512)).CopyTo(image, 446 + 12);
        slice.CopyTo(image, (int)RomWbwDisk.PrefixLength);

        var device = new MemoryDevice(image, "synthetic-card.img");
        using RomWbwDisk disk = RomWbwDisk.Open(device);
        Check(disk.Kind == "hd1k", "partitioned image is hd1k");
        Check(disk.Slices.Count == 1, "partial second slice is not offered, got " + disk.Slices.Count);
        using CpmFileSystem fs = disk.OpenSlice(0);
        byte[] nzcom = fs.ReadFile(Find(fs.List(), 0, "NZCOM", "COM"));
        fs.WriteFile(0, "SAFE", "COM", [1, 2, 3, 4]);
        fs.Save();

        Check(device.Writes.Count > 0, "save wrote something");
        foreach ((long offset, int count) in device.Writes)
        {
            Check(offset >= RomWbwDisk.PrefixLength + CpmFileSystem.BootLength, "write starts after the system tracks: " + offset);
            Check(offset + count <= RomWbwDisk.PrefixLength + RomWbwDisk.SliceLength, "write stays inside slice 0: " + (offset + count));
        }
        Check(image.Take(512).SequenceEqual(device.OriginalMbr), "MBR was not modified");
        Check(fs.ReadFile(Find(fs.List(), 0, "NZCOM", "COM")).SequenceEqual(nzcom), "NZCOM.COM unchanged on synthetic card");
    }

    private static void SliceImageStaysInsideItsWindow(string slicePath)
    {
        byte[] slice = File.ReadAllBytes(slicePath);
        Check(slice.Length == RomWbwDisk.SliceLength, "source slice is 8 MiB");
        int media = (int)(RomWbwDisk.PrefixLength + RomWbwDisk.SliceLength * 2);
        var image = new byte[media];
        image[510] = 0x55;
        image[511] = 0xAA;
        image[446 + 4] = 0x2E;
        BitConverter.GetBytes(2048).CopyTo(image, 446 + 8);
        BitConverter.GetBytes((int)((RomWbwDisk.SliceLength * 2) / 512)).CopyTo(image, 446 + 12);
        Array.Fill(image, (byte)0xA5, (int)RomWbwDisk.PrefixLength, (int)RomWbwDisk.SliceLength);
        Array.Fill(image, (byte)0x5A, (int)(RomWbwDisk.PrefixLength + RomWbwDisk.SliceLength), (int)RomWbwDisk.SliceLength);
        byte[] before = image.ToArray();

        var card = new MemoryDevice(image, "synthetic-card.img");
        var sourceDevice = new MemoryDevice(slice, "new-slice.img");
        using RomWbwDisk destination = RomWbwDisk.Open(card);
        using RomWbwDisk source = RomWbwDisk.Open(sourceDevice);
        Check(destination.Slices.Count == 2, "two full slices");
        destination.ReplaceSlice(1, source, 0);

        Check(image.AsSpan(0, (int)RomWbwDisk.PrefixLength).SequenceEqual(before.AsSpan(0, (int)RomWbwDisk.PrefixLength)), "prefix and partition table were not written");
        Check(image.AsSpan((int)RomWbwDisk.PrefixLength, (int)RomWbwDisk.SliceLength).SequenceEqual(before.AsSpan((int)RomWbwDisk.PrefixLength, (int)RomWbwDisk.SliceLength)), "slice 0 was not written");
        int slice1 = (int)(RomWbwDisk.PrefixLength + RomWbwDisk.SliceLength);
        Check(image.AsSpan(slice1, (int)RomWbwDisk.SliceLength).SequenceEqual(slice), "slice 1 is the new disk");
        foreach ((long offset, int count) in card.Writes)
        {
            Check(offset >= slice1, "write starts at slice 1: " + offset);
            Check(offset + count <= slice1 + RomWbwDisk.SliceLength, "write ends inside slice 1: " + (offset + count));
        }
    }

    private static void ClearingOneSliceLeavesTheOthers(string slicePath)
    {
        byte[] slice = File.ReadAllBytes(slicePath);
        int media = (int)(RomWbwDisk.PrefixLength + RomWbwDisk.SliceLength * 2);
        var image = new byte[media];
        image[510] = 0x55;
        image[511] = 0xAA;
        image[446 + 4] = 0x2E;
        BitConverter.GetBytes(2048).CopyTo(image, 446 + 8);
        BitConverter.GetBytes((int)((RomWbwDisk.SliceLength * 2) / 512)).CopyTo(image, 446 + 12);
        slice.CopyTo(image, (int)RomWbwDisk.PrefixLength);
        Array.Fill(image, (byte)0x11, (int)(RomWbwDisk.PrefixLength + RomWbwDisk.SliceLength), (int)RomWbwDisk.SliceLength);
        byte[] before = image.ToArray();

        var card = new MemoryDevice(image, "synthetic-card.img");
        using RomWbwDisk disk = RomWbwDisk.Open(card);
        disk.ClearSlice(1);

        int slice1 = (int)(RomWbwDisk.PrefixLength + RomWbwDisk.SliceLength);
        Check(image.AsSpan(0, slice1).SequenceEqual(before.AsSpan(0, slice1)), "clearing slice 1 left the prefix and slice 0 alone");
        Check(image.Skip(slice1).All(b => b == 0xE5), "slice 1 was erased");
        foreach ((long offset, int count) in card.Writes)
        {
            Check(offset >= slice1, "erase starts at slice 1: " + offset);
            Check(offset + count <= slice1 + RomWbwDisk.SliceLength, "erase ends inside slice 1: " + (offset + count));
        }

        using RomWbwDisk reread = RomWbwDisk.Open(card);
        Check(reread.Slices[0].Label == "NZ-COM", "slice 0 still reads as NZ-COM");
        Check(reread.Slices[1].Caption == "01    unused", "erased slice is unused, got " + reread.Slices[1].Caption);
        using CpmFileSystem cleared = reread.OpenSlice(1);
        Check(cleared.List().Count == 0, "erased slice has no files");
    }

    private static void ExtractedSliceIsARomWbwImage(string slicePath)
    {
        byte[] slice = File.ReadAllBytes(slicePath);
        int media = (int)(RomWbwDisk.PrefixLength + RomWbwDisk.SliceLength * 2);
        var image = new byte[media];
        image[510] = 0x55;
        image[511] = 0xAA;
        image[446 + 4] = 0x2E;
        BitConverter.GetBytes(2048).CopyTo(image, 446 + 8);
        BitConverter.GetBytes((int)((RomWbwDisk.SliceLength * 2) / 512)).CopyTo(image, 446 + 12);
        slice.CopyTo(image, (int)RomWbwDisk.PrefixLength);
        Array.Fill(image, (byte)0x11, (int)(RomWbwDisk.PrefixLength + RomWbwDisk.SliceLength), (int)RomWbwDisk.SliceLength);
        byte[] before = image.ToArray();

        string saved = Path.Combine(Path.GetTempPath(), "rc2014-extracted-slice.img");
        var card = new MemoryDevice(image, "synthetic-card.img");
        using (RomWbwDisk disk = RomWbwDisk.Open(card))
            disk.SaveSlice(0, saved);

        try
        {
            byte[] extracted = File.ReadAllBytes(saved);
            Check(extracted.Length == RomWbwDisk.SliceLength, "extracted image is 8 MiB");
            Check(extracted.AsSpan().SequenceEqual(slice), "extracted image is slice 0");
            Check(card.Writes.Count == 0, "extracting a slice does not write the card");
            Check(image.AsSpan().SequenceEqual(before), "card bytes were not modified");
            using var device = new FileBlockDevice(saved, writable: false);
            using RomWbwDisk opened = RomWbwDisk.Open(device);
            Check(opened.Kind == "slice", "extracted image opens as one RomWBW slice");
            Check(opened.Slices[0].Label == "NZ-COM", "extracted image keeps the slice label");
            using CpmFileSystem fs = opened.OpenSlice(0);
            Check(fs.List().Count == 362, "extracted image has the slice files, got " + fs.List().Count);
        }
        finally
        {
            File.Delete(saved);
        }
    }

    private static void DateStampSlotsStayReserved()
    {
        var blank = new byte[RomWbwDisk.SliceLength];
        var device = new MemoryDevice(blank, "blank.img");
        var window = new WindowDevice(device, 0, blank.Length, "blank");
        using (CpmFileSystem fs = CpmFileSystem.Open(window, 0, false))
        {
            fs.InitializeDirectory();
            fs.Save();
        }

        blank[CpmFileSystem.BootLength + 3 * CpmFileSystem.EntryLength] = 0x21;
        using (CpmFileSystem fs = CpmFileSystem.Open(new WindowDevice(device, 0, blank.Length, "blank"), 0, false))
        {
            fs.WriteFile(0, "STAMP", "COM", [9, 8, 7]);
            fs.Save();
        }

        Check(blank[CpmFileSystem.BootLength + 3 * CpmFileSystem.EntryLength] == 0x21, "date-stamp slot was not reused");
        using CpmFileSystem check = CpmFileSystem.Open(new WindowDevice(device, 0, blank.Length, "blank"), 0, false);
        CpmFile file = Find(check.List(), 0, "STAMP", "COM");
        Check(check.ReadFile(file).SequenceEqual(new byte[] { 9, 8, 7 }), "file written beside the stamp");
    }

    private static CpmFile Find(IReadOnlyList<CpmFile> files, int user, string name, string type)
    {
        CpmFile? file = files.FirstOrDefault(f => f.User == user && f.Name == name && f.Type == type);
        if (file == null)
            throw new InvalidOperationException($"Missing {user}:{name}.{type}");
        return file;
    }

    private static void Check(bool condition, string message)
    {
        if (condition)
        {
            Console.WriteLine("  ok  " + message);
            return;
        }

        _failed++;
        Console.WriteLine("FAIL  " + message);
    }

    private sealed class MemoryDevice : IBlockDevice
    {
        private readonly byte[] _data;

        public MemoryDevice(byte[] data, string name)
        {
            _data = data;
            DisplayName = name;
            OriginalMbr = data.Take(512).ToArray();
        }

        public List<(long Offset, int Count)> Writes { get; } = [];

        public byte[] OriginalMbr { get; }

        public long Length => _data.Length;

        public bool CanWrite => true;

        public bool IsPhysical => false;

        public string DisplayName { get; }

        public void Read(long offset, byte[] buffer, int index, int count)
        {
            Buffer.BlockCopy(_data, (int)offset, buffer, index, count);
        }

        public void Write(long offset, byte[] buffer, int index, int count)
        {
            Writes.Add((offset, count));
            Buffer.BlockCopy(buffer, index, _data, (int)offset, count);
        }

        public void Dispose()
        {
        }
    }
}
