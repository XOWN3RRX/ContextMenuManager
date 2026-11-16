using ContextMenuManager.Core;
using ContextMenuManager.UI;

namespace ContextMenuManager.Preview;

/// <summary>What was right-clicked in the preview.</summary>
internal enum PreviewTarget
{
    Background,
    Folder,
    File,
    Drive,
}

internal enum MenuStyle
{
    /// <summary>The Windows 11 compact menu with "Show more options".</summary>
    Modern,

    /// <summary>The full menu, drawn like Windows 11 ("Show more options").</summary>
    Classic11,

    /// <summary>The full menu, drawn like Windows 10.</summary>
    Classic10,
}

internal enum PreviewItemSource
{
    BuiltIn,
    OtherApp,
    Mine,
    ShowMoreOptions,
    Placeholder,
}

/// <summary>One row of a simulated menu.</summary>
internal sealed class PreviewItem
{
    public static PreviewItem Separator => new() { IsSeparator = true };

    public string Text { get; init; } = string.Empty;

    /// <summary>A Fluent glyph drawn as the icon (built-in items).</summary>
    public string? Glyph { get; init; }

    /// <summary>An icon location ("file,index") drawn as the icon (registry items).</summary>
    public string? IconLocation { get; init; }

    public bool IsSeparator { get; init; }

    public bool Disabled { get; init; }

    public bool Bold { get; init; }

    public List<PreviewItem>? Children { get; init; }

    public PreviewItemSource Source { get; init; }

    public MenuEntry? Entry { get; init; }

    /// <summary>For items from other programs: the command they registered.</summary>
    public string? Command { get; init; }

    /// <summary>Drawn with an accent outline, to point out the selected entry.</summary>
    public bool Highlight { get; set; }

    public bool HasChildren => Children is not null;
}

/// <summary>The item to point out in the preview: one of the user's entries, or another program's item by key name.</summary>
internal readonly record struct PreviewHighlight(MenuEntry? Entry, string? VerbKey);

internal sealed class PreviewMenu
{
    public List<PreviewItem> Items { get; } = [];

    /// <summary>The icon row at the top of the Windows 11 menu (cut, copy, rename...).</summary>
    public List<PreviewItem> ToolbarItems { get; } = [];

    public MenuStyle Style { get; init; }
}

/// <summary>Builds the simulated menus: Windows' own items, other programs' items and the user's entries.</summary>
internal static class PreviewMenuBuilder
{
    public static string SamplePath(PreviewTarget target) => target switch
    {
        PreviewTarget.Background => Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
        PreviewTarget.Folder => Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        PreviewTarget.File => Path.Combine(Path.GetTempPath(), "ContextMenuManager", "Notes.txt"),
        _ => Path.GetPathRoot(Environment.SystemDirectory) ?? @"C:\",
    };

    public static MenuLocations ToLocation(PreviewTarget target) => target switch
    {
        PreviewTarget.Background => MenuLocations.Background,
        PreviewTarget.Folder => MenuLocations.Folders,
        PreviewTarget.File => MenuLocations.Files,
        _ => MenuLocations.Drives,
    };

    public static PreviewTarget FromLocation(MenuLocations locations) =>
        locations.HasFlag(MenuLocations.Background) ? PreviewTarget.Background
        : locations.HasFlag(MenuLocations.Folders) ? PreviewTarget.Folder
        : locations.HasFlag(MenuLocations.Files) ? PreviewTarget.File
        : locations.HasFlag(MenuLocations.Drives) ? PreviewTarget.Drive
        : PreviewTarget.Background;

