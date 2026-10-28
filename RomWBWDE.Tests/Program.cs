using System.Text;
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
        MinEzImageReadsHello();
        MinEzKeepsTheThirdDirectoryBlock();
        MinEzPartitionLeavesTheFatArea();
        VolumeLabelBeatsTheFileGuess(slicePath);
        Hd512UsesItsOwnSlice(slicePath);
        RecognizesClearImages(slicePath);
        RenameTouchesOnlyTheName(slicePath);
        RejectsASliceTheDiskDidNotKeep(slicePath);
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

        var ordinary = new List<(string Name, string Type)>
        {
            ("MBASIC", "COM"), ("PIP", "COM"), ("STAT", "COM"), ("SYSGEN", "COM"), ("CPM", "SYS")
        };
        ordinary.AddRange(Enumerable.Range(0, 18).Select(i => ($"TUNE{i:00}", "PT3")));
        ordinary.AddRange(Enumerable.Range(0, 40).Select(i => ($"FILE{i:00}", "COM")));
        Check(SliceIdentity.Describe(ordinary) == "CP/M 2.2", "mbasic and music demos stay CP/M 2.2, got " + SliceIdentity.Describe(ordinary));
        Check(SliceIdentity.Describe([("BASCOM", "COM"), ("BRUN", "COM"), ("MBASIC", "COM")]) == "MBASIC", "basic compiler");
        Check(SliceIdentity.Describe([("ZSYS", "SYS"), ("ZSCONFIG", "COM"), ("PIP", "COM"), ("STAT", "COM"), ("MBASIC", "COM")]) == "ZSDOS", "zsdos");
        Check(SliceIdentity.Describe([("NZCOM", "COM"), ("ZSYS", "SYS"), ("MBASIC", "COM"), ("PIP", "COM")]) == "NZ-COM", "nz-com over zsdos");
        var tunes = Enumerable.Range(0, 10).Select(i => ($"TUNE{i:00}", "PT3")).Append(("NOTES", "TXT"));
        Check(SliceIdentity.Describe(tunes) == "Music", "music disk " + SliceIdentity.Describe(tunes));
        CatalogGuesses();
    }

    private static void CatalogGuesses()
    {
        string directory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "RomWBW", "Source", "Images"));
        if (!Directory.Exists(directory))
            directory = @"C:\Users\miguel\Documents\development\RomWBW\Source\Images";
        if (!Directory.Exists(directory))
            return;

        var expected = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["cpm22"] = "CP/M 2.2",
            ["zsdos"] = "ZSDOS",
            ["nzcom"] = "NZ-COM",
            ["cpm3"] = "CP/M 3",
            ["zpm3"] = "ZPM3",
            ["z3plus"] = "Z3PLUS",
            ["wp"] = "WordStar",
            ["games"] = "Games",
            ["tpascal"] = "Turbo Pascal",
            ["dos65"] = "DOS/65",
            ["z80asm"] = "SLR Z80",
            ["qpm"] = "QP/M",
            ["bascomp"] = "MBASIC",
            ["msxroms1"] = "ROM images",
            ["msxroms2"] = "ROM images",
            ["fortran"] = "Fortran",
            ["cowgol"] = "Cowgol",
            ["cobol"] = "COBOL",
            ["hitechc"] = "Hi-Tech C",
            ["aztecc"] = "Aztec C",
            ["bp"] = "BPBIOS",
            ["infocom"] = "Infocom",
            ["blank"] = null
        };

        foreach ((string disk, string? want) in expected)
        {
            string path = Path.Combine(directory, "hd1k_" + disk + ".cat");
            if (!File.Exists(path))
                continue;
            var files = new List<(string Name, string Type)>();
            foreach (string line in File.ReadLines(path))
            {
                if (line.Length < 12 || line[8] != '.')
                    continue;
                string name = line[..8].Trim();
                string type = line.Substring(9, 3).Trim();
                if (name.Length == 0)
                    continue;
                files.Add((name, type));
            }
            string? got = SliceIdentity.Describe(files);
            Check(got == want, disk + " guess " + (got ?? "(none)"));
        }
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

    private static void MinEzImageReadsHello()
    {
        string path = Path.Combine(Path.GetTempPath(), "minez-cpm.img");
        Check(File.Exists(path), "min-ez image is present");
        if (!File.Exists(path))
            return;

        using var device = new FileBlockDevice(path, writable: false);
        using RomWbwDisk disk = RomWbwDisk.Open(device);
        Check(RomWbwDisk.IsClearImage(device), "raw min-ez image is recognized");
        Check(disk.Kind == "minez", "raw min-ez image kind " + disk.Kind);
        Check(disk.Slices.Count == 1, "raw min-ez image is one partition");
        Check(disk.Slices[0].Caption == "00    Min-eZ*", "min-ez caption " + disk.Slices[0].Caption);
        using CpmFileSystem fs = disk.OpenSlice(0);
        CpmFile hello = Find(fs.List(), 0, "HELLO", "TXT");
        Check(hello.Size == 256, "HELLO.TXT size " + hello.Size);
        byte[] body = fs.ReadFile(hello);
        string text = System.Text.Encoding.ASCII.GetString(body);
        Check(text.StartsWith("Hello from the Min-eZ UART1 XMODEM workflow!"), "HELLO.TXT text");
    }

    private static void MinEzKeepsTheThirdDirectoryBlock()
    {
        string path = Path.Combine(Path.GetTempPath(), "minez-cpm.img");
        if (!File.Exists(path))
            return;

        byte[] image = File.ReadAllBytes(path);
        Check(image[16] == 3 && image[17] == 0, "HELLO.TXT is stored in block 3");
        Check(image.Skip(16384).Take(8192).All(value => value == 0xE5), "entries 512-767 are an empty directory block");

        int at = 600 * 32;
        Array.Clear(image, at, 32);
        Encoding.ASCII.GetBytes("LATE    TXT").CopyTo(image, at + 1);
        image[at] = 0;
        image[at + 13] = 8;
        image[at + 15] = 1;
        image[at + 16] = 4;
        byte[] payload = Encoding.ASCII.GetBytes("late-dir");
        payload.CopyTo(image, 4 * 8192);
        byte[] third = image.Skip(16384).Take(8192).ToArray();

        var card = new MemoryDevice(image, "minez.img");
        using (RomWbwDisk disk = RomWbwDisk.Open(card))
        using (CpmFileSystem fs = disk.OpenSlice(0))
        {
            byte[] late = fs.ReadFile(Find(fs.List(), 0, "LATE", "TXT"));
            Check(late.AsSpan(0, payload.Length).SequenceEqual(payload), "a file past entry 511 is readable");
            fs.WriteFile(0, "NEW", "BIN", new byte[] { 1, 2, 3, 4 });
            fs.Save();
            Check(fs.ReadFile(Find(fs.List(), 0, "LATE", "TXT")).AsSpan(0, payload.Length).SequenceEqual(payload),
                "the late directory entry survives a new file");
        }

        Check(image.AsSpan(16384, 8192).SequenceEqual(third), "a new file does not use the third directory block");
    }

    private static void MinEzPartitionLeavesTheFatArea()
    {
        string path = Path.Combine(Path.GetTempPath(), "minez-cpm.img");
        if (!File.Exists(path))
            return;

        byte[] cpm = File.ReadAllBytes(path);
        int prefix = (int)RomWbwDisk.PrefixLength;
        var image = new byte[prefix + cpm.Length];
        Array.Fill(image, (byte)0xA5);
        image[510] = 0x55;
        image[511] = 0xAA;
        image[446 + 4] = 0x0C;
        BitConverter.GetBytes(1).CopyTo(image, 446 + 8);
        BitConverter.GetBytes(prefix / 512 - 1).CopyTo(image, 446 + 12);
        image[462 + 4] = RomWbwDisk.MinEzPartitionType;
        BitConverter.GetBytes(prefix / 512).CopyTo(image, 462 + 8);
        BitConverter.GetBytes(cpm.Length / 512).CopyTo(image, 462 + 12);
        cpm.CopyTo(image, prefix);
        byte[] before = (byte[])image.Clone();

        var card = new MemoryDevice(image, "minez-card.img");
        using (RomWbwDisk disk = RomWbwDisk.Open(card))
        {
            Check(RomWbwDisk.IsClearImage(card), "partitioned min-ez image is recognized");
            Check(disk.Kind == "minez", "partitioned card is min-ez");
            Check(disk.Slices.Count == 1, "fat partition is not offered, got " + disk.Slices.Count);
            using CpmFileSystem fs = disk.OpenSlice(0);
            byte[] hello = fs.ReadFile(Find(fs.List(), 0, "HELLO", "TXT"));
            byte[] wide = new byte[70 * 1024];
            for (int i = 0; i < wide.Length; i++)
                wide[i] = (byte)(i * 17);
            fs.WriteFile(0, "WIDE", "BIN", wide);
            fs.Save();
            Check(fs.ReadFile(Find(fs.List(), 0, "WIDE", "BIN")).SequenceEqual(wide), "70 KB file round-trip");
            Check(fs.ReadFile(Find(fs.List(), 0, "HELLO", "TXT")).SequenceEqual(hello), "HELLO.TXT unchanged after write");
        }

        Check(image.AsSpan(0, prefix).SequenceEqual(before.AsSpan(0, prefix)), "FAT area and partition table were not written");
        Check(image.AsSpan(prefix + 16384, 8192).SequenceEqual(before.AsSpan(prefix + 16384, 8192)),
            "the third directory block was not allocated as file data");
        foreach ((long offset, int count) in card.Writes)
        {
            Check(offset >= prefix, "min-ez write starts in the CP/M partition: " + offset);
            Check(offset + count <= prefix + cpm.Length, "min-ez write stays in the CP/M partition: " + (offset + count));
        }
    }

    private static void VolumeLabelBeatsTheFileGuess(string slicePath)
    {
        byte[] image = File.ReadAllBytes(slicePath);
        StampVolume(image, "CP/M 2.2");
        using RomWbwDisk disk = RomWbwDisk.Open(new MemoryDevice(image, "labelled"));
        Check(disk.Kind == "slice", "labelled image stays one hd1k slice");
        Check(disk.Slices[0].Label == "CP/M 2.2", "volume label wins, got " + disk.Slices[0].Label);
        Check(disk.Slices[0].VolumeLabel == "CP/M 2.2", "stored volume label");
        using CpmFileSystem fs = disk.OpenSlice(0);
        Check(fs.List().Count == 362, "label change does not hide files");

        StampVolume(image, "Unlabeled");
        using RomWbwDisk plain = RomWbwDisk.Open(new MemoryDevice(image, "plain"));
        Check(plain.Slices[0].VolumeLabel == null, "Unlabeled is not shown");
        Check(plain.Slices[0].Label == "NZ-COM", "file guess returns, got " + plain.Slices[0].Label);

        StampVolume(image, "Turbo Pascal", signature: false);
        using RomWbwDisk unsigned = RomWbwDisk.Open(new MemoryDevice(image, "unsigned"));
        Check(unsigned.Slices[0].VolumeLabel == "Turbo Pascal", "name without the boot signature, got " + unsigned.Slices[0].VolumeLabel);

        for (int i = 0; i < SliceVolume.LabelLength; i++)
            image[SliceVolume.LabelOffset + i] = 0xE5;
        using RomWbwDisk erased = RomWbwDisk.Open(new MemoryDevice(image, "erased-name"));
        Check(erased.Slices[0].VolumeLabel == null, "an empty name is not a label");
        Check(erased.Slices[0].Label == "NZ-COM", "file guess used when the name is absent, got " + erased.Slices[0].Label);
    }

    private static void Hd512UsesItsOwnSlice(string slicePath)
    {
        int sliceBytes = (int)RomWbwDisk.Hd512SliceLength;
        byte[] image = new byte[sliceBytes * 2];
        Array.Fill(image, (byte)0xE5);
        byte[] source = File.ReadAllBytes(slicePath);
        int copied = source.Length - CpmLayout.Hd1k.BootLength;
        Buffer.BlockCopy(source, CpmLayout.Hd1k.BootLength, image, CpmLayout.Hd512.BootLength, copied);
        StampVolume(image, "CP/M 2.2");

        var device = new MemoryDevice(image, "hd512");
        using RomWbwDisk disk = RomWbwDisk.Open(device);
        Check(RomWbwDisk.IsClearImage(device), "hd512 image is recognized");
        Check(disk.Kind == "hd512", "classic image kind " + disk.Kind);
        Check(disk.Slices.Count == 2, "two hd512 slices, got " + disk.Slices.Count);
        Check(disk.Slices[0].Length == sliceBytes, "hd512 slice length");
        Check(disk.Slices[0].Label == "CP/M 2.2", "hd512 volume label " + disk.Slices[0].Label);
        Check(disk.Slices[1].Caption.Contains("unused"), "second hd512 slice " + disk.Slices[1].Caption);

        using CpmFileSystem fs = disk.OpenSlice(0);
        IReadOnlyList<CpmFile> files = fs.List();
        Check(files.Count > 0, "hd512 file count " + files.Count);
        CpmFile nzcom = Find(files, 0, "NZCOM", "COM");
        byte[] body = fs.ReadFile(nzcom);
        Check(body.Length == 10624 && body[0] == 0xC3, "hd512 reads NZCOM.COM");

        fs.WriteFile(0, "NEW", "TXT", Encoding.ASCII.GetBytes("hd512"));
        fs.Save();
        Check(device.Writes.Count > 0 && device.Writes.All(write =>
                write.Offset >= CpmLayout.Hd512.BootLength && write.Offset + write.Count <= sliceBytes),
            "hd512 writes stay inside slice 0, after the system tracks");
        Check(image.Skip(sliceBytes).All(value => value == 0xE5), "the other hd512 slice is untouched");
    }

    private static void RenameTouchesOnlyTheName(string slicePath)
    {
        byte[] image = File.ReadAllBytes(slicePath);
        byte[] original = (byte[])image.Clone();
        for (int i = 0; i < SliceVolume.LabelLength; i++)
            image[SliceVolume.LabelOffset + i] = 0xE5;
        image[1527] = 0xE5;
        var device = new MemoryDevice(image, "rename");
        using (RomWbwDisk disk = RomWbwDisk.Open(device))
        {
            Check(disk.Slices[0].Caption == "00    NZ-COM*", "guessed caption " + disk.Slices[0].Caption);
            disk.RenameSlice(0, "  Turbo Pascal  ");
        }

        Check(device.Writes.Count == 1 && device.Writes[0].Offset == 1024 && device.Writes[0].Count == 512,
            "rename writes the third sector only");
        int outside = 0;
        int changed = 0;
        for (int i = 0; i < image.Length; i++)
        {
            if (image[i] == original[i])
                continue;
            changed++;
            bool inName = i >= SliceVolume.LabelOffset && i < SliceVolume.LabelOffset + SliceVolume.LabelLength;
            if (!inName && i != 1527)
                outside++;
        }
        Check(changed > 0 && outside == 0, "only the name bytes changed, outside=" + outside);

        using (RomWbwDisk renamed = RomWbwDisk.Open(device))
        {
            Check(renamed.Slices[0].VolumeLabel == "Turbo Pascal", "renamed volume " + renamed.Slices[0].VolumeLabel);
            Check(renamed.Slices[0].Caption == "00    Turbo Pascal", "renamed caption " + renamed.Slices[0].Caption);
            using CpmFileSystem fs = renamed.OpenSlice(0);
            Check(fs.List().Count == 362, "rename leaves the files");
        }

        bool rejected = false;
        try
        {
            using RomWbwDisk disk = RomWbwDisk.Open(device);
            disk.RenameSlice(0, "this name is far too long");
        }
        catch (IOException)
        {
            rejected = true;
        }
        Check(rejected, "a name longer than 16 characters is refused");

        string minezPath = Path.Combine(Path.GetTempPath(), "minez-cpm.img");
        if (File.Exists(minezPath))
        {
            using var minezDevice = new FileBlockDevice(minezPath, writable: false);
            using RomWbwDisk minez = RomWbwDisk.Open(minezDevice);
            bool blocked = false;
            try
            {
                minez.RenameSlice(0, "Nope");
            }
            catch (IOException ex)
            {
                blocked = ex.Message.Contains("no slice name", StringComparison.Ordinal);
            }
            Check(blocked, "a Min-eZ partition cannot be renamed");
        }
    }

    private static void RecognizesClearImages(string slicePath)
    {
        using (var slice = new FileBlockDevice(slicePath, writable: false))
            Check(RomWbwDisk.IsClearImage(slice), "hd1k slice image is recognized");

        byte[] zeros = new byte[(int)RomWbwDisk.SliceLength];
        Check(!RomWbwDisk.IsClearImage(new MemoryDevice(zeros, "zeros")), "a zero-filled file is not an image");
        byte[] empty = new byte[(int)RomWbwDisk.SliceLength];
        Array.Fill(empty, (byte)0xE5);
        Check(RomWbwDisk.IsClearImage(new MemoryDevice(empty, "empty")), "an empty slice is recognized");

        byte[] shortFile = new byte[1440 * 1024];
        Check(!RomWbwDisk.IsClearImage(new MemoryDevice(shortFile, "floppy")), "a floppy image is not a slice");

        int media = (int)(RomWbwDisk.PrefixLength + RomWbwDisk.SliceLength * 2);
        byte[] disk = new byte[media];
        disk[510] = 0x55;
        disk[511] = 0xAA;
        disk[446 + 4] = 0x2E;
        BitConverter.GetBytes(2048).CopyTo(disk, 446 + 8);
        BitConverter.GetBytes(media / 512 - 2048).CopyTo(disk, 446 + 12);
        Check(RomWbwDisk.IsClearImage(new MemoryDevice(disk, "hd1k-disk")), "an exact hd1k disk is recognized");

        byte[] ragged = new byte[media + 4096];
        disk.CopyTo(ragged, 0);
        Check(!RomWbwDisk.IsClearImage(new MemoryDevice(ragged, "ragged")), "a disk with a partial slice is not recognized");

        string binary = @"C:\Users\miguel\Documents\development\RomWBW\Binary";
        if (!Directory.Exists(binary))
            return;

        foreach (string file in Directory.EnumerateFiles(binary))
        {
            string name = Path.GetFileName(file);
            bool want = name.StartsWith("hd1k_", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("hd512_", StringComparison.OrdinalIgnoreCase);
            if (new FileInfo(file).Length < RomWbwDisk.SliceLength)
                want = false;
            if (name.StartsWith("fd144_", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
                want = false;
            using var image = new FileBlockDevice(file, writable: false);
            bool clear = RomWbwDisk.IsClearImage(image);
            if (name is "psys.img" or "psys_fp.img")
                want = true;
            if (want || name.StartsWith("fd144_", StringComparison.OrdinalIgnoreCase) || name is "GPL-3.0.txt" or "hd1k_prefix.dat" or "hd512_combo.img")
                Check(clear == want, name + (clear ? " blue" : " plain"));
        }
    }

    private static void RejectsASliceTheDiskDidNotKeep(string slicePath)
    {
        byte[] blank = new byte[(int)RomWbwDisk.SliceLength];
        Array.Fill(blank, (byte)0xE5);
        var destination = new MemoryDevice(blank, "drop", keepWrites: false);
        using var sourceDevice = new FileBlockDevice(slicePath, writable: false);
        using RomWbwDisk source = RomWbwDisk.Open(sourceDevice);
        using RomWbwDisk disk = RomWbwDisk.Open(destination);
        bool rejected = false;
        try
        {
            disk.ReplaceSlice(0, source, 0);
        }
        catch (IOException ex)
        {
            rejected = ex.Message.Contains("did not keep", StringComparison.Ordinal);
        }
        Check(rejected, "a slice the disk did not keep is refused");
        Check(blank[0] == 0xE5 && blank[16384] == 0xE5, "the blank slice was not treated as written");
    }

    private static void StampVolume(byte[] image, string label, bool signature = true)
    {
        image[SliceVolume.SignatureOffset] = signature ? (byte)0x5A : (byte)0;
        image[SliceVolume.SignatureOffset + 1] = signature ? (byte)0xA5 : (byte)0;
        for (int i = 0; i < SliceVolume.LabelLength; i++)
            image[SliceVolume.LabelOffset + i] = (byte)'$';
        byte[] text = Encoding.ASCII.GetBytes(label);
        Buffer.BlockCopy(text, 0, image, SliceVolume.LabelOffset, text.Length);
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

        public MemoryDevice(byte[] data, string name, bool keepWrites = true)
        {
            _data = data;
            DisplayName = name;
            KeepWrites = keepWrites;
            OriginalMbr = data.Take(512).ToArray();
        }

        public bool KeepWrites { get; }

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
            if (KeepWrites)
                Buffer.BlockCopy(buffer, index, _data, (int)offset, count);
        }

        public void Dispose()
        {
        }
    }
}
