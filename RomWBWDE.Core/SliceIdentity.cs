namespace RomWBWDE;

/// <summary>
/// Names a slice from the files in its directory. The first matching system wins,
/// so a more specific disk (NZ-COM, Z3PLUS) is not labelled as plain CP/M.
/// </summary>
public static class SliceIdentity
{
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

        bool Has(string name) => names.Contains(name);
        int OfType(string type) => typeCount.TryGetValue(type, out int n) ? n : 0;
        bool Starts(string prefix) 
        {
            foreach (string name in names)
            {
                if (name.StartsWith(prefix, StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        if (Has("NZCOM") || Has("!NZ-COM"))
            return "NZ-COM";
        if (Has("ZPM3") || Has("STARTZPM"))
            return "ZPM3";
        if (Has("Z3PLUS") || Has("!Z3PLUS") || Has("STARTZ3P"))
            return "Z3PLUS";
        if (Has("DOS65") || OfType("CO6") >= 3)
            return "DOS/65";
        if (Has("TURBO") || Has("TINST") || OfType("PAS") >= 3)
            return "Turbo Pascal";
        if (Has("SLR180") || Has("180FIG"))
            return "SLR Z80";
        if (Has("ZORK1") || Has("ZORK2") || Has("ZORK3") || Has("HITCH") || Has("SARGON"))
            return "Games";
        if (Has("ZFILER") || Has("ARUNZ"))
            return "Z-System";
        if (Starts("MSX"))
            return "MSX";
        if (OfType("ROM") >= 8)
            return "ROM images";
        if (Has("CPMLDR") || Has("BIOS3") || Has("BNKBIOS3"))
            return "CP/M 3";
        if (Has("WS") && (Has("WSMSGS") || Has("WSOVLY1")))
            return "WordStar";
        if (Has("DBASE"))
            return "dBASE";
        if (Has("QPM"))
            return "QP/M";
        if (Has("MBASIC"))
            return "MBASIC";
        if (OfType("PT3") >= 8 || OfType("MYM") >= 4)
            return "Music";
        if (Has("ZSDOS"))
            return "ZSDOS";
        if (Has("MOVCPM") || Has("SYSGEN") || (Has("PIP") && Has("STAT")))
            return "CP/M 2.2";
        return "CP/M";
    }
}
