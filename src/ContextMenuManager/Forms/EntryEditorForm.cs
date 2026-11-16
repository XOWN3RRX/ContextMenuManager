using ContextMenuManager.Core;
using ContextMenuManager.UI;
using ContextMenuManager.UI.Controls;

namespace ContextMenuManager;

/// <summary>Creates or edits one entry. Changes are only applied when the user clicks Save.</summary>
internal sealed class EntryEditorForm : Form
{
    private const int LabelWidth = 230;

    private readonly MenuEntry _entry;
    private readonly List<MenuEntry> _submenus;
    private readonly Label _heading;
    private readonly Panel _scroll;
    private readonly List<SettingRow> _rows = [];

    private readonly SegmentedControl _kind;
    private readonly InputBox _text;
    private readonly ComboBox _parent;
    private readonly InputBox _program;
    private readonly InputBox _arguments;
    private readonly InputBox _icon;
    private readonly PictureBox _iconPreview;
    private readonly ChipToggle[] _locationChips;
    private readonly InputBox _fileTypes;
    private readonly ComboBox _position;
    private readonly ToggleSwitch _shift;
    private readonly ToggleSwitch _separatorBefore;
    private readonly ToggleSwitch _separatorAfter;
    private readonly Label _commandPreview;
    private readonly Label _problems;

    private readonly SettingRow _kindRow, _parentRow, _programRow, _argumentsRow, _locationsRow, _fileTypesRow, _positionRow, _shiftRow;

    private readonly ModernButton _saveButton;
    private readonly ModernButton _cancelButton;
    private bool _loading;