    /// <summary>The Windows 11 compact menu. The app's entries are never here; they are under "Show more options".</summary>
    public static PreviewMenu BuildModern(PreviewTarget target)
    {
        var menu = new PreviewMenu { Style = MenuStyle.Modern };

        if (target != PreviewTarget.Background)
        {
            menu.ToolbarItems.AddRange(
            [
                BuiltIn("Cut", Glyphs.Cut, disabled: target == PreviewTarget.Drive),
                BuiltIn("Copy", Glyphs.Copy),
                BuiltIn("Rename", Glyphs.Rename),
                BuiltIn("Share", Glyphs.Share, disabled: target != PreviewTarget.File),
                BuiltIn("Delete", Glyphs.Delete, disabled: target == PreviewTarget.Drive),
            ]);
        }

        menu.Items.AddRange(target switch
        {
            PreviewTarget.Background =>
            [
                BuiltIn("View", Glyphs.View, children: ViewSubmenu()),
                BuiltIn("Sort by", Glyphs.Sort, children: SortSubmenu()),
                BuiltIn("Refresh", Glyphs.Refresh),
                PreviewItem.Separator,
                BuiltIn("New", Glyphs.Add, children: NewSubmenu()),
                PreviewItem.Separator,
                BuiltIn("Display settings", Glyphs.Monitor),
                BuiltIn("Personalize", Glyphs.Personalize),
                PreviewItem.Separator,
                BuiltIn("Open in Terminal", Glyphs.Terminal),
            ],
            PreviewTarget.Folder =>
            [
                BuiltIn("Open", Glyphs.Open, bold: true),
                BuiltIn("Open in new window", Glyphs.ShowMore),
                BuiltIn("Pin to Quick access", Glyphs.Pin),
                BuiltIn("Pin to Start", Glyphs.Pin),
                BuiltIn("Compress to ZIP file", Glyphs.Library),
                BuiltIn("Copy as path", Glyphs.Copy),
                BuiltIn("Properties", Glyphs.Properties),
                PreviewItem.Separator,
                BuiltIn("Open in Terminal", Glyphs.Terminal),
            ],
            PreviewTarget.File =>
            [
                BuiltIn("Open", Glyphs.Open, bold: true),
                BuiltIn("Open with", Glyphs.OpenWith, children: [BuiltIn("Notepad"), BuiltIn("Choose another app")]),
                BuiltIn("Compress to ZIP file", Glyphs.Library),
                BuiltIn("Copy as path", Glyphs.Copy),
                BuiltIn("Properties", Glyphs.Properties),
            ],
            _ =>
            [
                BuiltIn("Open", Glyphs.Open, bold: true),
                BuiltIn("Open in new window", Glyphs.ShowMore),
                BuiltIn("Pin to Quick access", Glyphs.Pin),
                BuiltIn("Format...", null),
                BuiltIn("Properties", Glyphs.Properties),
            ],
        });

        menu.Items.Add(PreviewItem.Separator);
        menu.Items.Add(new PreviewItem { Text = "Show more options", Glyph = Glyphs.ShowMore, Source = PreviewItemSource.ShowMoreOptions });
        return menu;
    }

    /// <summary>The full ("classic") menu with Windows' items, other programs' items and the user's entries.</summary>
    public static PreviewMenu BuildClassic(
        PreviewTarget target,
        MenuStyle style,
        bool shift,
        IReadOnlyList<MenuEntry> entries,
        IReadOnlyList<ExistingVerb> existing,
        PreviewHighlight highlight)
    {
        var top = new List<PreviewItem>();
        var middle = new List<PreviewItem>();
        var bottom = new List<PreviewItem>();

        void Place(MenuPosition position, IEnumerable<PreviewItem> items) =>
            (position == MenuPosition.Top ? top : position == MenuPosition.Bottom ? bottom : middle).AddRange(items);

        foreach (ExistingVerb verb in existing.Where(v => shift || !v.ShiftOnly))
        {
            PreviewItem item = FromVerb(verb);
            item.Highlight = string.Equals(verb.KeyName, highlight.VerbKey, StringComparison.OrdinalIgnoreCase);
            Place(verb.Position, WithSeparators(item, verb.SeparatorBefore, verb.SeparatorAfter));
        }

        MenuLocations location = ToLocation(target);
        Guid? forced = highlight.Entry is null ? null : highlight.Entry.ParentId ?? highlight.Entry.Id;

        foreach (MenuEntry entry in entries.Where(e => !e.IsChild))
        {
            bool visible = (entry.Enabled || entry.Id == forced)
                && entry.Locations.HasFlag(location)
                && (shift || !entry.ShiftOnly)
                && (location != MenuLocations.Files || entry.FileTypeList.Count == 0 || entry.FileTypeList.Contains(".txt"));

            if (!visible)
            {
                continue;
            }

            PreviewItem item = FromEntry(entry, entries, highlight.Entry);
            Place(entry.Position, WithSeparators(item, entry.SeparatorBefore, entry.SeparatorAfter));
        }

        var menu = new PreviewMenu { Style = style };
        menu.Items.AddRange(top);

        List<PreviewItem> builtIn = ClassicBuiltIns(target, shift);
        int slot = builtIn.FindIndex(i => i.Text == "\u0000slot");
        menu.Items.AddRange(builtIn.Take(slot));
        menu.Items.Add(PreviewItem.Separator);
        menu.Items.AddRange(middle);
        menu.Items.Add(PreviewItem.Separator);
        menu.Items.AddRange(builtIn.Skip(slot + 1));
        menu.Items.AddRange(bottom);

        CleanSeparators(menu.Items);
        return menu;
    }

