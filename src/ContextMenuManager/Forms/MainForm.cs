using System.Diagnostics;
using System.Text.Json;
using ContextMenuManager.Core;
using ContextMenuManager.Interop;
using ContextMenuManager.Preview;
using ContextMenuManager.UI;
using ContextMenuManager.UI.Controls;

namespace ContextMenuManager;

/// <summary>The live preview on the left; the user's entries on the right.</summary>
internal sealed class MainForm : Form
{
    private readonly AppServices _services;
    private readonly PreviewCard _preview;
    private readonly Label _title;
    private readonly ModernButton _settingsButton;
    private readonly ModernButton _moreButton;
    private readonly InputBox _search;
    private readonly EntryListBox _list;
    private readonly Label _empty;
    private readonly Label _status;
    private readonly ModernButton _templatesButton;
    private readonly ModernButton _newButton;
    private readonly ToolTip _toolTip;
    private readonly ContextMenuStrip _entryMenu;
    private readonly ContextMenuStrip _moreMenu;
    private readonly Dictionary<PreviewTarget, IReadOnlyList<ExistingVerb>> _existingVerbs = [];

    private MenuEntry? _clickedEntry;
    private PreviewTarget _clickedTarget;
    private bool _updatingList;

    public MainForm(AppServices services)
    {
        _services = services;
        SuspendLayout();

        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(1180, 700);
        MinimumSize = new Size(900, 560);
        StartPosition = FormStartPosition.CenterScreen;
        Text = services.IsSandbox ? "Context Menu Manager (sandbox: the real menu is not changed)" : "Context Menu Manager";
        Icon = Program.AppIcon;
        KeyPreview = true;
        Theme.ApplyTo(this);

        _toolTip = new ToolTip();

        _preview = new PreviewCard();
        _preview.Scene.ClassicMenuProvider = BuildClassicMenu;
        _preview.Scene.ItemClicked += Scene_ItemClicked;
        _preview.Scene.Banner.RunButton.Click += (_, _) => RunClickedEntry();
        _preview.StyleSelector.SelectedIndex = InitialStyleIndex();
        _preview.StyleSelector.SelectedIndexChanged += (_, _) => PreviewStyleChanged();
        _preview.ShiftToggle.CheckedChanged += (_, _) =>
        {
            _preview.Scene.ForceShift = _preview.ShiftToggle.Checked;
            ShowSelectedInPreview();
        };
        _toolTip.SetToolTip(_preview.ShiftToggle, "Show entries that only appear while Shift is held");
        ApplyPreviewStyle();

        _title = new Label
        {
            Text = "Menu entries",
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Theme.Text,
            BackColor = Theme.Background,
        };

        _settingsButton = new ModernButton { Kind = ButtonKind.Subtle, Glyph = Glyphs.Settings, TabStop = false, AccessibleName = "Settings" };
        _settingsButton.Click += (_, _) => ShowSettings();
        _toolTip.SetToolTip(_settingsButton, "Settings");

        _moreButton = new ModernButton { Kind = ButtonKind.Subtle, Glyph = Glyphs.More, TabStop = false, AccessibleName = "More options" };
        _moreButton.Click += (_, _) => _moreMenu!.Show(_moreButton, new Point(0, _moreButton.Height));
        _toolTip.SetToolTip(_moreButton, "Import, export and more");

        _search = new InputBox("Search entries", Glyphs.Search);
        _search.TextChanged += (_, _) => RefreshList();
        _search.TextBox.KeyDown += Search_KeyDown;

        _list = new EntryListBox { AccessibleName = "Menu entries" };
        _list.SelectedIndexChanged += (_, _) =>
        {
            if (!_updatingList)
            {
                ShowSelectedInPreview();
            }
        };
        _list.ToggleClicked += (_, entry) => ToggleEntry(entry);
        _list.MouseDoubleClick += List_MouseDoubleClick;
        _list.KeyDown += List_KeyDown;

        _empty = new Label
        {
            Text = "No entries yet.\n\nStart with a template such as “Open Git Bash here”, or create your own entry.",
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = Theme.TextSecondary,
            BackColor = Theme.Background,
            Visible = false,
        };

        _status = new Label
        {
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Theme.TextSecondary,
            BackColor = Theme.Background,
            AutoEllipsis = true,
        };

        _templatesButton = new ModernButton { Glyph = Glyphs.Templates, Text = "Templates" };
        _templatesButton.Click += (_, _) => ShowTemplates();
        _toolTip.SetToolTip(_templatesButton, "Add a ready-made entry (Ctrl+T)");

        _newButton = new ModernButton { Kind = ButtonKind.Accent, Glyph = Glyphs.Add, Text = "New entry" };
        _newButton.Click += (_, _) => NewEntry();
        _toolTip.SetToolTip(_newButton, "Create your own entry (Ctrl+N)");

        _entryMenu = BuildEntryMenu();
        _list.ContextMenuStrip = _entryMenu;
        _moreMenu = BuildMoreMenu();

        Controls.AddRange([_preview, _title, _settingsButton, _moreButton, _search, _empty, _list, _status, _templatesButton, _newButton]);
        ResumeLayout(false);
    }