    public EntryEditorForm(MenuEntry entry, EntryStore store, bool isNew)
    {
        _entry = entry;
        _submenus = [.. store.Submenus.Where(s => s.Id != entry.Id)];
        _loading = true;

        SuspendLayout();
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(720, 760);
        MinimumSize = new Size(620, 520);
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        Text = isNew ? "New entry" : "Edit entry";
        Icon = Program.AppIcon;
        Theme.ApplyTo(this);

        _heading = new Label { Text = isNew ? "New entry" : "Edit entry", AutoSize = false, ForeColor = Theme.Text, BackColor = Theme.Background };
        _scroll = new Panel { AutoScroll = true, BackColor = Theme.Background };

        _kind = new SegmentedControl("Command", "Submenu") { Width = 200, Height = 32, BackColor = Theme.Surface };
        _kind.SelectedIndex = entry.Kind == EntryKind.Submenu ? 1 : 0;
        _kind.SelectedIndexChanged += (_, _) => UpdateVisibility();

        _text = new InputBox("For example: Open Git Bash here") { Text = entry.Text };

        _parent = MakeCombo();
        _parent.Items.Add("None (top level of the menu)");
        foreach (MenuEntry submenu in _submenus)
        {
            _parent.Items.Add(submenu.Text);
        }

        int parentIndex = _submenus.FindIndex(s => s.Id == entry.ParentId);
        _parent.SelectedIndex = parentIndex + 1;
        _parent.SelectedIndexChanged += (_, _) => UpdateVisibility();

        _program = new InputBox(@"C:\Path\To\Program.exe") { Text = entry.Program, Monospace = true };
        var browseProgram = new ModernButton { Text = "Browse", Size = new Size(90, 32) };
        browseProgram.Click += (_, _) => BrowseProgram();

        _arguments = new InputBox("Optional, for example: \"{path}\"") { Text = entry.Arguments, Monospace = true };
        var insertPath = new ModernButton { Text = "+ {path}", Size = new Size(90, 32) };
        insertPath.Click += (_, _) => InsertPath();

        _icon = new InputBox("Empty uses the program's icon") { Text = entry.Icon };
        _iconPreview = new PictureBox { Size = new Size(24, 24), SizeMode = PictureBoxSizeMode.Zoom, BackColor = Theme.Surface };
        var browseIcon = new ModernButton { Text = "Browse", Size = new Size(90, 32) };
        browseIcon.Click += (_, _) => BrowseIcon();

        _locationChips =
        [
            new ChipToggle("Desktop", MenuLocations.Background, entry.Locations),
            new ChipToggle("Folders", MenuLocations.Folders, entry.Locations),
            new ChipToggle("Files", MenuLocations.Files, entry.Locations),
            new ChipToggle("Drives", MenuLocations.Drives, entry.Locations),
        ];
        var chipPanel = new Panel { Height = 32, BackColor = Theme.Surface };
        int x = 0;
        foreach (ChipToggle chip in _locationChips)
        {
            chip.Location = new Point(x, 0);
            chip.CheckedChanged += (_, _) => UpdateVisibility();
            chipPanel.Controls.Add(chip);
            x += chip.Width + 6;
        }

        chipPanel.Width = x;

        _fileTypes = new InputBox(".txt; .md   (empty = all files)") { Text = entry.FileTypes };

        _position = MakeCombo();
        _position.Items.AddRange(["Default", "Top of the menu", "Bottom of the menu"]);
        _position.SelectedIndex = (int)entry.Position;

        _shift = new ToggleSwitch { Checked = entry.ShiftOnly, BackColor = Theme.Surface };
        _separatorBefore = new ToggleSwitch { Checked = entry.SeparatorBefore, BackColor = Theme.Surface };
        _separatorAfter = new ToggleSwitch { Checked = entry.SeparatorAfter, BackColor = Theme.Surface };

        _commandPreview = new Label
        {
            AutoSize = false,
            ForeColor = Theme.Text,
            BackColor = Theme.Well,
            Padding = new Padding(10, 8, 10, 8),
            AutoEllipsis = true,
        };
        _problems = new Label { AutoSize = false, ForeColor = Theme.TextSecondary, BackColor = Theme.Background };

        _kindRow = Row("Type", "A command runs a program. A submenu groups other entries.", _kind);
        var textRow = Row("Name", "The text you see in the menu", _text, fill: true);
        _parentRow = Row("Inside submenu", "Group it inside a submenu", _parent, fill: true);
        _programRow = Row("Program", "What runs when you click it", _program, fill: true).WithExtras(browseProgram);
        _argumentsRow = Row("Arguments", "{path} = the item you clicked", _arguments, fill: true).WithExtras(insertPath);
        var iconRow = Row("Icon", "An .ico, .exe or .dll file", _icon, fill: true).WithExtras(_iconPreview, browseIcon);
        _locationsRow = Row("Show when you right-click", "Pick one or more places", chipPanel);
        _fileTypesRow = Row("Only these file types", "Leave empty for every file", _fileTypes, fill: true);
        _positionRow = Row("Position", "Where Windows puts it in the menu", _position);
        _shiftRow = Row("Only with Shift", "Hidden unless you hold Shift while right-clicking", _shift);
        Row("Line above", "Draw a separator line above the entry", _separatorBefore);
        Row("Line below", "Draw a separator line below the entry", _separatorAfter);

        foreach (InputBox box in new[] { _text, _program, _arguments, _icon, _fileTypes })
        {
            box.TextChanged += (_, _) => UpdatePreview();
        }

        _icon.TextChanged += (_, _) => UpdateIconPreview();
        _program.TextChanged += (_, _) => UpdateIconPreview();
        foreach (ToggleSwitch toggle in new[] { _shift, _separatorBefore, _separatorAfter })
        {
            toggle.CheckedChanged += (_, _) => UpdatePreview();
        }

        _saveButton = new ModernButton { Kind = ButtonKind.Accent, Text = "Save" };
        _saveButton.Click += (_, _) => Save();
        _cancelButton = new ModernButton { Text = "Cancel", DialogResult = DialogResult.Cancel };
        AcceptButton = _saveButton;
        CancelButton = _cancelButton;

        _scroll.Controls.Add(_commandPreview);
        _scroll.Controls.Add(_problems);
        Controls.AddRange([_heading, _scroll, _saveButton, _cancelButton]);
        ResumeLayout(false);

        _loading = false;
        UpdateVisibility();
        UpdateIconPreview();

        SettingRow Row(string title, string description, Control action, bool fill = false)
        {
            var row = new SettingRow(title, description, action) { LabelWidth = fill ? LabelWidth : 0 };
            _rows.Add(row);
            _scroll.Controls.Add(row);
            return row;
        }
    }