    private static List<PreviewItem> ClassicBuiltIns(PreviewTarget target, bool shift)
    {
        PreviewItem slot = new() { Text = "\u0000slot" };
        return target switch
        {
            PreviewTarget.Background =>
            [
                BuiltIn("View", null, children: ViewSubmenu()),
                BuiltIn("Sort by", null, children: SortSubmenu()),
                BuiltIn("Refresh", null),
                PreviewItem.Separator,
                BuiltIn("Paste", null, disabled: true),
                BuiltIn("Paste shortcut", null, disabled: true),
                slot,
                BuiltIn("New", null, children: NewSubmenu()),
                PreviewItem.Separator,
                BuiltIn("Display settings", Glyphs.Monitor),
                BuiltIn("Personalize", Glyphs.Personalize),
            ],
            PreviewTarget.Folder =>
            [
                BuiltIn("Open", null, bold: true),
                BuiltIn("Open in new window", null),
                BuiltIn("Pin to Quick access", null),
                slot,
                BuiltIn("Give access to", null, children: [BuiltIn("Specific people...")]),
                BuiltIn("Include in library", null, children: [BuiltIn("Documents"), BuiltIn("Music"), BuiltIn("Pictures"), BuiltIn("Videos")]),
                PreviewItem.Separator,
                BuiltIn("Send to", null, children: SendToSubmenu()),
                PreviewItem.Separator,
                BuiltIn("Cut", null),
                BuiltIn("Copy", null),
                PreviewItem.Separator,
                BuiltIn("Create shortcut", null),
                BuiltIn("Delete", null),
                BuiltIn("Rename", null),
                PreviewItem.Separator,
                BuiltIn("Properties", null),
            ],
            PreviewTarget.File =>
            [
                BuiltIn("Open", null, bold: true),
                BuiltIn("Edit", null),
                BuiltIn("Print", null),
                slot,
                BuiltIn("Open with", null, children: [BuiltIn("Notepad"), BuiltIn("Choose another app")]),
                BuiltIn("Share", Glyphs.Share),
                PreviewItem.Separator,
                BuiltIn("Send to", null, children: SendToSubmenu()),
                PreviewItem.Separator,
                BuiltIn("Cut", null),
                BuiltIn("Copy", null),
                PreviewItem.Separator,
                BuiltIn("Create shortcut", null),
                BuiltIn("Delete", null),
                BuiltIn("Rename", null),
                PreviewItem.Separator,
                BuiltIn("Properties", null),
            ],
            _ =>
            [
                BuiltIn("Open", null, bold: true),
                BuiltIn("Open in new window", null),
                BuiltIn("Pin to Quick access", null),
                slot,
                BuiltIn("Format...", null),
                PreviewItem.Separator,
                BuiltIn("Copy", null),
                PreviewItem.Separator,
                BuiltIn("Rename", null),
                BuiltIn("Properties", null),
            ],
        };
    }

