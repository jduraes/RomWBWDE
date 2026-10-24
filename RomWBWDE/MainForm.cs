using RomWBWDE;

namespace RomWBWDE.Explorer;

internal sealed class MainForm : Form
{
    private readonly BrowserPanel _left = new() { Dock = DockStyle.Fill };
    private readonly BrowserPanel _right = new() { Dock = DockStyle.Fill };
    private readonly Label _status = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(8, 0, 0, 0) };
    private BrowserPanel _active;

    public MainForm()
    {
        Text = "RomWBW Disk Explorer";
        MinimumSize = new Size(1080, 680);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.White;
        ForeColor = Color.Black;
        _active = _left;

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            SplitterWidth = 6
        };
        split.Panel1.Controls.Add(_left);
        split.Panel2.Controls.Add(_right);

        var bar = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 24,
            BackColor = Color.FromArgb(245, 245, 245)
        };
        _status.Dock = DockStyle.Fill;
        _status.TextAlign = ContentAlignment.MiddleLeft;
        _status.AutoEllipsis = true;
        _status.Padding = new Padding(8, 0, 4, 0);
        _status.ForeColor = Color.Black;
        _status.BackColor = bar.BackColor;
        bar.Controls.Add(_status);

        Controls.Add(split);
        Controls.Add(bar);

        _left.PlaceCommands(onTheLeft: false,
            ("Delete", (_, _) => DeleteFrom(_left)),
            ("Copy →", (_, _) => CopyBetween(_left, _right)));
        _right.PlaceCommands(onTheLeft: true,
            ("← Copy", (_, _) => CopyBetween(_right, _left)),
            ("Delete", (_, _) => DeleteFrom(_right)));

        _left.BecameActive += (_, _) => Activate(_left);
        _right.BecameActive += (_, _) => Activate(_right);
        _left.PanelDropped += (sender, _) => CopyBetween((BrowserPanel)sender!, _left);
        _right.PanelDropped += (sender, _) => CopyBetween((BrowserPanel)sender!, _right);
        _left.HostFilesDropped += (_, paths) => CopyHostFiles(paths, _left);
        _right.HostFilesDropped += (_, paths) => CopyHostFiles(paths, _right);

        Load += (_, _) => Startup();
        FormClosing += (_, e) =>
        {
            if (!_left.ConfirmDiscard() || !_right.ConfirmDiscard())
                e.Cancel = true;
        };
        Shown += (_, _) =>
        {
            split.SplitterDistance = split.Width / 2;
            Activate(_left);
        };
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.F5)
        {
            BrowserPanel other = _active == _left ? _right : _left;
            CopyBetween(_active, other);
            return true;
        }
        if (keyData == Keys.F8)
        {
            DeleteFrom(_active);
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void Startup()
    {
        _status.Text = "F5 copies from the active panel. A copy onto the card writes only the selected slice.";
        try
        {
            _left.OpenFolder(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        }
        catch (Exception ex)
        {
            _status.Text = ex.Message;
        }

        try
        {
            foreach (DriveDescription drive in DriveCatalog.RemovableDrives())
            {
                try
                {
                    _right.OpenPhysical(drive.Index);
                    _status.Text = "Opened " + drive.Model + ". Open a user area, then drag files into it.";
                    Activate(_right);
                    return;
                }
                catch (InvalidDataException)
                {
                }
            }
        }
        catch (Exception ex)
        {
            _status.Text = ex.Message;
        }
    }

    private void Activate(BrowserPanel panel)
    {
        _active = panel;
        _left.SetActive(panel == _left);
        _right.SetActive(panel == _right);
    }

    private void CopyBetween(BrowserPanel source, BrowserPanel target)
    {
        try
        {
            if (source.Folder != null && target.Folder != null)
                CopyHostFiles(source.SelectedHostFiles(), target);
            else if (source.Folder != null && target.Filesystem != null)
                CopyHostFiles(source.SelectedHostFiles(), target);
            else if (source.Filesystem != null && target.Folder != null)
                CopyCpmToHost(source.SelectedCpmFiles(), source.Filesystem, target);
            else if (source.Filesystem != null && target.Filesystem != null)
                CopyCpmToCpm(source, target);
            else
                MessageBox.Show("Open a folder or a slice on both sides.", "Copy", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Copy failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void CopyHostFiles(string[] paths, BrowserPanel target)
    {
        var files = new List<string>();
        foreach (string path in paths)
        {
            if (Directory.Exists(path))
                files.AddRange(Directory.EnumerateFiles(path));
            else if (File.Exists(path))
                files.Add(path);
        }
        CopyHostFiles(files, target);
    }

    private void CopyHostFiles(List<string> files, BrowserPanel target)
    {
        if (files.Count == 0)
        {
            MessageBox.Show("Select one or more files.", "Copy", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (target.Filesystem != null)
        {
            CopyHostToSlice(files, target);
            return;
        }
        if (target.Folder == null)
            return;

        if (MessageBox.Show(CopyPreview(files.Count, target.Folder, files.Select(Path.GetFileName)), "Copy", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;
        var written = new List<string>();
        foreach (string file in files)
        {
            string name = Path.GetFileName(file);
            string destination = Path.Combine(target.Folder, name);
            if (string.Equals(Path.GetFullPath(file), Path.GetFullPath(destination), StringComparison.OrdinalIgnoreCase))
                continue;
            File.Copy(file, destination, overwrite: true);
            written.Add(name);
        }
        target.Reload();
        target.ShowFiles(written);
        _status.Text = $"Copied {written.Count} file(s) to {target.Folder}.";
    }

    private void CopyHostToSlice(List<string> files, BrowserPanel target)
    {
        CpmFileSystem slice = target.Filesystem!;
        var planned = new List<(string Path, string Name, string Type)>();
        var usedNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (string file in files)
        {
            (string name, string type) = CpmFileSystem.SplitHostName(file);
            if (name.Length == 0)
                throw new IOException($"{Path.GetFileName(file)} has no usable CP/M name. Use letters, digits, and the usual CP/M punctuation, up to 8 characters.");
            string key = name + "." + type;
            if (!usedNames.Add(key))
                throw new IOException($"{Path.GetFileName(file)} and another selected file both become the CP/M name {key}.");
            planned.Add((file, name, type));
        }
        int user = target.ChosenUser() ?? 0;
        if (!PrepareSlice(slice, planned.Count, planned.Select(f => Path.GetFileName(f.Path)), $"Stored as user {user}."))
            return;

        int written = 0;
        foreach ((string path, string name, string type) in planned)
        {
            _status.Text = "Writing " + Path.GetFileName(path);
            slice.WriteFile(user, name, type, File.ReadAllBytes(path));
            slice.Save();
            written++;
        }
        target.Reload();
        target.RefreshCaption();
        target.ShowFiles(planned.Select(file => file.Type.Length == 0 ? file.Name : file.Name + "." + file.Type));
        _status.Text = $"Wrote {written} file(s) to slice {slice.SliceIndex} as user {user}. Other slices were not changed.";
    }

    private void CopyCpmToHost(List<CpmFile> files, CpmFileSystem source, BrowserPanel target)
    {
        string folder = target.Folder!;
        if (files.Count == 0)
        {
            MessageBox.Show("Select one or more files.", "Copy", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (MessageBox.Show(CopyPreview(files.Count, folder, files.Select(f => f.DisplayName)), "Copy", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;

        var written = new List<string>();
        foreach (CpmFile file in files)
        {
            string name = HostName(file);
            File.WriteAllBytes(Path.Combine(folder, name), source.ReadFile(file));
            written.Add(name);
        }
        target.Reload();
        target.ShowFiles(written);
        _status.Text = $"Copied {written.Count} file(s) to {folder}.";
    }

    private static string CopyPreview(int count, string destination, IEnumerable<string?> names)
    {
        var listed = names.Where(name => !string.IsNullOrWhiteSpace(name)).Take(12).ToList();
        string preview = string.Join("\n", listed);
        if (count > listed.Count)
            preview += "\n…";
        return $"Copy {count} file(s) to {destination}?\n\n{preview}";
    }

    private void CopyCpmToCpm(BrowserPanel source, BrowserPanel target)
    {
        if (ReferenceEquals(source.Disk, target.Disk) && source.Filesystem!.SliceIndex == target.Filesystem!.SliceIndex)
        {
            MessageBox.Show("The two panels are showing the same slice.", "Copy", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        List<CpmFile> files = source.SelectedCpmFiles();
        if (files.Count == 0)
        {
            MessageBox.Show("Select one or more files.", "Copy", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        CpmFileSystem from = source.Filesystem!;
        CpmFileSystem to = target.Filesystem!;
        int? user = target.ChosenUser();
        string placement = user is int chosen
            ? $"Stored as user {chosen}."
            : "Each file keeps its user number.";
        if (!PrepareSlice(to, files.Count, files.Select(f => f.User + ":" + f.DisplayName), placement))
            return;

        foreach (CpmFile file in files)
        {
            int destinationUser = user ?? file.User;
            _status.Text = "Writing " + file.DisplayName;
            to.WriteFile(destinationUser, file.Name, file.Type, from.ReadFile(file));
            to.Save();
        }
        target.Reload();
        target.RefreshCaption();
        target.ShowFiles(files.Select(file => file.DisplayName));
        _status.Text = user is int writtenAs
            ? $"Wrote {files.Count} file(s) to slice {to.SliceIndex} as user {writtenAs}. Other slices were not changed."
            : $"Wrote {files.Count} file(s) to slice {to.SliceIndex}. Each file kept its user number.";
    }

    private bool PrepareSlice(CpmFileSystem slice, int count, IEnumerable<string?> names, string placement)
    {
        if (!slice.HasCpMDirectory)
        {
            DialogResult init = MessageBox.Show(
                $"Slice {slice.SliceIndex} has no CP/M directory. Initialize that directory so files can be added?\n\nThis clears only the directory of this slice. The 16 KB system tracks, the other slices, and the partition table are left alone.",
                "Initialize slice",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);
            if (init != DialogResult.Yes)
                return false;
        }

        string where = slice.IsPhysical
            ? $"slice {slice.SliceIndex} on the card ({slice.DisplayName})"
            : $"slice {slice.SliceIndex} of {slice.DisplayName}";
        string extra = slice.IsPhysical
            ? "\n\nOnly this slice is written. The partition table and the other slices are not changed."
            : "";
        string listed = string.Join(", ", names.Take(6));
        if (count > 6)
            listed += ", …";
        DialogResult go = MessageBox.Show(
            $"Copy {count} file(s) to {where}?\n\n{placement}\n\n{listed}\n\nA file already there with the same name and user is replaced.{extra}",
            "Copy",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);
        if (go != DialogResult.Yes)
            return false;
        if (!slice.HasCpMDirectory)
            slice.InitializeDirectory();
        return true;
    }

    private void DeleteFrom(BrowserPanel panel)
    {
        Activate(panel);
        if (panel.Filesystem == null)
        {
            DeleteHostEntries(panel);
            return;
        }
        List<CpmFile> files = panel.SelectedCpmFiles();
        if (files.Count == 0)
        {
            MessageBox.Show("Select one or more files in the slice.", "Delete", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        string listed = string.Join(", ", files.Take(8).Select(f => f.User + ":" + f.DisplayName));
        DialogResult answer = MessageBox.Show(
            $"Remove {files.Count} file(s) from slice {panel.Filesystem.SliceIndex}?\n\n{listed}",
            "Delete",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning);
        if (answer != DialogResult.Yes)
            return;
        foreach (CpmFile file in files)
            panel.Filesystem.DeleteFile(file.User, file.Name, file.Type);
        panel.Filesystem.Save();
        panel.Reload();
        panel.RefreshCaption();
        _status.Text = $"Removed {files.Count} file(s) from slice {panel.Filesystem.SliceIndex}.";
    }

    private void DeleteHostEntries(BrowserPanel panel)
    {
        if (panel.Folder == null)
        {
            MessageBox.Show("Open a folder first.", "Delete", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        List<(string Path, bool IsDirectory)> entries = panel.SelectedHostEntries();
        if (entries.Count == 0)
        {
            MessageBox.Show("Select one or more files in the folder.", "Delete", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var names = entries.Select(entry => Path.GetFileName(entry.Path)).Take(12).ToList();
        string listed = string.Join("\n", names);
        if (entries.Count > names.Count)
            listed += "\n…";
        DialogResult answer = MessageBox.Show(
            $"Move {entries.Count} item(s) from {panel.Folder} to the Recycle Bin?\n\n{listed}",
            "Delete",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning);
        if (answer != DialogResult.Yes)
            return;

        if (!Recycle(entries.Select(entry => entry.Path).ToList(), out string? error))
        {
            MessageBox.Show(error, "Delete", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        panel.Reload();
        int removed = entries.Count(entry => !File.Exists(entry.Path) && !Directory.Exists(entry.Path));
        _status.Text = removed == 0
            ? "Nothing was removed."
            : $"Moved {removed} item(s) from {panel.Folder} to the Recycle Bin.";
    }

    private bool Recycle(List<string> paths, out string? error)
    {
        error = null;
        var operation = new ShFileOperation
        {
            hwnd = Handle,
            wFunc = 3,
            pFrom = string.Join('\0', paths) + '\0',
            fFlags = 0x0040 | 0x0010 | 0x0400
        };
        int result = SHFileOperation(ref operation);
        if (result == 0 && !operation.AnyAborted)
            return true;
        error = operation.AnyAborted
            ? "Delete was cancelled."
            : "Windows could not move the selection to the Recycle Bin.";
        return false;
    }

    [System.Runtime.InteropServices.DllImport("shell32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int SHFileOperation(ref ShFileOperation operation);

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private struct ShFileOperation
    {
        public IntPtr hwnd;
        public uint wFunc;
        [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPWStr)]
        public string? pFrom;
        [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPWStr)]
        public string? pTo;
        public ushort fFlags;
        [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
        public bool AnyAborted;
        public IntPtr hNameMappings;
        [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPWStr)]
        public string? lpszProgressTitle;
    }

    private static string HostName(CpmFile file)
    {
        string name = file.DisplayName.Trim().TrimEnd('.');
        foreach (char invalid in Path.GetInvalidFileNameChars())
            name = name.Replace(invalid, '_');
        if (name.Length == 0)
            name = "FILE";
        if (file.User != 0)
            name = file.User + "_" + name;
        return name;
    }
}