    /// <summary>The edited entry (valid after the dialog returned OK).</summary>
    public MenuEntry Result => _entry;

    private int S(int logical) => LogicalToDeviceUnits(logical);

    private static ComboBox MakeCombo() => new()
    {
        DropDownStyle = ComboBoxStyle.DropDownList,
        FlatStyle = FlatStyle.Flat,
        BackColor = Theme.Surface,
        ForeColor = Theme.Text,
        Width = 220,
    };

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        _heading.Font = Theme.CreateStrongFont(Font, 1.45f);
        _commandPreview.Font = new Font(Theme.MonoFontName, Font.Size * 0.95f);
        _problems.Font = Theme.CreateCaptionFont(Font);
        _text.TextBox.Focus();
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        if (_saveButton is null || _loading)
        {
            return;
        }

        int pad = S(20);
        _heading.Bounds = new Rectangle(pad, pad, ClientSize.Width - 2 * pad, S(36));
        _saveButton.Bounds = new Rectangle(ClientSize.Width - pad - S(110), ClientSize.Height - pad - S(34), S(110), S(34));
        _cancelButton.Bounds = new Rectangle(_saveButton.Left - S(8) - S(110), _saveButton.Top, S(110), S(34));
        _scroll.Bounds = Rectangle.FromLTRB(pad, _heading.Bottom + S(8), ClientSize.Width - pad + S(12), _saveButton.Top - S(12));
        LayoutRows();
    }

    private void LayoutRows()
    {
        int width = _scroll.ClientSize.Width - S(14);
        int y = _scroll.AutoScrollPosition.Y;
        foreach (SettingRow row in _rows.Where(r => r.Visible))
        {
            row.Bounds = new Rectangle(0, y, width, S(60));
            y = row.Bottom + S(4);
        }

        _commandPreview.Bounds = new Rectangle(0, y + S(8), width, S(38));
        _problems.Bounds = new Rectangle(0, _commandPreview.Bottom + S(6), width, S(64));
        _scroll.AutoScrollMinSize = new Size(0, _problems.Bottom - _scroll.AutoScrollPosition.Y + S(4));
    }

    private bool IsSubmenu => _kind.SelectedIndex == 1;

    private bool IsChild => !IsSubmenu && _parent.SelectedIndex > 0;

    private void UpdateVisibility()
    {
        if (_loading)
        {
            return;
        }

        _parentRow.Visible = !IsSubmenu;
        _parent.Enabled = _submenus.Count > 0;
        _parentRow.Description = _submenus.Count > 0 ? "Group it inside a submenu" : "Create a submenu first to group entries";

        // A submenu that already has items can't be turned into an item of another submenu.
        _kind.Enabled = !IsChild;

        _programRow.Visible = _argumentsRow.Visible = !IsSubmenu;
        _locationsRow.Visible = _positionRow.Visible = _shiftRow.Visible = !IsChild;
        _fileTypesRow.Visible = !IsChild && _locationChips[2].Checked;

        LayoutRows();
        UpdatePreview();
        Invalidate(true);
    }

    private void ApplyToEntry(MenuEntry entry)
    {
        entry.Kind = IsSubmenu ? EntryKind.Submenu : EntryKind.Command;
        entry.Text = _text.Text.Trim();
        entry.Program = IsSubmenu ? string.Empty : _program.Text.Trim();
        entry.Arguments = IsSubmenu ? string.Empty : _arguments.Text.Trim();
        entry.Icon = _icon.Text.Trim();
        entry.FileTypes = _fileTypes.Text.Trim();
        entry.Position = (MenuPosition)Math.Max(0, _position.SelectedIndex);
        entry.ShiftOnly = _shift.Checked;
        entry.SeparatorBefore = _separatorBefore.Checked;
        entry.SeparatorAfter = _separatorAfter.Checked;

        if (IsChild)
        {
            MenuEntry parent = _submenus[_parent.SelectedIndex - 1];
            entry.ParentId = parent.Id;
            entry.Locations = parent.Locations;
            entry.ShiftOnly = false;
            entry.Position = MenuPosition.Default;
        }
        else
        {
            entry.ParentId = null;
            entry.Locations = _locationChips.Where(c => c.Checked).Aggregate(MenuLocations.None, (all, c) => all | c.Place);
        }
    }

    private void UpdatePreview()
    {
        if (_loading)
        {
            return;
        }

        var draft = _entry.Clone();
        ApplyToEntry(draft);

        if (draft.Kind == EntryKind.Submenu)
        {
            _commandPreview.Text = "A submenu doesn't run anything. Add items to it from the list.";
        }
        else if (string.IsNullOrWhiteSpace(draft.Program))
        {
            _commandPreview.Text = "Windows will run: (choose a program)";
        }
        else
        {
            MenuLocations first = draft.Locations == MenuLocations.None ? MenuLocations.Background
                : Enum.GetValues<MenuLocations>().First(l => l != MenuLocations.None && draft.Locations.HasFlag(l));
            _commandPreview.Text = "Windows will run:  " + CommandBuilder.BuildRegistryCommand(draft, first);
        }

        IReadOnlyList<string> problems = EntryValidator.Validate(draft);
        IReadOnlyList<string> warnings = EntryValidator.Warnings(draft);
        _problems.ForeColor = problems.Count > 0 ? Color.FromArgb(Theme.IsDark ? 0xFF : 0xC4, Theme.IsDark ? 0x99 : 0x2B, Theme.IsDark ? 0xA4 : 0x1C) : Theme.TextSecondary;
        _problems.Text = string.Join("\n", problems.Concat(warnings.Select(w => "Note: " + w)));
    }

    private void UpdateIconPreview()
    {
        var draft = new MenuEntry { Program = _program.Text, Icon = _icon.Text };
        _iconPreview.Image = IconLoader.Load(draft.EffectiveIcon, S(24));
    }

    private void Save()
    {
        ApplyToEntry(_entry);
        IReadOnlyList<string> problems = EntryValidator.Validate(_entry);
        if (problems.Count > 0)
        {
            _problems.Text = string.Join("\n", problems);
            System.Media.SystemSounds.Beep.Play();
            return;
        }

        DialogResult = DialogResult.OK;
        Close();
    }

    private void BrowseProgram()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Choose a program",
            Filter = "Programs (*.exe;*.bat;*.cmd)|*.exe;*.bat;*.cmd|All files (*.*)|*.*",
        };

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _program.Text = dialog.FileName;
            if (string.IsNullOrWhiteSpace(_text.Text))
            {
                _text.Text = "Open with " + Path.GetFileNameWithoutExtension(dialog.FileName);
            }

            if (string.IsNullOrWhiteSpace(_arguments.Text))
            {
                _arguments.Text = "\"{path}\"";
            }
        }
    }

    private void BrowseIcon()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Choose an icon",
            Filter = "Icons and programs (*.ico;*.exe;*.dll)|*.ico;*.exe;*.dll|All files (*.*)|*.*",
        };

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _icon.Text = dialog.FileName;
        }
    }

    private void InsertPath()
    {
        TextBox box = _arguments.TextBox;
        string token = "\"" + CommandBuilder.PathToken + "\"";
        int start = box.SelectionStart;
        box.Text = box.Text.Remove(start, box.SelectionLength).Insert(start, token);
        box.SelectionStart = start + token.Length;
        box.Focus();
    }

    /// <summary>A pill-shaped on/off button for the location choices.</summary>
    private sealed class ChipToggle : ModernButton
    {
        private bool _checked;

        public ChipToggle(string text, MenuLocations location, MenuLocations current)
        {
            Text = text;
            Place = location;
            Size = new Size(84, 32);
            Checked = current.HasFlag(location);
            Click += (_, _) => Checked = !Checked;
        }

        public event EventHandler? CheckedChanged;

        public MenuLocations Place { get; }

        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public bool Checked
        {
            get => _checked;
            set
            {
                _checked = value;
                Kind = value ? ButtonKind.Accent : ButtonKind.Standard;
                Glyph = value ? Glyphs.CheckMark : null;
                Invalidate();
                CheckedChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }
}
