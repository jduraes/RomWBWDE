using System.Drawing.Drawing2D;
using RomWBWDE;

namespace RomWBWDE.Explorer;

internal sealed class BrowserPanel : UserControl
{
    private readonly Panel _header;
    private readonly Panel _accent;
    private readonly Panel _toolbar;
    private readonly FlowLayoutPanel _buttons;
    private FlowLayoutPanel? _commands;
    private readonly Label _title;
    private readonly Label _subtitle;
    private readonly Panel _sliceRow;
    private readonly FlowLayoutPanel _sliceActions;
    private readonly Label _sliceLabel;
    private readonly ComboBox _slices;
    private readonly Label _userLabel;
    private readonly ComboBox _users;
    private readonly FlowLayoutPanel _nav;
    private readonly Button _writeSlice;
    private readonly Button _extractSlice;
    private readonly Button _deleteSlice;
    private readonly Button _previousSlice;
    private readonly Button _nextSlice;
    private readonly OpeningList _files;
    private readonly CheckBox _selectAll;
    private readonly Label _status;
    private readonly Button _up;
    private readonly ToolTip _tips = new();
    private bool _fillingSlices;
    private bool _fillingUsers;
    private bool _syncingChecks;
    private bool _fitQueued;
    private int _selectAllRequest;
    private bool _active;
    private int _sortColumn;
    private bool _sortAscending = true;
    private int? _dropTargetUser;

    private RomWbwDisk? _disk;
    private CpmFileSystem? _filesystem;
    private string? _folder;
    private int? _user;

    public BrowserPanel()
    {
        Font = new Font("Segoe UI", 9f);
        _header = new Panel { Dock = DockStyle.Top, Height = 65 };
        _accent = new Panel { Dock = DockStyle.Top, Height = 3 };
        _toolbar = new Panel { Dock = DockStyle.Top, Height = 36 };
        _buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(6, 4, 0, 0),
            WrapContents = false,
            Margin = new Padding(0)
        };
        _buttons.Controls.Add(MakeButton("Folder", (_, _) => BrowseFolder()));
        _buttons.Controls.Add(MakeButton("Image", (_, _) => BrowseImage()));
        _buttons.Controls.Add(MakeButton("Drive", (_, _) => BrowseDrive()));
        _up = MakeButton("Up", (_, _) => Up());
        _buttons.Controls.Add(_up);
        _toolbar.Controls.Add(_buttons);

