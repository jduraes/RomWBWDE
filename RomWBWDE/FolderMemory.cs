namespace RomWBWDE.Explorer;

/// <summary>
/// Remembers the last Windows directory opened in a pane.
/// A missing directory is ignored and the home directory is used instead.
/// </summary>
internal static class FolderMemory
{
    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RomWBWDE",
        "last-folder.txt");

    public static string Recall()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        try
        {
            if (!File.Exists(FilePath))
                return home;
            string path = File.ReadAllText(FilePath).Trim();
            if (path.Length > 0 && Directory.Exists(path))
                return path;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
        }

        return home;
    }

    public static void Remember(string path)
    {
        try
        {
            string full = Path.GetFullPath(path);
            if (!Directory.Exists(full))
                return;
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, full);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
        }
    }
}
