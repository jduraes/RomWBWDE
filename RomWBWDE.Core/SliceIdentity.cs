namespace RomWBWDE;

/// <summary>
/// Names a slice from every file in its directory. Each kind of disk has its
/// own markers, and the kind with the strongest set wins. A file that is on
/// most RomWBW slices, such as MBASIC or a PT3 demo, cannot name the slice on
/// its own.
/// </summary>
public static class SliceIdentity
{
    private static readonly string[] Preference =
    [
        "NZ-COM", "ZPM3", "Z3PLUS", "QP/M", "DOS/65", "Turbo Pascal", "SLR Z80",
        "WordStar", "dBASE", "MBASIC", "Fortran", "Cowgol", "Hi-Tech C", "Aztec C",
        "COBOL", "BPBIOS", "Infocom", "Games", "Z-System", "MSX", "ROM images",
        "Music", "CP/M 3", "ZSDOS", "CP/M 2.2", "CP/M"
    ];

    public static string? Describe(IEnumerable<(string Name, string Type)> files)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        var typeCount = new Dictionary<string, int>(StringComparer.Ordinal);
        int count = 0;
        foreach ((string name, string type) in files)
        {
            count++;
            names.Add(name);
            typeCount.TryGetValue(type, out int n);
            typeCount[type] = n + 1;
        }

        if (count == 0)
            return null;

        int OfType(string type) => typeCount.TryGetValue(type, out int n) ? n : 0;
        int Hit(params string[] markers)
        {
            int found = 0;
            foreach (string marker in markers)
            {
                if (names.Contains(marker))
                    found++;
            }
            return found;
        }

        var scores = new Dictionary<string, int>(StringComparer.Ordinal);
        void Add(string label, int points)
        {
            if (points <= 0)
                return;
            scores.TryGetValue(label, out int soFar);
            scores[label] = soFar + points;
        }

        Add("NZ-COM", Hit("NZCOM", "!NZ-COM", "NZBLITZ", "MKZCM") * 5);
        Add("ZPM3", Hit("ZPM3", "STARTZPM", "ZCCP", "ZPMLDR", "MAKEDOS") * 5);
        Add("Z3PLUS", Hit("Z3PLUS", "!Z3PLUS", "STARTZ3P") * 5);
        Add("QP/M", Hit("QPM", "QPIP", "QINSTALL", "QBACKUP") * 5);
        Add("DOS/65", Hit("DOS65") * 5 + (OfType("CO6") >= 3 ? Math.Min(OfType("CO6"), 6) : 0));
        Add("Turbo Pascal", Hit("TINST", "TURBOMSG") * 5 + Hit("TURBO") + (OfType("PAS") >= 3 ? 3 : 0));
        Add("SLR Z80", Hit("SLR180", "180FIG", "Z80ASM", "LNKFIG") * 5);
        Add("WordStar", Hit("WS", "WSMSGS", "WSOVLY1", "MAILMRGE", "MAINDICT") * 3);
        Add("dBASE", Hit("DBASE") * 5);
        Add("MBASIC", Hit("BASCOM", "BRUN", "OBSLIB", "BASLIB") * 5 + Hit("MBASIC"));
        Add("Fortran", Hit("F80", "FORLIB") * 5);
        Add("Cowgol", Hit("COWBE") * 5 + (OfType("COW") >= 3 ? Math.Min(OfType("COW"), 6) : 0));
        Add("Hi-Tech C", Hit("C309", "HITECH") * 5);
        Add("Aztec C", Hit("CC", "CNM", "CZ") * 5);
        Add("COBOL", Hit("COBOL") * 5);
        Add("BPBIOS", Hit("BPBIO") * 5);
        Add("Z-System", Hit("ZFILER", "ARUNZ", "SALIAS"));
        Add("CP/M 3", Hit("CPMLDR", "BIOS3", "BNKBIOS3", "BNKBDOS3", "CPM3") * 2);
        Add("ZSDOS", Hit("ZSYS", "ZSCONFIG", "ZSVSTAMP", "SETUPZST") * 3);
        Add("CP/M 2.2", Hit("CPM") * 5 + Hit("MOVCPM", "PIP", "STAT", "SYSGEN", "SUBMIT", "ED", "DDT", "ASM"));

        int stories = OfType("Z3") + OfType("Z4") + OfType("Z5") + OfType("Z6") + OfType("Z8");
        if (stories >= 4)
            Add("Infocom", stories);
        Add("Games", Hit("ZORK1", "ZORK2", "ZORK3", "HITCH", "SARGON", "DUNGEON", "CASTLE") * 2);

        bool msx = false;
        foreach (string name in names)
        {
            if (name.StartsWith("MSX", StringComparison.Ordinal))
            {
                msx = true;
                break;
            }
        }
        if (msx)
            Add("MSX", 4);

        int roms = OfType("ROM");
        if (roms >= 8 && roms * 2 >= count)
            Add("ROM images", roms);

        int tunes = OfType("PT3") + OfType("MYM");
        if (tunes >= 8 && tunes * 2 >= count)
            Add("Music", tunes);

        string? best = null;
        int bestScore = 0;
        foreach (string label in Preference)
        {
            if (scores.TryGetValue(label, out int score) && score > bestScore)
            {
                best = label;
                bestScore = score;
            }
        }

        return best ?? "CP/M";
    }
}