        _title = new Label
        {
            Dock = DockStyle.Top,
            Height = 26,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(8, 2, 4, 0),
            AutoEllipsis = true,
            ForeColor = Color.Black,
            Font = new Font("Segoe UI", 11f, FontStyle.Bold)
        };
        _subtitle = new Label
        {
            Dock = DockStyle.Top,
            Height = 18,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(8, 0, 4, 0),
            AutoEllipsis = true,
            Visible = false,
            ForeColor = Color.Black,
            Font = new Font("Segoe UI", 8.5f, FontStyle.Regular)
        };
        _sliceLabel = new Label
        {
            Text = "Slice",
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Color.Black,
            Font = new Font("Segoe UI", 9f, FontStyle.Bold)
        };
        _slices = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            IntegralHeight = false,
            DropDownHeight = 480,
            BackColor = Color.White,
            ForeColor = Color.Black,
            Font = new Font("Consolas", 10f, FontStyle.Regular)
        };
        _slices.SelectedIndexChanged += SliceChanged;
        _userLabel = new Label
        {
            Text = "User",
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Color.Black,
            Font = new Font("Segoe UI", 9f, FontStyle.Bold)
        };
        _users = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            IntegralHeight = false,
            DropDownHeight = 420,
            BackColor = Color.White,
            ForeColor = Color.Black,
            Font = new Font("Consolas", 10f, FontStyle.Regular)
        };
        _users.SelectedIndexChanged += UserChanged;
        _writeSlice = MakeButton("Write slice", (_, _) => WriteSlice());
        _extractSlice = MakeButton("Extract slice", (_, _) => ExtractSlice());
        _deleteSlice = MakeButton("Delete slice", (_, _) => DeleteSlice());
        _previousSlice = MakeButton("Previous", (_, _) => StepSlice(-1));
        _nextSlice = MakeButton("Next", (_, _) => StepSlice(1));
        _writeSlice.Margin = new Padding(0, 2, 6, 0);
        _extractSlice.Margin = new Padding(0, 2, 6, 0);
        _deleteSlice.Margin = new Padding(0, 2, 6, 0);
        _previousSlice.Margin = new Padding(0, 2, 6, 0);
        _nextSlice.Margin = new Padding(0, 2, 0, 0);
        _writeSlice.Padding = new Padding(14, 1, 14, 1);
        _extractSlice.Padding = new Padding(14, 1, 14, 1);
        _deleteSlice.Padding = new Padding(14, 1, 14, 1);
        _previousSlice.Padding = new Padding(14, 1, 14, 1);
        _nextSlice.Padding = new Padding(14, 1, 14, 1);
        _nav = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
            FlowDirection = FlowDirection.LeftToRight,
            Margin = new Padding(0),
            Padding = new Padding(0, 3, 4, 0)
        };
        _nav.Controls.Add(_previousSlice);
        _nav.Controls.Add(_nextSlice);
        _sliceActions = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 36,
            Visible = false,
            WrapContents = false,
            FlowDirection = FlowDirection.LeftToRight,
            Padding = new Padding(8, 3, 4, 0),
            Margin = new Padding(0)
        };
        _sliceActions.Controls.Add(_writeSlice);
        _sliceActions.Controls.Add(_extractSlice);
        _sliceActions.Controls.Add(_deleteSlice);
        _sliceRow = new Panel { Dock = DockStyle.Top, Height = 36, Visible = false };
        _sliceRow.Controls.Add(_sliceLabel);
        _sliceRow.Controls.Add(_slices);
        _sliceRow.Controls.Add(_userLabel);
        _sliceRow.Controls.Add(_users);
        _sliceRow.Controls.Add(_nav);
        _sliceRow.Resize += (_, _) => LayoutSliceRow();

        _header.Controls.Add(_sliceActions);
        _header.Controls.Add(_sliceRow);
        _header.Controls.Add(_subtitle);
        _header.Controls.Add(_title);
        _header.Controls.Add(_toolbar);
        _header.Controls.Add(_accent);

        _status = new Label
        {
            Dock = DockStyle.Bottom,
            Height = 24,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(8, 0, 4, 0),
            AutoEllipsis = true,
            ForeColor = Color.Black,
            BackColor = Color.FromArgb(245, 245, 245)
        };

        _files = new OpeningList
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            GridLines = true,
            HideSelection = false,
            MultiSelect = true,
            AllowDrop = true,
            BorderStyle = BorderStyle.None,
            BackColor = Color.White,
            ForeColor = Color.Black,
            Font = new Font("Segoe UI", 9f, FontStyle.Regular),
            UseCompatibleStateImageBehavior = false,
            CheckBoxes = true
        };
        _files.ItemCheck += FileItemCheck;
        _files.Columns.Add("Name", 280);
        _files.Columns.Add("Size", 90, HorizontalAlignment.Right);
        _files.Columns.Add("Modified", 140);
        _files.ColumnClick += (_, e) => SortBy(e.Column);
        _files.ItemActivate += (_, _) => ActivateItem();
        _files.ItemOpened += item =>
        {
            if (item.Tag is Row row)
                BeginInvoke(() => OpenRow(row));
        };
        _files.ViewportChanged += (_, _) => PositionSelectAll();
        _selectAll = new CheckBox
        {
            ThreeState = true,
            AutoCheck = false,
            AutoSize = false,
            Text = "",
            TabStop = false,
            Visible = false,
            BackColor = Color.White,
            FlatStyle = FlatStyle.Standard
        };
        _selectAll.MouseDown += SelectAllMouseDown;
        _files.Controls.Add(_selectAll);
        _files.ItemChecked += (_, _) =>
        {
            if (!_syncingChecks)
                UpdateSelectAll();
        };
        _files.ItemDrag += StartDrag;
        _files.DragEnter += DragEntered;
        _files.DragOver += DragEntered;
        _files.DragLeave += (_, _) => EndDragPaint();
        _files.DragDrop += DragDropped;
        _files.Enter += (_, _) => BecameActive?.Invoke(this, EventArgs.Empty);
        _files.MouseDown += (_, _) => BecameActive?.Invoke(this, EventArgs.Empty);
        _files.KeyDown += ListKeyDown;
        _files.HandleCreated += (_, _) => DropFilter.Allow(_files.Handle);
        _files.Resize += (_, _) => FitNameColumn();

        Controls.Add(_files);
        Controls.Add(_status);
        Controls.Add(_header);
        HandleCreated += (_, _) => ApplyIcons();
        PaintHeader();
        ShowHint();
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        LayoutSliceRow();
    }

    public void PlaceCommands(bool onTheLeft, params (string Text, EventHandler Click)[] commands)
    {
        _commands = new FlowLayoutPanel
        {
            Dock = onTheLeft ? DockStyle.Left : DockStyle.Right,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
            FlowDirection = FlowDirection.LeftToRight,
            Padding = onTheLeft ? new Padding(6, 4, 0, 0) : new Padding(0, 4, 6, 0),
            Margin = new Padding(0),
            BackColor = _toolbar.BackColor
        };
        foreach ((string text, EventHandler click) in commands)
            _commands.Controls.Add(MakeButton(text, click));
        int width = _commands.Padding.Horizontal + 4;
        foreach (Control control in _commands.Controls)
            width += control.GetPreferredSize(Size.Empty).Width + control.Margin.Horizontal;
        _commands.MinimumSize = new Size(width, 0);
        _toolbar.Controls.Add(_commands);
    }

    public event EventHandler? BecameActive;

    public event EventHandler<BrowserPanel>? PanelDropped;

    public event EventHandler<string[]>? HostFilesDropped;

    public CpmFileSystem? Filesystem => _filesystem;

    public RomWbwDisk? Disk => _disk;

    public string? Folder => _folder;

    public bool IsPhysical => _filesystem?.IsPhysical == true;

    public bool IsDirty => _filesystem?.IsDirty == true;

    /// <summary>
    /// User number files should be written as. Null means the slice root:
    /// host files go to user 0, and files copied from another slice keep their user.
    /// </summary>
    public int? ChosenUser()
    {
        int? dropped = _dropTargetUser;
        _dropTargetUser = null;
        if (_user is int open)
            return open;
        return dropped;
    }

    public string PlaceName
    {
        get
        {
            if (_folder != null)
                return _folder;
            if (_filesystem == null)
                return "this panel";
            string label = CurrentLabel();
            return _user is int user ? $"{label}, user {user}" : label;
        }
    }

    public void SetActive(bool active)
    {
        _active = active;
        PaintHeader();
    }

    public void OpenFolder(string path)
    {
        if (!ConfirmDiscard())
            return;
        CloseDisk();
        _folder = path;
        _user = null;
        ShowSliceRow(false);
        Reload();
        FolderMemory.Remember(path);
    }

    public void OpenImage(string path)
    {
        if (!ConfirmDiscard())
            return;
        var device = new FileBlockDevice(path, writable: true);
        try
        {
            Adopt(RomWbwDisk.Open(device));
        }
        catch
        {
            device.Dispose();
            throw;
        }
    }

    public void OpenPhysical(int index)
    {
        if (!ConfirmDiscard())
            return;
        PhysicalDrive drive = PhysicalDrive.Open(index, writable: true);
        try
        {
            Adopt(RomWbwDisk.Open(drive));
        }
        catch
        {
            drive.Dispose();
            throw;
        }
    }

    public bool ConfirmDiscard()
    {
        if (_filesystem is not { IsDirty: true })
            return true;

        DialogResult answer = MessageBox.Show(
            "This slice has copies that are not written yet. Write them now?",
            "Unsaved slice",
            MessageBoxButtons.YesNoCancel,
            MessageBoxIcon.Warning);
        if (answer == DialogResult.Cancel)
            return false;
        if (answer == DialogResult.Yes)
            _filesystem.Save();
        return true;
    }

    public void Reload()
    {
        _files.BeginUpdate();
        _files.ListViewItemSorter = null;
        try
        {
            _files.Items.Clear();
            if (_folder != null)
                FillFolder();
            else if (_filesystem != null)
                FillSlice();
            else
                ShowHint();
        }
        finally
        {
            _files.EndUpdate();
            FitNameColumn();
            UpdateSelectAll();
        }
    }

    public List<(string Path, bool IsDirectory)> SelectedHostEntries()
    {
        var entries = new List<(string Path, bool IsDirectory)>();
        foreach (ListViewItem item in ChosenItems())
        {
            if (item.Tag is not Row row || row.Path == null || row.IsParent)
                continue;
            entries.Add((row.Path, row.IsDirectory));
        }
        return entries;
    }

    public List<string> SelectedHostFiles()
    {
        var paths = new List<string>();
        foreach (ListViewItem item in ChosenItems())
        {
            if (item.Tag is not Row row || row.Path == null || row.IsParent)
                continue;
            if (row.IsDirectory)
            {
                try
                {
                    paths.AddRange(Directory.EnumerateFiles(row.Path));
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
            else
            {
                paths.Add(row.Path);
            }
        }
        return paths;
    }

    public List<CpmFile> SelectedCpmFiles()
    {
        var files = new List<CpmFile>();
        if (_filesystem == null)
            return files;

        IReadOnlyList<CpmFile>? all = null;
        foreach (ListViewItem item in ChosenItems())
        {
            if (item.Tag is not Row row)
                continue;
            if (row.File is { } file)
            {
                files.Add(file);
            }
            else if (row.UserArea is int user)
            {
                all ??= _filesystem.List();
                files.AddRange(all.Where(f => f.User == user));
            }
        }
        return files;
    }

    private IEnumerable<ListViewItem> ChosenItems()
    {
        var ticked = new List<ListViewItem>();
        foreach (ListViewItem item in _files.CheckedItems)
        {
            if (item.Tag is Row { IsParent: false })
                ticked.Add(item);
        }
        if (ticked.Count > 0)
            return ticked;

        foreach (ListViewItem item in _files.SelectedItems)
            ticked.Add(item);
        return ticked;
    }

    private static string? NameOf(ListViewItem item)
    {
        if (item.Tag is not Row row)
            return item.Text;
        if (row.File != null)
            return row.File.DisplayName;
        if (!string.IsNullOrEmpty(row.Path))
            return Path.GetFileName(row.Path);
        return item.Text;
    }

    private void FileItemCheck(object? sender, ItemCheckEventArgs e)
    {
        if (_syncingChecks)
            return;
        ListViewItem item = _files.Items[e.Index];
        if (item.Tag is Row { IsParent: true })
        {
            e.NewValue = CheckState.Unchecked;
            if (!HitIsSelectAllBox())
                return;
            int request = ++_selectAllRequest;
            bool check = _selectAll.CheckState != CheckState.Checked;
            BeginInvoke(() =>
            {
                if (request != _selectAllRequest)
                    return;
                SetAllChecked(check);
            });
            return;
        }

        // A click in a multiple selection toggles every selected row. Only the row under the pointer, or the focused row from the keyboard, changes.
        if (!IsCheckTarget(item))
        {
            e.NewValue = item.Checked ? CheckState.Checked : CheckState.Unchecked;
            return;
        }

        _syncingChecks = true;
        item.Selected = e.NewValue == CheckState.Checked;
        _syncingChecks = false;
    }

    private bool HitIsSelectAllBox()
    {
        Point point = _files.PointToClient(Cursor.Position);
        if (_selectAll.Visible && _selectAll.Bounds.Contains(point))
            return true;
        return _files.HitTest(point).Location == ListViewHitTestLocations.StateImage;
    }

    private bool IsCheckTarget(ListViewItem item)
    {
        if (Control.MouseButtons == MouseButtons.None)
            return item.Focused;

        ListViewItem? hit = _files.HitTest(_files.PointToClient(Cursor.Position)).Item;
        return hit == null || hit.Index == item.Index;
    }

    public void ShowFiles(IEnumerable<string> names)
    {
        var wanted = new HashSet<string>(names.Where(name => !string.IsNullOrWhiteSpace(name)), StringComparer.OrdinalIgnoreCase);
        if (wanted.Count == 0)
            return;

        ListViewItem? first = null;
        _syncingChecks = true;
        try
        {
            foreach (ListViewItem item in _files.Items)
            {
                string? name = NameOf(item);
                bool hit = name != null && wanted.Contains(name);
                item.Selected = hit;
                item.Checked = hit && item.Tag is not Row { IsParent: true };
                if (hit && first == null)
                    first = item;
            }
        }
        finally
        {
            _syncingChecks = false;
        }

        first?.EnsureVisible();
        UpdateSelectAll();
    }

    public void RefreshCaption()
    {
        if (_filesystem == null || _slices.SelectedItem is not SliceChoice choice)
            return;

        IReadOnlyList<CpmFile> files = _filesystem.List();
        string? label = SliceIdentity.Describe(files.Select(f => (f.Name, f.Type)));
        label = SliceMap.PreferLabel(_disk?.Kind, label, files.Count);
        choice.Label = label;
        choice.Caption = SliceMap.CaptionFor(choice.Index, _filesystem.HasCpMDirectory, files.Count, label);
        _fillingSlices = true;
        int index = _slices.SelectedIndex;
        _slices.Items[index] = choice;
        _slices.SelectedIndex = index;
        _fillingSlices = false;
        FillUserChoices(keepUser: true);
        UpdateStatus();
        UpdateTitle();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            CloseDisk();
            _tips.Dispose();
        }
        base.Dispose(disposing);
    }

    private void Adopt(RomWbwDisk disk)
    {
        CloseDisk();
        _folder = null;
        _user = null;
        _disk = disk;
        ShowSliceRow(true);
        _fillingSlices = true;
        _slices.Items.Clear();
        foreach (SliceMap slice in disk.Slices)
            _slices.Items.Add(new SliceChoice { Index = slice.Index, Caption = slice.Caption, Label = slice.Label });
        _fillingSlices = false;
        if (_slices.Items.Count == 0)
        {
            _title.Text = "No complete slice fits on this card";
            _subtitle.Text = disk.Device.DisplayName;
            _status.Text = "The partition runs past the end of this card.";
            return;
        }
        _slices.SelectedIndex = 0;
        UpdateSliceButtons();
    }

    private void StepSlice(int delta)
    {
        int next = _slices.SelectedIndex + delta;
        if (next < 0 || next >= _slices.Items.Count)
            return;
        _slices.SelectedIndex = next;
    }

    private void ShowSliceRow(bool visible)
    {
        _sliceRow.Visible = visible;
        _sliceActions.Visible = visible;
        _subtitle.Visible = visible;
        _header.Height = visible ? 3 + 36 + 26 + 18 + 36 + 36 : 3 + 36 + 26;
        if (visible)
            LayoutSliceRow();
        UpdateSliceButtons();
    }

    private void LayoutSliceRow()
    {
        int width = _sliceRow.ClientSize.Width;
        if (width < 80)
            return;
        int navW = Math.Max(_nav.PreferredSize.Width, _nav.Width);
        _nav.SetBounds(Math.Max(0, width - navW), 0, navW, _sliceRow.Height);
        int y = 5;
        int h = Math.Max(22, _sliceRow.Height - 10);
        int right = _nav.Left - 8;
        int userW = Math.Clamp(right / 5, 100, 170);
        _users.SetBounds(right - userW, y, userW, h);
        _userLabel.SetBounds(_users.Left - 44, y, 42, h);
        _sliceLabel.SetBounds(8, y, 42, h);
        _slices.SetBounds(52, y, Math.Max(48, _userLabel.Left - 58), h);
    }

    private void UpdateSliceButtons()
    {
        bool any = _sliceRow.Visible && _slices.Items.Count > 0;
        _writeSlice.Enabled = any && _filesystem != null;
        _extractSlice.Enabled = any && _filesystem != null;
        _deleteSlice.Enabled = any && _filesystem != null;
        _previousSlice.Enabled = any && _slices.SelectedIndex > 0;
        _nextSlice.Enabled = any && _slices.SelectedIndex >= 0 && _slices.SelectedIndex < _slices.Items.Count - 1;
    }

    private void DeleteSlice()
    {
        BecameActive?.Invoke(this, EventArgs.Empty);
        if (_disk == null || _filesystem == null)
        {
            MessageBox.Show("Open the card and choose a slice.", "Delete slice", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (!_disk.Device.CanWrite)
        {
            MessageBox.Show("This disk was opened read-only.", "Delete slice", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (!ConfirmDiscard())
            return;

        int files = _filesystem.List().Count;
        if (files == 0)
        {
            MessageBox.Show("This slice is already unused.", "Delete slice", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        string target = _filesystem.IsPhysical
            ? $"slice {_filesystem.SliceIndex} on the card ({_filesystem.DisplayName})"
            : $"slice {_filesystem.SliceIndex} of {_filesystem.DisplayName}";
        string caption = _slices.SelectedItem is SliceChoice choice ? choice.Caption.Trim() : "slice " + _filesystem.SliceIndex;
        DialogResult go = MessageBox.Show(
            $"Erase {caption} ({files} files) from {target}?\n\n{SliceCaution("The whole slice is cleared, including its system tracks, and left unused. The partition table and the other slices are not changed.", "The whole CP/M partition is cleared and left unused. The FAT32 volume and the partition table are not changed.")}",
            "Delete slice",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning);
        if (go != DialogResult.Yes)
            return;

        int index = _filesystem.SliceIndex;
        _status.Text = "Erasing slice " + index + "…";
        try
        {
            _disk.ClearSlice(index);
        }
        catch (Exception ex)
        {
            ReopenSlice(index);
            MessageBox.Show(ex.Message, "Delete slice", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        ReopenSlice(index);
        _status.Text = $"Erased slice {index}. The other slices were not changed.";
    }

    private void ExtractSlice()
    {
        BecameActive?.Invoke(this, EventArgs.Empty);
        if (_disk == null || _filesystem == null)
        {
            MessageBox.Show("Open the card and choose a slice.", "Extract slice", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        string? label = _slices.SelectedItem is SliceChoice choice ? choice.Label : null;
        using var dialog = new SaveFileDialog
        {
            Title = "Save this slice as an 8 MiB RomWBW image",
            Filter = "Slice image|*.img|All files|*.*",
            FileName = SliceFileName(_filesystem.SliceIndex, label),
            InitialDirectory = _folder ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            OverwritePrompt = true
        };
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK)
            return;

        try
        {
            _status.Text = "Saving slice " + _filesystem.SliceIndex + "…";
            _disk.SaveSlice(_filesystem.SliceIndex, dialog.FileName);
            _status.Text = $"Saved slice {_filesystem.SliceIndex} to {dialog.FileName}. It is an 8 MiB RomWBW slice image.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Extract slice", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private static string SliceFileName(int index, string? label)
    {
        string name = index.ToString("00");
        if (!string.IsNullOrWhiteSpace(label))
        {
            foreach (char invalid in Path.GetInvalidFileNameChars())
                label = label.Replace(invalid, '-');
            name += "-" + label.Trim();
        }
        return name + ".img";
    }

    private void WriteSlice()
    {
        BecameActive?.Invoke(this, EventArgs.Empty);
        if (_disk == null || _filesystem == null)
        {
            MessageBox.Show("Open the card and choose an unused slice.", "Write slice", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (!_disk.Device.CanWrite)
        {
            MessageBox.Show("This disk was opened read-only.", "Write slice", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (!ConfirmDiscard())
            return;
        if (_filesystem.List().Count > 0)
        {
            MessageBox.Show(
                "This slice already has files. Choose an unused slice. The other slices are not changed.",
                "Write slice",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        using var dialog = new OpenFileDialog
        {
            Title = "RomWBW slice image to write into this slice",
            Filter = "Disk image|*.img;*.cpm;*.bin|All files|*.*",
            InitialDirectory = _folder ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
        };
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK)
            return;

        RomWbwDisk? source = null;
        try
        {
            var image = new FileBlockDevice(dialog.FileName, writable: false);
            try
            {
                source = RomWbwDisk.Open(image);
            }
            catch
            {
                image.Dispose();
                throw;
            }

            if (source.Slices.Count == 0 || source.Slices.Any(slice => slice.Length != RomWbwDisk.SliceLength))
                throw new IOException("The image has to be one 8 MiB slice, or a RomWBW hard disk made of full 8 MiB slices.");

            SliceMap? origin = source.Slices.Count == 1
                ? source.Slices[0]
                : ChooseSourceSlice(source, Path.GetFileName(dialog.FileName));
            if (origin == null)
                return;

            string target = _filesystem.IsPhysical
                ? $"slice {_filesystem.SliceIndex} on the card ({_filesystem.DisplayName})"
                : $"slice {_filesystem.SliceIndex} of {_filesystem.DisplayName}";
            DialogResult go = MessageBox.Show(
                $"Write {origin.Caption.Trim()} from {Path.GetFileName(dialog.FileName)} into {target}?\n\n{SliceCaution("Only this slice is replaced, including its system tracks. The partition table and the other slices are not changed.", "Only this CP/M partition is replaced. The FAT32 volume and the partition table are not changed.")}",
                "Write slice",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);
            if (go != DialogResult.Yes)
                return;

            _status.Text = "Writing slice " + _filesystem.SliceIndex + "…";
            int index = _filesystem.SliceIndex;
            try
            {
                _disk.ReplaceSlice(index, source, origin.Index);
            }
            catch (Exception)
            {
                ReopenSlice(index);
                throw;
            }

            ReopenSlice(index);
            _status.Text = $"Wrote slice {index}. The other slices were not changed.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Write slice", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            source?.Dispose();
        }
    }

    private void ReopenSlice(int index)
    {
        _filesystem?.Dispose();
        _filesystem = _disk!.OpenSlice(index);
        _user = null;
        FillUserChoices(keepUser: false);
        PaintHeader();
        Reload();
        RefreshCaption();
    }

    private SliceMap? ChooseSourceSlice(RomWbwDisk source, string fileName)
    {
        using var dialog = new Form
        {
            Text = "Slice to write",
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent,
            ClientSize = new Size(420, 320),
            MinimizeBox = false,
            MaximizeBox = false,
            Font = new Font("Segoe UI", 9f),
            BackColor = Color.White,
            ForeColor = Color.Black
        };
        var hint = new Label
        {
            Dock = DockStyle.Top,
            Height = 44,
            Padding = new Padding(12, 10, 12, 0),
            Text = fileName + " has more than one slice. Choose the slice to write."
        };
        var list = new ListBox
        {
            Dock = DockStyle.Fill,
            IntegralHeight = false,
            Font = new Font("Consolas", 10f),
            BorderStyle = BorderStyle.FixedSingle
        };
        foreach (SliceMap slice in source.Slices)
            list.Items.Add(slice);
        int formatted = source.Slices.ToList().FindIndex(slice => slice.LooksFormatted && slice.FileCount > 0);
        list.SelectedIndex = formatted >= 0 ? formatted : 0;
        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 44,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(8)
        };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Width = 88, Height = 28 };
        var ok = new Button { Text = "Write", DialogResult = DialogResult.OK, Width = 88, Height = 28 };
        buttons.Controls.Add(ok);
        buttons.Controls.Add(cancel);
        dialog.Controls.Add(list);
        dialog.Controls.Add(hint);
        dialog.Controls.Add(buttons);
        dialog.AcceptButton = ok;
        dialog.CancelButton = cancel;
        list.DoubleClick += (_, _) => dialog.DialogResult = DialogResult.OK;
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK || list.SelectedItem is not SliceMap selected)
            return null;
        return selected;
    }

    private void SliceChanged(object? sender, EventArgs e)
    {
        if (_fillingSlices || _disk == null || _slices.SelectedItem is not SliceChoice choice)
            return;
        if (_filesystem != null && _filesystem.SliceIndex == choice.Index)
            return;
        if (!ConfirmDiscard())
        {
            RestoreSliceSelection();
            return;
        }
        _filesystem?.Dispose();
        _filesystem = _disk.OpenSlice(choice.Index);
        _user = null;
        FillUserChoices(keepUser: false);
        UpdateSliceButtons();
        PaintHeader();
        Reload();
    }

    private void UserChanged(object? sender, EventArgs e)
    {
        if (_fillingUsers || _users.SelectedItem is not UserChoice choice)
            return;
        _user = choice.User;
        Reload();
    }

    private void RestoreSliceSelection()
    {
        if (_filesystem == null)
            return;
        _fillingSlices = true;
        for (int i = 0; i < _slices.Items.Count; i++)
        {
            if (_slices.Items[i] is SliceChoice choice && choice.Index == _filesystem.SliceIndex)
            {
                _slices.SelectedIndex = i;
                break;
            }
        }
        _fillingSlices = false;
    }

    private void FillUserChoices(bool keepUser)
    {
        int? keep = keepUser ? _user : null;
        _fillingUsers = true;
        _users.Items.Clear();
        bool ready = _filesystem?.HasCpMDirectory == true;
        _users.Enabled = ready;
        if (!ready || _filesystem == null)
        {
            _user = null;
            _fillingUsers = false;
            return;
        }

        var counts = _filesystem.List().GroupBy(f => f.User).ToDictionary(g => g.Key, g => g.Count());
        _users.Items.Add(new UserChoice(null, "All users"));
        var ids = new SortedSet<int>();
        for (int id = 0; id <= 15; id++)
            ids.Add(id);
        foreach (int id in counts.Keys)
            ids.Add(id);
        foreach (int id in ids)
        {
            string caption = counts.TryGetValue(id, out int n) && n > 0 ? $"{id,-2}  {n} files" : $"{id,-2}  empty";
            _users.Items.Add(new UserChoice(id, caption));
        }

        int select = 0;
        if (keep is int user)
        {
            for (int i = 0; i < _users.Items.Count; i++)
            {
                if (_users.Items[i] is UserChoice choice && choice.User == user)
                {
                    select = i;
                    break;
                }
            }
        }
        _users.SelectedIndex = select;
        _user = (_users.SelectedItem as UserChoice)?.User;
        _fillingUsers = false;
    }

    private void FillFolder()
    {
        SetColumns("Size", "Modified", sizeColumn: false);
        DirectoryInfo directory = new(_folder!);
        _title.Text = directory.FullName;
        _subtitle.Text = "";
        _tips.SetToolTip(_title, directory.FullName);
        DirectoryInfo? parent = directory.Parent;
        if (parent != null)
            _files.Items.Add(Item("..", "", "", new Row { Path = parent.FullName, IsDirectory = true, IsParent = true }, folder: true));

        IEnumerable<FileSystemInfo> entries;
        try
        {
            entries = directory.EnumerateFileSystemInfos();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _status.Text = ex.Message;
            return;
        }

        foreach (FileSystemInfo entry in entries.OrderBy(e => e is not DirectoryInfo).ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase))
        {
            if (entry is DirectoryInfo dir)
                _files.Items.Add(Item(dir.Name, "", dir.LastWriteTime.ToString("g"), new Row { Path = dir.FullName, IsDirectory = true }, folder: true));
            else if (entry is FileInfo file)
                _files.Items.Add(Item(file.Name, DriveDescription.FormatSize(file.Length), file.LastWriteTime.ToString("g"), new Row { Path = file.FullName }, folder: false));
        }
        _up.Enabled = parent != null;
        _status.Text = $"{_files.Items.Count} items";
    }

    private void FillSlice()
    {
        CpmFileSystem fs = _filesystem!;
        UpdateTitle();
        if (!fs.HasCpMDirectory)
        {
            SetColumns("Files", "Size", sizeColumn: true);
            _up.Enabled = false;
            _status.Text = "Unused. This slice has no CP/M directory. Copy a file here to initialize it as user 0.";
            return;
        }

        IReadOnlyList<CpmFile> files = fs.List();
        if (_user is int user)
        {
            SetColumns("Size", "", sizeColumn: true);
            _files.Items.Add(Item("..", "", "", new Row { IsParent = true, IsDirectory = true }, folder: true));
            foreach (CpmFile file in files.Where(f => f.User == user).OrderBy(f => f.Name, StringComparer.Ordinal).ThenBy(f => f.Type, StringComparer.Ordinal))
                _files.Items.Add(Item(file.DisplayName, DriveDescription.FormatSize(file.Size), "", new Row { File = file }, folder: false));
            _up.Enabled = true;
        }
        else
        {
            SetColumns("Files", "Size", sizeColumn: true);
            foreach (IGrouping<int, CpmFile> group in files.GroupBy(f => f.User).OrderBy(g => g.Key))
            {
                long bytes = group.Sum(f => (long)f.Size);
                _files.Items.Add(Item(
                    "User " + group.Key,
                    group.Count().ToString(),
                    DriveDescription.FormatSize(bytes),
                    new Row { UserArea = group.Key, IsDirectory = true },
                    folder: true));
            }
            _up.Enabled = false;
        }
        UpdateStatus();
    }

    private void UpdateTitle()
    {
        if (_filesystem == null)
            return;
        string label = CurrentLabel();
        _title.Text = _user is int user ? $"{label}    ·    user {user}" : label;
        _subtitle.Text = _filesystem.DisplayName;
        _tips.SetToolTip(_title, _subtitle.Text);
    }

    private string SliceCaution(string hd1k, string minez) => _disk?.Kind == "minez" ? minez : hd1k;

    private string CurrentLabel()
    {
        if (_slices.SelectedItem is SliceChoice choice && !string.IsNullOrEmpty(choice.Label))
            return choice.Label;
        if (_filesystem == null)
            return "Slice";
        if (_filesystem.HasCpMDirectory && _filesystem.List().Count > 0)
            return "Slice " + _filesystem.SliceIndex;
        return "Slice " + _filesystem.SliceIndex + "  ·  unused";
    }

    private void UpdateStatus()
    {
        if (_filesystem == null)
            return;
        string free = DriveDescription.FormatSize(_filesystem.FreeBytes);
        string dirty = _filesystem.IsDirty ? "    not written yet" : "";
        if (!_filesystem.HasCpMDirectory)
        {
            _status.Text = "Unused. Copy a file here to initialize this slice." + dirty;
            return;
        }
        if (_user == null && _filesystem.List().Count == 0)
        {
            _status.Text = "Unused." + dirty;
            return;
        }

        int shown = 0;
        foreach (ListViewItem item in _files.Items)
        {
            if (item.Tag is Row { IsParent: false })
                shown++;
        }

        if (_user is int user)
        {
            string empty = shown == 0 ? "    drop files here to add them" : "";
            _status.Text = $"{shown} files in user {user}    {free} free on this slice{empty}{dirty}";
            return;
        }

        int total = _filesystem.List().Count;
        _status.Text = $"{shown} user areas    {total} files    {free} free{dirty}";
    }

    private void ShowHint()
    {
        _title.Text = "Open a folder, a disk image, or the card";
        _subtitle.Text = "";
        _status.Text = "Drag files onto this panel, or press F5";
        ShowSliceRow(false);
        _up.Enabled = false;
    }

    private void BrowseFolder()
    {
        BecameActive?.Invoke(this, EventArgs.Empty);
        using var dialog = new FolderBrowserDialog
        {
            InitialDirectory = _folder ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            UseDescriptionForTitle = true,
            Description = "Folder to copy to and from"
        };
        if (dialog.ShowDialog(FindForm()) == DialogResult.OK)
            OpenFolder(dialog.SelectedPath);
    }

    private void BrowseImage()
    {
        BecameActive?.Invoke(this, EventArgs.Empty);
        using var dialog = new OpenFileDialog
        {
            Title = "Open a RomWBW disk image or a single slice",
            Filter = "Disk image|*.img;*.cpm;*.bin|All files|*.*",
            InitialDirectory = _folder ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
        };
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK)
            return;
        try
        {
            OpenImage(dialog.FileName);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Cannot open image", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void BrowseDrive()
    {
        BecameActive?.Invoke(this, EventArgs.Empty);
        IReadOnlyList<DriveDescription> drives;
        try
        {
            drives = DriveCatalog.RemovableDrives()
                .Select(drive => new DriveDescription
                {
                    Index = drive.Index,
                    Model = drive.Model,
                    ReportedSize = drive.ReportedSize,
                    MediaLoaded = drive.MediaLoaded,
                    Letters = DriveCatalog.LookupLetters(drive.Index)
                })
                .ToList();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Cannot list drives", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (drives.Count == 0)
        {
            MessageBox.Show("No removable drive with media was found.", "Drives", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var dialog = new Form
        {
            Text = "Open a drive",
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent,
            ClientSize = new Size(680, 280),
            MinimizeBox = false,
            MaximizeBox = false,
            Font = new Font("Segoe UI", 9f),
            BackColor = Color.White,
            ForeColor = Color.Black
        };
        var hint = new Label
        {
            Dock = DockStyle.Top,
            Height = 44,
            Padding = new Padding(12, 10, 12, 0),
            Text = "Removable disks with media. The card is opened for one slice at a time."
        };
        var list = new ListBox
        {
            Dock = DockStyle.Fill,
            IntegralHeight = false,
            HorizontalScrollbar = true,
            Font = new Font("Segoe UI", 10f),
            BorderStyle = BorderStyle.FixedSingle
        };
        foreach (DriveDescription drive in drives)
            list.Items.Add(drive);
        list.SelectedIndex = 0;
        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 44,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(8)
        };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Width = 88, Height = 28 };
        var ok = new Button { Text = "Open", DialogResult = DialogResult.OK, Width = 88, Height = 28 };
        buttons.Controls.Add(ok);
        buttons.Controls.Add(cancel);
        dialog.Controls.Add(list);
        dialog.Controls.Add(hint);
        dialog.Controls.Add(buttons);
        dialog.AcceptButton = ok;
        dialog.CancelButton = cancel;
        list.DoubleClick += (_, _) => dialog.DialogResult = DialogResult.OK;
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK || list.SelectedItem is not DriveDescription selected)
            return;
        try
        {
            OpenPhysical(selected.Index);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Cannot open drive", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void Up()
    {
        if (_filesystem != null && _user != null)
        {
            _users.SelectedIndex = 0;
            return;
        }
        if (_folder == null)
            return;
        DirectoryInfo? parent = Directory.GetParent(_folder);
        if (parent != null)
            OpenFolder(parent.FullName);
    }

    private void ActivateItem()
    {
        if (_files.SelectedItems.Count != 1 || _files.SelectedItems[0].Tag is not Row row)
            return;
        OpenRow(row);
    }

    private void OpenRow(Row row)
    {
        _selectAllRequest++;
        if (row.UserArea is int user)
        {
            SelectUser(user);
            return;
        }
        if (row.IsParent && _filesystem != null)
        {
            _users.SelectedIndex = 0;
            return;
        }
        if (row.IsDirectory && row.Path != null)
            OpenFolder(row.Path);
    }

    private void SelectUser(int user)
    {
        for (int i = 0; i < _users.Items.Count; i++)
        {
            if (_users.Items[i] is UserChoice choice && choice.User == user)
            {
                _users.SelectedIndex = i;
                return;
            }
        }
    }

    private void ListKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Control && e.KeyCode == Keys.A)
        {
            _syncingChecks = true;
            foreach (ListViewItem item in _files.Items)
            {
                bool include = item.Tag is not Row { IsParent: true };
                item.Selected = include;
                item.Checked = include;
            }
            _syncingChecks = false;
            UpdateSelectAll();
            e.SuppressKeyPress = true;
        }
        else if (e.KeyCode == Keys.Back)
        {
            Up();
            e.SuppressKeyPress = true;
        }
    }

    private void StartDrag(object? sender, ItemDragEventArgs e)
    {
        if (_files.SelectedItems.Count == 0)
            return;
        if (_files.SelectedItems.Count == 1 && _files.SelectedItems[0].Tag is Row { IsParent: true })
            return;
        var data = new DataObject();
        data.SetData(typeof(BrowserPanel), this);
        DoDragDrop(data, DragDropEffects.Copy);
    }

    private void DragEntered(object? sender, DragEventArgs e)
    {
        bool acceptable = e.Data?.GetDataPresent(DataFormats.FileDrop) == true || e.Data?.GetDataPresent(typeof(BrowserPanel)) == true;
        e.Effect = acceptable ? DragDropEffects.Copy : DragDropEffects.None;
        if (!acceptable)
            return;
        _files.BackColor = Color.FromArgb(232, 242, 252);
        HighlightUserRow(e);
    }

    private void DragDropped(object? sender, DragEventArgs e)
    {
        EndDragPaint();
        _dropTargetUser = UserUnder(e);
        BecameActive?.Invoke(this, EventArgs.Empty);
        if (e.Data?.GetData(typeof(BrowserPanel)) is BrowserPanel source && source != this)
            PanelDropped?.Invoke(source, this);
        else if (e.Data?.GetData(DataFormats.FileDrop) is string[] paths)
            HostFilesDropped?.Invoke(this, paths);
    }

    private int? UserUnder(DragEventArgs e)
    {
        if (_filesystem == null || _user != null)
            return null;
        Point point = _files.PointToClient(new Point(e.X, e.Y));
        return _files.HitTest(point).Item?.Tag is Row { UserArea: int user } ? user : null;
    }

    private void HighlightUserRow(DragEventArgs e)
    {
        int? user = UserUnder(e);
        foreach (ListViewItem item in _files.Items)
        {
            bool hit = user is int n && item.Tag is Row { UserArea: int area } && area == n;
            item.BackColor = hit ? Color.FromArgb(210, 228, 248) : Color.White;
        }
    }

    private void EndDragPaint()
    {
        _files.BackColor = Color.White;
        foreach (ListViewItem item in _files.Items)
            item.BackColor = Color.White;
    }

    private void SortBy(int column)
    {
        if (_sortColumn == column)
            _sortAscending = !_sortAscending;
        else
        {
            _sortColumn = column;
            _sortAscending = true;
        }
        _files.ListViewItemSorter = new ColumnSort(column, _sortAscending);
        _files.Sort();
        UpdateSelectAll();
    }

    private void SelectAllMouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left || e.Clicks > 1)
            return;
        SetAllChecked(_selectAll.CheckState != CheckState.Checked);
    }

    private void SetAllChecked(bool check)
    {
        _syncingChecks = true;
        try
        {
            foreach (ListViewItem item in _files.Items)
            {
                if (item.Tag is Row { IsParent: true })
                {
                    item.Checked = false;
                    continue;
                }

                item.Checked = check;
                item.Selected = check;
            }
        }
        finally
        {
            _syncingChecks = false;
        }

        UpdateSelectAll();
    }

    private void UpdateSelectAll()
    {
        int selectable = 0;
        int ticked = 0;
        ListViewItem? parent = null;
        foreach (ListViewItem item in _files.Items)
        {
            if (item.Tag is Row { IsParent: true })
            {
                parent = item;
                continue;
            }

            selectable++;
            if (item.Checked)
                ticked++;
        }

        if (parent == null || selectable == 0)
        {
            _selectAll.Visible = false;
            return;
        }

        _selectAll.CheckState = ticked == 0
            ? CheckState.Unchecked
            : ticked == selectable ? CheckState.Checked : CheckState.Indeterminate;
        PositionSelectAll();
    }

    private void PositionSelectAll()
    {
        ListViewItem? parent = null;
        foreach (ListViewItem item in _files.Items)
        {
            if (item.Tag is Row { IsParent: true })
            {
                parent = item;
                break;
            }
        }

        if (parent == null || parent.Bounds.Height < 4 || parent.Bounds.Bottom <= 0 || parent.Bounds.Top >= _files.ClientSize.Height)
        {
            _selectAll.Visible = false;
            return;
        }

        int box = 16;
        if (_selectAll.IsHandleCreated)
        {
            using Graphics graphics = _selectAll.CreateGraphics();
            box = CheckBoxRenderer.GetGlyphSize(graphics, System.Windows.Forms.VisualStyles.CheckBoxState.UncheckedNormal).Width;
        }

        Rectangle icon = parent.GetBounds(ItemBoundsPortion.Icon);
        int x = icon.Width > 0 ? Math.Max(1, icon.Left - box - 4) : parent.Bounds.Left + 3;
        int y = parent.Bounds.Top + Math.Max(0, (parent.Bounds.Height - box) / 2);
        _selectAll.SetBounds(x, y, box, box);
        _selectAll.Visible = true;
        _selectAll.BringToFront();
    }

    private void CloseDisk()
    {
        _filesystem?.Dispose();
        _filesystem = null;
        _disk?.Dispose();
        _disk = null;
        _user = null;
    }

    private void PaintHeader()
    {
        Color back = _active ? Color.White : Color.FromArgb(236, 236, 236);
        _header.BackColor = back;
        _toolbar.BackColor = back;
        _buttons.BackColor = back;
        if (_commands != null)
            _commands.BackColor = back;
        _title.BackColor = back;
        _subtitle.BackColor = back;
        _sliceRow.BackColor = back;
        _sliceActions.BackColor = back;
        _nav.BackColor = back;
        _sliceLabel.BackColor = back;
        _userLabel.BackColor = back;
        _accent.BackColor = _active ? Color.Black : Color.FromArgb(190, 190, 190);
        _status.BackColor = Color.FromArgb(245, 245, 245);
        _status.ForeColor = Color.Black;
    }

    private void FitNameColumn()
    {
        if (_fitQueued || !_files.IsHandleCreated || _files.IsDisposed)
            return;
        _fitQueued = true;
        _files.BeginInvoke(ApplyNameColumn);
    }

    private void ApplyNameColumn()
    {
        _fitQueued = false;
        if (_files.IsDisposed || _files.Columns.Count < 3 || _files.ClientSize.Width <= 0)
            return;

        int others = _files.Columns[1].Width + _files.Columns[2].Width;
        int name = Math.Max(80, _files.ClientSize.Width - others - 4);
        if (_files.Columns[0].Width != name)
            _files.Columns[0].Width = name;

        if (HorizontalOverflow(out int extra))
        {
            int shrunk = Math.Max(80, _files.Columns[0].Width - extra - 2);
            if (_files.Columns[0].Width != shrunk)
                _files.Columns[0].Width = shrunk;
        }
    }

    private bool HorizontalOverflow(out int extra)
    {
        extra = 0;
        var info = new ScrollInfo { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<ScrollInfo>(), fMask = 7 };
        if (!GetScrollInfo(_files.Handle, 0, ref info) || info.nPage == 0)
            return false;
        int range = info.nMax - info.nMin + 1;
        if (range <= info.nPage)
            return false;
        extra = range - (int)info.nPage;
        return extra > 0;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetScrollInfo(IntPtr hwnd, int bar, ref ScrollInfo info);

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct ScrollInfo
    {
        public int cbSize;
        public uint fMask;
        public int nMin;
        public int nMax;
        public uint nPage;
        public int nPos;
        public int nTrackPos;
    }

    private void SetColumns(string middle, string last, bool sizeColumn)
    {
        _files.Columns[0].Text = "Name";
        _files.Columns[1].Text = middle;
        _files.Columns[2].Text = last;
        _files.Columns[1].Width = sizeColumn && middle == "Size" ? 90 : middle == "Files" ? 70 : 90;
        _files.Columns[2].Width = string.IsNullOrEmpty(last) ? 0 : last == "Size" ? 100 : 150;
        _files.Columns[1].TextAlign = HorizontalAlignment.Right;
        _files.Columns[2].TextAlign = last == "Modified" ? HorizontalAlignment.Left : HorizontalAlignment.Right;
        FitNameColumn();
    }

    private void ApplyIcons()
    {
        int px = Math.Clamp((int)Math.Round(16 * DeviceDpi / 96.0), 16, 32);
        var images = new ImageList { ImageSize = new Size(px, px), ColorDepth = ColorDepth.Depth32Bit };
        images.Images.Add(DrawFolder(px));
        images.Images.Add(DrawPage(px));
        _files.SmallImageList = images;
    }

    private static Bitmap DrawFolder(int px)
    {
        var bmp = new Bitmap(px, px);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Color.Transparent);
        using var tab = new SolidBrush(Color.FromArgb(196, 138, 32));
        using var body = new SolidBrush(Color.FromArgb(232, 184, 72));
        int m = Math.Max(1, px / 16);
        g.FillRectangle(tab, m, px * 3 / 16, px * 7 / 16, px * 4 / 16);
        g.FillRectangle(body, m, px * 5 / 16, px - 2 * m, px * 9 / 16);
        return bmp;
    }

    private static Bitmap DrawPage(int px)
    {
        var bmp = new Bitmap(px, px);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Color.Transparent);
        int m = Math.Max(1, px / 16);
        int fold = Math.Max(4, px / 3);
        using var paper = new SolidBrush(Color.FromArgb(250, 250, 250));
        using var ink = new Pen(Color.FromArgb(55, 55, 55), Math.Max(1f, px / 16f));
        using var line = new Pen(Color.FromArgb(140, 140, 140), Math.Max(1f, px / 16f));
        using var path = new GraphicsPath();
        path.AddPolygon(new Point[]
        {
            new(m, m),
            new(px - m - fold, m),
            new(px - m - 1, m + fold),
            new(px - m - 1, px - m - 1),
            new(m, px - m - 1)
        });
        g.FillPath(paper, path);
        g.DrawPath(ink, path);
        g.DrawLine(ink, px - m - fold, m, px - m - fold, m + fold);
        g.DrawLine(ink, px - m - fold, m + fold, px - m - 1, m + fold);
        int left = m + px / 5;
        int right = px - m - px / 5;
        g.DrawLine(line, left, px / 2, right, px / 2);
        g.DrawLine(line, left, px / 2 + px / 5, right - px / 6, px / 2 + px / 5);
        return bmp;
    }

    private static ListViewItem Item(string name, string middle, string last, Row row, bool folder)
    {
        var item = new ListViewItem(name) { Tag = row, ImageIndex = folder ? 0 : 1 };
        item.SubItems.Add(middle);
        item.SubItems.Add(last);
        return item;
    }

    private static Button MakeButton(string text, EventHandler click)
    {
        var button = new Button
        {
            Text = text,
            AutoSize = true,
            Margin = new Padding(4, 2, 0, 0),
            Padding = new Padding(10, 1, 10, 1),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.White,
            ForeColor = Color.Black,
            Font = new Font("Segoe UI", 9f),
            UseVisualStyleBackColor = false
        };
        button.FlatAppearance.BorderColor = Color.FromArgb(160, 160, 160);
        button.FlatAppearance.MouseOverBackColor = Color.FromArgb(232, 232, 232);
        button.Click += click;
        return button;
    }

    private sealed class OpeningList : ListView
    {
        public event Action<ListViewItem>? ItemOpened;

        public event EventHandler? ViewportChanged;

        protected override void WndProc(ref Message m)
        {
            const int leftButtonDoubleClick = 0x0203;
            if (m.Msg == leftButtonDoubleClick)
            {
                long packed = m.LParam.ToInt64();
                int x = unchecked((short)packed);
                int y = unchecked((short)(packed >> 16));
                ListViewItem? item = HitTest(x, y).Item;
                if (item != null)
                    ItemOpened?.Invoke(item);
            }

            base.WndProc(ref m);

            const int verticalScroll = 0x0115;
            const int horizontalScroll = 0x0114;
            const int mouseWheel = 0x020A;
            const int keyDown = 0x0100;
            const int size = 0x0005;
            if (m.Msg is verticalScroll or horizontalScroll or mouseWheel or keyDown or size)
                ViewportChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private sealed class SliceChoice
    {
        public int Index { get; init; }

        public string? Label { get; set; }

        public string Caption { get; set; } = "";

        public override string ToString() => Caption;
    }

    private sealed class UserChoice(int? user, string caption)
    {
        public int? User { get; } = user;

        public string Caption { get; } = caption;

        public override string ToString() => Caption;
    }

    private sealed class Row
    {
        public string? Path { get; init; }

        public bool IsDirectory { get; init; }

        public bool IsParent { get; init; }

        public int? UserArea { get; init; }

        public CpmFile? File { get; init; }
    }

    private sealed class ColumnSort(int column, bool ascending) : System.Collections.IComparer
    {
        public int Compare(object? x, object? y)
        {
            var left = (ListViewItem?)x;
            var right = (ListViewItem?)y;
            if (left?.Tag is Row { IsParent: true })
                return -1;
            if (right?.Tag is Row { IsParent: true })
                return 1;

            string a = left?.SubItems.Count > column ? left.SubItems[column].Text : "";
            string b = right?.SubItems.Count > column ? right.SubItems[column].Text : "";
            int order;
            if (int.TryParse(a, out int ai) && int.TryParse(b, out int bi))
                order = ai.CompareTo(bi);
            else if (column == 0 && left?.Tag is Row { UserArea: int lu } && right?.Tag is Row { UserArea: int ru })
                order = lu.CompareTo(ru);
            else
                order = string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
            return ascending ? order : -order;
        }
    }
}