    private int S(int logical) => LogicalToDeviceUnits(logical);

    private EntryStore Store => _services.Store;

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        _title.Font = Theme.CreateStrongFont(Font, 1.45f);
        _status.Font = Theme.CreateCaptionFont(Font);
        _empty.Font = Theme.CreateCaptionFont(Font);

        RefreshList();
        if (_list.Items.Count > 0)
        {
            _list.SelectedIndex = 0;
        }

        if (Store.LoadError is { } error)
        {
            ShowError("Your saved entries couldn't be read",
                $"A backup of the file was kept in the data folder and the app started with an empty list.\n\n{error}");
        }

        _list.Focus();
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        if (_newButton is null)
        {
            return; // Called during construction.
        }

        Size client = ClientSize;
        int pad = S(16);
        int gap = S(16);
        int sidebarWidth = S(360);
        int sidebarX = client.Width - pad - sidebarWidth;
        int iconButton = S(34);

        _preview.Bounds = new Rectangle(pad, pad, Math.Max(0, sidebarX - gap - pad), Math.Max(0, client.Height - 2 * pad));

        _title.Bounds = new Rectangle(sidebarX, pad, sidebarWidth - 2 * iconButton - S(4), iconButton);
        _moreButton.Bounds = new Rectangle(sidebarX + sidebarWidth - iconButton, pad, iconButton, iconButton);
        _settingsButton.Bounds = new Rectangle(_moreButton.Left - S(4) - iconButton, pad, iconButton, iconButton);

        _search.Bounds = new Rectangle(sidebarX, _title.Bottom + S(10), sidebarWidth, S(34));

        int buttonWidth = (sidebarWidth - S(8)) / 2;
        _templatesButton.Bounds = new Rectangle(sidebarX, client.Height - pad - S(38), buttonWidth, S(38));
        _newButton.Bounds = new Rectangle(sidebarX + sidebarWidth - buttonWidth, _templatesButton.Top, buttonWidth, S(38));
        _status.Bounds = new Rectangle(sidebarX + S(4), _templatesButton.Top - S(8) - S(20), sidebarWidth - S(4), S(20));