    private static PreviewItem FromEntry(MenuEntry entry, IReadOnlyList<MenuEntry> all, MenuEntry? highlight)
    {
        List<PreviewItem>? children = null;
        if (entry.Kind == EntryKind.Submenu)
        {
            children = [];
            foreach (MenuEntry child in all.Where(c => c.ParentId == entry.Id && (c.Enabled || c.Id == highlight?.Id)))
            {
                children.AddRange(WithSeparators(new PreviewItem
                {
                    Text = child.Text,
                    IconLocation = child.EffectiveIcon,
                    Source = PreviewItemSource.Mine,
                    Entry = child,
                    Highlight = child.Id == highlight?.Id,
                }, child.SeparatorBefore, child.SeparatorAfter));
            }

            CleanSeparators(children);
            if (children.Count == 0)
            {
                children.Add(new PreviewItem { Text = "(empty)", Disabled = true, Source = PreviewItemSource.Placeholder });
            }
        }

        return new PreviewItem
        {
            Text = entry.Text,
            IconLocation = entry.EffectiveIcon,
            Source = PreviewItemSource.Mine,
            Entry = entry,
            Children = children,
            Highlight = entry.Id == highlight?.Id,
        };
    }

    private static PreviewItem FromVerb(ExistingVerb verb) => new()
    {
        Text = verb.Text,
        IconLocation = verb.Icon,
        Source = PreviewItemSource.OtherApp,
        Command = verb.Command,
        Children = verb.Children?.Select(FromVerb).ToList() is { } list
            ? list.Count > 0 ? list : [new PreviewItem { Text = "(items added by the program)", Disabled = true, Source = PreviewItemSource.Placeholder }]
            : null,
    };

    private static IEnumerable<PreviewItem> WithSeparators(PreviewItem item, bool before, bool after)
    {
        if (before) yield return PreviewItem.Separator;
        yield return item;
        if (after) yield return PreviewItem.Separator;
    }

    /// <summary>Removes leading, trailing and doubled separators, like Windows does.</summary>
    private static void CleanSeparators(List<PreviewItem> items)
    {
        for (int i = items.Count - 1; i >= 0; i--)
        {
            bool isSeparator = items[i].IsSeparator;
            if (isSeparator && (i == 0 || i == items.Count - 1 || items[i - 1].IsSeparator))
            {
                items.RemoveAt(i);
            }
        }

        while (items.Count > 0 && items[0].IsSeparator)
        {
            items.RemoveAt(0);
        }
    }

    private static PreviewItem BuiltIn(string text, string? glyph = null, bool disabled = false, bool bold = false, List<PreviewItem>? children = null) => new()
    {
        Text = text,
        Glyph = glyph,
        Disabled = disabled,
        Bold = bold,
        Children = children,
        Source = PreviewItemSource.BuiltIn,
    };

    private static List<PreviewItem> ViewSubmenu() =>
    [
        BuiltIn("Large icons"), BuiltIn("Medium icons"), BuiltIn("Small icons"), PreviewItem.Separator,
        BuiltIn("Auto arrange icons"), BuiltIn("Align icons to grid"), PreviewItem.Separator, BuiltIn("Show desktop icons"),
    ];

    private static List<PreviewItem> SortSubmenu() =>
        [BuiltIn("Name"), BuiltIn("Size"), BuiltIn("Item type"), BuiltIn("Date modified")];

    private static List<PreviewItem> NewSubmenu() =>
        [BuiltIn("Folder", Glyphs.Folder), BuiltIn("Shortcut", Glyphs.Link), PreviewItem.Separator, BuiltIn("Text Document", Glyphs.Document)];

    private static List<PreviewItem> SendToSubmenu() =>
        [BuiltIn("Compressed (zipped) folder"), BuiltIn("Desktop (create shortcut)"), BuiltIn("Documents"), BuiltIn("Mail recipient")];
}