        int listTop = _search.Bottom + S(8);
        _list.Bounds = new Rectangle(sidebarX, listTop, sidebarWidth, Math.Max(0, _status.Top - S(6) - listTop));
        _empty.Bounds = Rectangle.Inflate(_list.Bounds, -S(16), 0);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        switch (keyData)
        {
            case Keys.Control | Keys.N:
                NewEntry();
                return true;
            case Keys.Control | Keys.T:
                ShowTemplates();
                return true;
            case Keys.Control | Keys.F:
                _search.TextBox.Focus();
                _search.TextBox.SelectAll();
                return true;
            case Keys.F5:
                _existingVerbs.Clear();
                IconLoader.Clear();
                ShowSelectedInPreview();
                return true;
            default:
                return base.ProcessCmdKey(ref msg, keyData);
        }
    }

    public void BringToFrontFromOtherInstance()
    {
        if (WindowState == FormWindowState.Minimized)
        {
            WindowState = FormWindowState.Normal;
        }

        Activate();
        NativeMethods.SetForegroundWindow(Handle);
    }

    // ---------------------------------------------------------------- list

    private void RefreshList(MenuEntry? select = null)
    {
        Guid? selectedId = select?.Id ?? _list.SelectedEntry?.Id;
        string filter = _search.Text.Trim();

        List<MenuEntry> items = [.. Store.InDisplayOrder()];
        if (filter.Length > 0)
        {
            items = [.. items.Where(e => Matches(e, filter) || e.IsChild && Store.Find(e.ParentId!.Value) is { } p && Matches(p, filter))];
        }

        _updatingList = true;
        try
        {
            _list.BeginUpdate();
            _list.Items.Clear();
            _list.Items.AddRange([.. items]);
            int index = items.FindIndex(e => e.Id == selectedId);
            _list.SelectedIndex = index;
            _list.EndUpdate();
        }
        finally
        {
            _updatingList = false;
        }

        _empty.Visible = Store.Entries.Count == 0;
        if (_empty.Visible)
        {
            _empty.BringToFront();
        }

        UpdateStatus();
        if (select is not null)
        {
            ShowSelectedInPreview();
        }
    }

    private static bool Matches(MenuEntry entry, string filter) =>
        entry.Text.Contains(filter, StringComparison.CurrentCultureIgnoreCase)
        || entry.Program.Contains(filter, StringComparison.CurrentCultureIgnoreCase);

    private void UpdateStatus(string? message = null)
    {
        if (message is not null)
        {
            _status.Text = message;
            return;
        }

        int total = Store.Entries.Count;
        int active = Store.Entries.Count(e => e.Enabled && (!e.IsChild || Store.Find(e.ParentId!.Value)?.Enabled == true));
        _status.Text = total switch
        {
            0 => "Your menu has no custom entries",
            1 => active == 1 ? "1 entry, in your menu" : "1 entry, turned off",
            _ => $"{total} entries  ·  {active} in your menu",
        };

        if (_services.IsSandbox)
        {
            _status.Text += "  ·  sandbox";
        }
    }

    private void ShowSelectedInPreview()
    {
        _preview.Scene.Banner.Visible = false;
        if (_list.SelectedEntry is { } entry)
        {
            MenuEntry? parent = entry.ParentId is { } id ? Store.Find(id) : null;
            _preview.Scene.ShowEntry(entry, parent);
        }
        else
        {
            _preview.Scene.CloseMenus();
        }
    }

    // ---------------------------------------------------------------- changes

    /// <summary>Saves the list and writes it to the registry. Returns false (after telling the user) on errors.</summary>
    private bool Commit(MenuEntry? select = null)
    {
        bool ok = true;
        try
        {
            Store.Save();
            _services.Writer.Apply(Store.Entries);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            ok = false;
            ShowError("The change couldn't be saved", ex.Message);
        }

        RefreshList(select);
        return ok;
    }

    private void NewEntry()
    {
        MenuEntry? parent = _list.SelectedEntry is { } selected
            ? selected.Kind == EntryKind.Submenu && !selected.IsChild ? selected : selected.ParentId is { } p ? Store.Find(p) : null
            : null;

        var entry = new MenuEntry { Text = string.Empty };
        if (parent is not null)
        {
            entry.ParentId = parent.Id;
            entry.Locations = parent.Locations;
        }

        using var editor = new EntryEditorForm(entry, Store, isNew: true);
        if (editor.ShowDialog(this) == DialogResult.OK)
        {
            Insert(editor.Result);
            Commit(editor.Result);
        }
    }

    private void Insert(MenuEntry entry)
    {
        if (entry.ParentId is { } parentId)
        {
            int last = Store.Entries.FindLastIndex(e => e.Id == parentId || e.ParentId == parentId);
            Store.Entries.Insert(last + 1, entry);
        }
        else
        {
            Store.Entries.Add(entry);
        }
    }

    private void EditEntry(MenuEntry entry)
    {
        using var editor = new EntryEditorForm(entry.Clone(), Store, isNew: false);
        if (editor.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        MenuEntry result = editor.Result;
        int index = Store.Entries.FindIndex(e => e.Id == entry.Id);
        if (index < 0)
        {
            return;
        }

        if (result.ParentId != entry.ParentId)
        {
            // Moved into or out of a submenu: put it at the end of its new place.
            Store.Entries.RemoveAt(index);
            Insert(result);
        }
        else
        {
            Store.Entries[index] = result;
        }

        if (result.Kind == EntryKind.Command && entry.Kind == EntryKind.Submenu)
        {
            // A submenu that became a command can't keep children: move them to the top level.
            foreach (MenuEntry child in Store.Entries.Where(e => e.ParentId == result.Id))
            {
                child.ParentId = null;
                child.Locations = result.Locations;
            }
        }

        foreach (MenuEntry child in Store.Entries.Where(e => e.ParentId == result.Id))
        {
            child.Locations = result.Locations;
        }

        Commit(result);
    }

    private void DuplicateEntry(MenuEntry entry)
    {
        MenuEntry copy = entry.Clone();
        copy.Id = Guid.NewGuid();
        copy.Text = entry.Text + " (copy)";
        int index = Store.Entries.FindIndex(e => e.Id == entry.Id);
        Store.Entries.Insert(index + 1 + (entry.Kind == EntryKind.Submenu ? Store.ChildrenOf(entry).Count() : 0), copy);

        if (entry.Kind == EntryKind.Submenu)
        {
            int insertAt = Store.Entries.IndexOf(copy) + 1;
            foreach (MenuEntry child in Store.ChildrenOf(entry).ToList())
            {
                MenuEntry childCopy = child.Clone();
                childCopy.Id = Guid.NewGuid();
                childCopy.ParentId = copy.Id;
                Store.Entries.Insert(insertAt++, childCopy);
            }
        }

        Commit(copy);
    }

    private void DeleteEntry(MenuEntry entry)
    {
        int children = Store.ChildrenOf(entry).Count();
        var page = new TaskDialogPage
        {
            Caption = "Context Menu Manager",
            Heading = $"Delete “{entry.Text}”?",
            Text = children > 0
                ? $"The submenu and the {children} item(s) inside it are removed from the app and from the Windows menu."
                : "The entry is removed from the app and from the Windows menu.",
            Icon = TaskDialogIcon.Warning,
            Buttons = { new TaskDialogButton("Delete"), TaskDialogButton.Cancel },
            DefaultButton = TaskDialogButton.Cancel,
        };

        if (TaskDialog.ShowDialog(this, page) == TaskDialogButton.Cancel)
        {
            return;
        }

        int index = _list.SelectedIndex;
        Store.Remove(entry);
        Commit();
        if (_list.Items.Count > 0)
        {
            _list.SelectedIndex = Math.Clamp(index, 0, _list.Items.Count - 1);
        }
    }

    private void ToggleEntry(MenuEntry entry)
    {
        entry.Enabled = !entry.Enabled;
        Commit(entry);
        UpdateStatus(entry.Enabled ? $"“{entry.Text}” is now in your menu" : $"“{entry.Text}” was taken out of your menu");
    }

    private void MoveEntry(MenuEntry entry, int direction)
    {
        if (Store.Move(entry, direction))
        {
            Commit(entry);
        }
    }

    private void TestRun(MenuEntry entry)
    {
        if (entry.Kind != EntryKind.Command)
        {
            return;
        }

        MenuEntry placement = entry.ParentId is { } id && Store.Find(id) is { } parent ? parent : entry;
        _clickedEntry = entry;
        _clickedTarget = PreviewMenuBuilder.FromLocation(placement.Locations);
        RunClickedEntry();
    }

    // ---------------------------------------------------------------- preview

    private int InitialStyleIndex() => _services.Settings.PreviewStyle switch
    {
        PreviewStyleSetting.Windows11 => 0,
        PreviewStyleSetting.Windows10 => 1,
        _ => ClassicContextMenu.IsWindows11 ? 0 : 1,
    };

    private void PreviewStyleChanged()
    {
        _services.Settings.PreviewStyle = _preview.StyleSelector.SelectedIndex == 0 ? PreviewStyleSetting.Windows11 : PreviewStyleSetting.Windows10;
        _services.Settings.Save();
        ApplyPreviewStyle();
        ShowSelectedInPreview();
    }

    private void ApplyPreviewStyle()
    {
        bool windows11 = _preview.StyleSelector.SelectedIndex == 0;

        // With the "full context menu" setting on, Windows 11 opens the classic menu right away.
        // The sandbox always shows a default Windows 11, so contributors can see both menus.
        bool fullMenu = ClassicContextMenu.IsWindows11 && ClassicContextMenu.IsEnabled && !_services.IsSandbox;
        _preview.Scene.Style = !windows11 ? MenuStyle.Classic10 : fullMenu ? MenuStyle.Classic11 : MenuStyle.Modern;
    }

    private PreviewMenu BuildClassicMenu(PreviewTarget target, bool shift, MenuEntry? highlight, MenuStyle style)
    {
        if (!_existingVerbs.TryGetValue(target, out IReadOnlyList<ExistingVerb>? existing))
        {
            existing = ShellVerbReader.Read(PreviewMenuBuilder.ToLocation(target), ".txt");
            _existingVerbs[target] = existing;
        }

        return PreviewMenuBuilder.BuildClassic(target, style, shift, Store.Entries, existing, highlight);
    }

    private void Scene_ItemClicked(object? sender, PreviewClickEventArgs e)
    {
        ResultBanner banner = _preview.Scene.Banner;
        PreviewItem item = e.Item;
        _clickedEntry = null;

        switch (item.Source)
        {
            case PreviewItemSource.Mine when item.Entry is { } entry:
                _clickedEntry = entry;
                _clickedTarget = e.Target;
                string note = entry.Enabled ? string.Empty : " This entry is turned off, so it is not in your real menu yet.";
                banner.Show(
                    $"You clicked “{entry.Text}”",
                    "In the real menu, Windows would now run this command, with the item you clicked filled in:" + note,
                    CommandRunner.BuildTestCommand(entry, e.Target),
                    canRun: true);
                break;

            case PreviewItemSource.OtherApp:
                string path = PreviewMenuBuilder.SamplePath(e.Target);
                string command = string.IsNullOrEmpty(item.Command) ? string.Empty
                    : System.Text.RegularExpressions.Regex.Replace(item.Command, "%[1vVlL]", path.Replace("$", "$$"));
                banner.Show(
                    $"“{item.Text}” was added by another program",
                    "Context Menu Manager never changes items that other programs added. In the real menu it would run:",
                    command.Length > 0 ? command : null,
                    canRun: false);
                break;

            default:
                banner.Show(
                    $"“{item.Text}” is a Windows item",
                    "Built-in items do their usual job in the real menu. Here in the preview they don't do anything.",
                    null,
                    canRun: false);
                break;
        }

        _preview.Scene.ShowBanner();
    }

    private void RunClickedEntry()
    {
        if (_clickedEntry is not { } entry)
        {
            return;
        }

        string command = CommandRunner.BuildTestCommand(entry, _clickedTarget);
        if (CommandRunner.LooksRisky(command))
        {
            var page = new TaskDialogPage
            {
                Caption = "Context Menu Manager",
                Heading = "Run this command for real?",
                Text = $"It looks like it could close programs, sign you out, restart the PC or delete something:\n\n{command}",
                Icon = TaskDialogIcon.Warning,
                Buttons = { new TaskDialogButton("Run it"), TaskDialogButton.Cancel },
                DefaultButton = TaskDialogButton.Cancel,
            };

            if (TaskDialog.ShowDialog(this, page) == TaskDialogButton.Cancel)
            {
                return;
            }
        }

        if (CommandRunner.Run(entry, _clickedTarget) is { } error)
        {
            ShowError("The test run didn't start", error);
        }
        else
        {
            UpdateStatus($"Ran “{entry.Text}”");
        }
    }

    // ---------------------------------------------------------------- menus and dialogs

    private ContextMenuStrip BuildEntryMenu()
    {
        var menu = new ContextMenuStrip();
        ThemedMenuRenderer.Apply(menu);

        var edit = new ToolStripMenuItem("Edit...", null, (_, _) => Selected(EditEntry)) { ShortcutKeyDisplayString = "Enter" };
        edit.Font = new Font(edit.Font, FontStyle.Bold);
        var show = new ToolStripMenuItem("Show in preview", null, (_, _) => ShowSelectedInPreview());
        var run = new ToolStripMenuItem("Test run", null, (_, _) => Selected(TestRun));
        var toggle = new ToolStripMenuItem("Turn off", null, (_, _) => Selected(ToggleEntry)) { ShortcutKeyDisplayString = "Space" };
        var duplicate = new ToolStripMenuItem("Duplicate", null, (_, _) => Selected(DuplicateEntry));
        var up = new ToolStripMenuItem("Move up", null, (_, _) => Selected(e => MoveEntry(e, -1))) { ShortcutKeyDisplayString = "Alt+Up" };
        var down = new ToolStripMenuItem("Move down", null, (_, _) => Selected(e => MoveEntry(e, 1))) { ShortcutKeyDisplayString = "Alt+Down" };
        var addChild = new ToolStripMenuItem("Add item to submenu...", null, (_, _) => NewEntry());
        var delete = new ToolStripMenuItem("Delete", null, (_, _) => Selected(DeleteEntry)) { ShortcutKeyDisplayString = "Del" };

        menu.Items.AddRange([edit, show, run, toggle, new ToolStripSeparator(), addChild, duplicate, up, down, new ToolStripSeparator(), delete]);
        menu.Opening += (_, e) =>
        {
            if (_list.SelectedEntry is not { } entry)
            {
                e.Cancel = true;
                return;
            }

            toggle.Text = entry.Enabled ? "Turn off" : "Turn on";
            run.Enabled = entry.Kind == EntryKind.Command;
            addChild.Visible = entry.Kind == EntryKind.Submenu;
        };

        return menu;
    }

    private ContextMenuStrip BuildMoreMenu()
    {
        var menu = new ContextMenuStrip();
        ThemedMenuRenderer.Apply(menu);
        menu.Items.AddRange(
        [
            new ToolStripMenuItem("Import entries...", null, (_, _) => Import()),
            new ToolStripMenuItem("Export entries...", null, (_, _) => Export()),
            new ToolStripSeparator(),
            new ToolStripMenuItem("Open data folder", null, (_, _) => OpenDataFolder()),
            new ToolStripMenuItem("Remove everything this app added...", null, (_, _) => RemoveAll()),
            new ToolStripSeparator(),
            new ToolStripMenuItem("Help and source code", null, (_, _) => OpenUrl(Program.RepositoryUrl)),
        ]);
        return menu;
    }

    private void Selected(Action<MenuEntry> action)
    {
        if (_list.SelectedEntry is { } entry)
        {
            action(entry);
        }
    }

    private void ShowTemplates()
    {
        using var templates = new TemplatesForm(entries =>
        {
            foreach (MenuEntry entry in entries)
            {
                Insert(entry);
            }

            Commit(entries[0]);
        });
        templates.ShowDialog(this);
    }

    private void ShowSettings()
    {
        using var settings = new SettingsForm(_services, RemoveAll);
        settings.ShowDialog(this);
        ApplyPreviewStyle();
        ShowSelectedInPreview();
    }

    private void Import()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Import entries",
            Filter = "Context Menu Manager files (*.json)|*.json|All files (*.*)|*.*",
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        List<MenuEntry> entries;
        try
        {
            var info = new FileInfo(dialog.FileName);
            if (info.Length > 5 * 1024 * 1024)
            {
                throw new InvalidDataException("The file is too large to be an entry list.");
            }

            entries = EntryStore.PrepareImport(EntryStore.Deserialize(File.ReadAllText(dialog.FileName)).Entries);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            ShowError("The file couldn't be imported", ex.Message);
            return;
        }

        if (entries.Count == 0)
        {
            ShowError("Nothing to import", "The file doesn't contain any entries.");
            return;
        }

        // Show every command before anything is added: imported files can come from anyone.
        string commands = string.Join("\n", entries.Take(15).Select(e =>
            e.Kind == EntryKind.Submenu ? $"• {e.Text} (submenu)" : $"• {e.Text}:  {CommandBuilder.BuildRegistryCommand(e, MenuLocations.Folders)}"));
        if (entries.Count > 15)
        {
            commands += $"\n… and {entries.Count - 15} more";
        }

        var page = new TaskDialogPage
        {
            Caption = "Context Menu Manager",
            Heading = $"Import {entries.Count} entries?",
            Text = "Only import files you trust. Check what each entry runs:\n\n" + commands
                + "\n\nImported entries are added turned off. Turn them on after you've checked them.",
            Icon = TaskDialogIcon.ShieldWarningYellowBar,
            Buttons = { new TaskDialogButton("Import"), TaskDialogButton.Cancel },
            DefaultButton = TaskDialogButton.Cancel,
        };

        if (TaskDialog.ShowDialog(this, page) == TaskDialogButton.Cancel)
        {
            return;
        }

        Store.Entries.AddRange(entries);
        Commit(entries[0]);
    }

    private void Export()
    {
        if (Store.Entries.Count == 0)
        {
            ShowError("Nothing to export", "Add some entries first.");
            return;
        }

        using var dialog = new SaveFileDialog
        {
            Title = "Export entries",
            Filter = "Context Menu Manager files (*.json)|*.json",
            FileName = "context-menu-entries.json",
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            File.WriteAllText(dialog.FileName, EntryStore.Serialize(Store.Entries));
            UpdateStatus("Entries exported");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowError("The entries couldn't be exported", ex.Message);
        }
    }

    private void RemoveAll()
    {
        var page = new TaskDialogPage
        {
            Caption = "Context Menu Manager",
            Heading = "Remove everything this app added?",
            Text = "All your entries are taken out of the Windows context menu. Your list stays in the app with every entry turned off, so you can bring them back later.\n\nItems added by Windows or other programs are not touched.\n\nTip: do this before you delete the app.",
            Icon = TaskDialogIcon.Warning,
            Buttons = { new TaskDialogButton("Remove"), TaskDialogButton.Cancel },
            DefaultButton = TaskDialogButton.Cancel,
        };

        if (TaskDialog.ShowDialog(this, page) == TaskDialogButton.Cancel)
        {
            return;
        }

        try
        {
            foreach (MenuEntry entry in Store.Entries)
            {
                entry.Enabled = false;
            }

            Store.Save();
            int removed = _services.Writer.RemoveAll();
            RefreshList();
            UpdateStatus(removed == 1 ? "Removed 1 item from the Windows menu" : $"Removed {removed} items from the Windows menu");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            ShowError("Not everything could be removed", ex.Message);
        }
    }

    private void OpenDataFolder()
    {
        Directory.CreateDirectory(Store.DataDirectory);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{Store.DataDirectory}\"") { UseShellExecute = true })?.Dispose();
    }

    public static void OpenUrl(string url) =>
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })?.Dispose();

    private void ShowError(string heading, string text) => TaskDialog.ShowDialog(this, new TaskDialogPage
    {
        Caption = "Context Menu Manager",
        Heading = heading,
        Text = text,
        Icon = TaskDialogIcon.Error,
    });

    // ---------------------------------------------------------------- keyboard and mouse

    private void List_MouseDoubleClick(object? sender, MouseEventArgs e)
    {
        int index = _list.IndexFromPoint(e.Location);
        if (e.Button == MouseButtons.Left && index >= 0 && _list.Items[index] is MenuEntry entry
            && e.X < _list.Width - S(64))
        {
            EditEntry(entry);
        }
    }

    private void List_KeyDown(object? sender, KeyEventArgs e)
    {
        if (_list.SelectedEntry is not { } entry)
        {
            return;
        }

        switch (e.KeyData)
        {
            case Keys.Enter:
                EditEntry(entry);
                break;
            case Keys.Delete:
                DeleteEntry(entry);
                break;
            case Keys.Space:
                ToggleEntry(entry);
                break;
            case Keys.Alt | Keys.Up:
                MoveEntry(entry, -1);
                break;
            case Keys.Alt | Keys.Down:
                MoveEntry(entry, 1);
                break;
            default:
                return;
        }

        e.Handled = e.SuppressKeyPress = true;
    }

    private void Search_KeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.KeyCode)
        {
            case Keys.Escape:
                _search.Text = string.Empty;
                _list.Focus();
                e.SuppressKeyPress = true;
                break;
            case Keys.Down or Keys.Up when _list.Items.Count > 0:
                int next = _list.SelectedIndex + (e.KeyCode == Keys.Down ? 1 : -1);
                _list.SelectedIndex = Math.Clamp(next, 0, _list.Items.Count - 1);
                e.SuppressKeyPress = true;
                break;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _toolTip.Dispose();
            _entryMenu.Dispose();
            _moreMenu.Dispose();
        }

        base.Dispose(disposing);
    }
}
