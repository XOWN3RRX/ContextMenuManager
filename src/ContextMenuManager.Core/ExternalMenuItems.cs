using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace ContextMenuManager.Core;

public enum ExternalItemKind
{
    /// <summary>A static item that runs a command (for example Git's "Open Git Bash here").</summary>
    Command,

    /// <summary>A static submenu.</summary>
    Submenu,

    /// <summary>A shell extension (COM handler, for example 7-Zip). It decides its own text and items at run time.</summary>
    ShellExtension,
}

/// <summary>A context menu item that another program added. The app only reads these.</summary>
public sealed class ExternalItem
{
    public required string Id { get; init; }

    public required string KeyName { get; init; }

    public required string Text { get; init; }

    public required ExternalItemKind Kind { get; init; }

    public string Icon { get; init; } = string.Empty;

    public string Command { get; init; } = string.Empty;

    /// <summary>The program that provides the item, from its file's version info (for example "Git").</summary>
    public string? Publisher { get; init; }

    /// <summary>For shell extensions: the DLL that implements it.</summary>
    public string? ModulePath { get; init; }

    public bool ShiftOnly { get; init; }

    public MenuPosition Position { get; init; }

    public IReadOnlyList<ExistingVerb>? Children { get; init; }

    public MenuLocations Locations { get; internal set; }

    /// <summary>Where it is registered, relative to HKEY_CLASSES_ROOT.</summary>
    public List<string> RegistryPaths { get; } = [];

    public bool CanCopy => Kind != ExternalItemKind.ShellExtension;

    /// <summary>
    /// Turns the item into entries the user owns and can edit (turned off, so the copy
    /// doesn't show up twice in the menu). A submenu comes first, followed by its items.
    /// </summary>
    public IReadOnlyList<MenuEntry> ToEditableEntries()
    {
        if (!CanCopy)
        {
            return [];
        }

        var first = new MenuEntry
        {
            Kind = Kind == ExternalItemKind.Submenu ? EntryKind.Submenu : EntryKind.Command,
            Text = Text,
            Icon = Icon,
            Locations = Locations,
            ShiftOnly = ShiftOnly,
            Position = Position,
            Enabled = false,
        };

        if (Kind == ExternalItemKind.Command)
        {
            (first.Program, first.Arguments) = SplitForEditing(Command);
            return [first];
        }

        var result = new List<MenuEntry> { first };
        foreach (ExistingVerb child in Children ?? [])
        {
            if (child.Children is not null || string.IsNullOrWhiteSpace(child.Command))
            {
                continue; // Only one level of submenus is supported.
            }

            (string program, string arguments) = SplitForEditing(child.Command);
            result.Add(new MenuEntry
            {
                Text = child.Text,
                Program = program,
                Arguments = arguments,
                Icon = child.Icon,
                ParentId = first.Id,
                Locations = Locations,
                SeparatorBefore = child.SeparatorBefore,
                SeparatorAfter = child.SeparatorAfter,
                Enabled = false,
            });
        }

        return result;
    }

    private static (string Program, string Arguments) SplitForEditing(string command)
    {
        (string program, string arguments) = CommandBuilder.Split(command);
        return (program, ExternalMenuItems.ShellPlaceholder().Replace(arguments, CommandBuilder.PathToken));
    }
}

/// <summary>Finds the context menu items other programs added, for every place the app supports.</summary>
public static partial class ExternalMenuItems
{
    private static readonly (MenuLocations Location, string[] Paths)[] s_extensionPaths =
    [
        (MenuLocations.Background, [@"Directory\Background\shellex\ContextMenuHandlers", @"DesktopBackground\shellex\ContextMenuHandlers"]),
        (MenuLocations.Folders, [@"Directory\shellex\ContextMenuHandlers", @"Folder\shellex\ContextMenuHandlers"]),
        (MenuLocations.Files, [@"*\shellex\ContextMenuHandlers", @"AllFilesystemObjects\shellex\ContextMenuHandlers"]),
        (MenuLocations.Drives, [@"Drive\shellex\ContextMenuHandlers"]),
    ];

    // Handlers whose registry name says little about what you see in the menu.
    private static readonly Dictionary<string, string> s_knownNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["EPP"] = "Scan with Microsoft Defender",
    };

    private static readonly MenuLocations[] s_locations =
        [MenuLocations.Background, MenuLocations.Folders, MenuLocations.Files, MenuLocations.Drives];

    [GeneratedRegex("%[1vVlL](?![A-Za-z0-9_])")]
    internal static partial Regex ShellPlaceholder();

    [GeneratedRegex(@"^\{[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}\}$")]
    private static partial Regex GuidPattern();

    /// <summary>Reads everything. Takes a moment (it looks at program files), so call it off the UI thread.</summary>
    public static IReadOnlyList<ExternalItem> ReadAll()
    {
        var items = new Dictionary<string, ExternalItem>(StringComparer.OrdinalIgnoreCase);
        var publishers = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        foreach (MenuLocations location in s_locations)
        {
            foreach (ExistingVerb verb in ShellVerbReader.Read(location))
            {
                ExternalItemKind kind = verb.Children is null ? ExternalItemKind.Command : ExternalItemKind.Submenu;
                string id = $"verb|{verb.KeyName}|{verb.Text}";
                if (!items.TryGetValue(id, out ExternalItem? item))
                {
                    string? exe = ProgramPath(verb.Command);
                    item = new ExternalItem
                    {
                        Id = id,
                        KeyName = verb.KeyName,
                        Text = verb.Text,
                        Kind = kind,
                        Icon = verb.Icon.Length > 0 ? verb.Icon : exe ?? string.Empty,
                        Command = verb.Command,
                        Publisher = Publisher(exe, publishers),
                        ShiftOnly = verb.ShiftOnly,
                        Position = verb.Position,
                        Children = verb.Children,
                    };
                    items[id] = item;
                }

                Add(item, location, verb.SourcePath);
            }

            foreach (ExternalItem extension in ReadShellExtensions(location, publishers))
            {
                if (items.TryGetValue(extension.Id, out ExternalItem? existing))
                {
                    Add(existing, location, extension.RegistryPaths[0]);
                }
                else
                {
                    items[extension.Id] = extension;
                }
            }
        }

        return [.. items.Values.OrderBy(i => i.Kind == ExternalItemKind.ShellExtension).ThenBy(i => i.Text, StringComparer.CurrentCultureIgnoreCase)];
    }

    private static void Add(ExternalItem item, MenuLocations location, string path)
    {
        item.Locations |= location;
        if (!item.RegistryPaths.Contains(path, StringComparer.OrdinalIgnoreCase))
        {
            item.RegistryPaths.Add(path);
        }
    }

    private static IEnumerable<ExternalItem> ReadShellExtensions(MenuLocations location, Dictionary<string, string?> publishers)
    {
        string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

        foreach (string path in s_extensionPaths.First(p => p.Location == location).Paths)
        {
            string[] names;
            try
            {
                using RegistryKey? handlers = Registry.ClassesRoot.OpenSubKey(path);
                names = handlers?.GetSubKeyNames() ?? [];
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
            {
                continue;
            }

            foreach (string name in names)
            {
                ExternalItem? item = ReadShellExtension(path, name, location, windows, publishers);
                if (item is not null)
                {
                    yield return item;
                }
            }
        }
    }

    private static ExternalItem? ReadShellExtension(string path, string name, MenuLocations location, string windows, Dictionary<string, string?> publishers)
    {
        // Tools that "disable" a handler often rename it with a leading "-".
        if (name.StartsWith('-'))
        {
            return null;
        }

        try
        {
            using RegistryKey? handler = Registry.ClassesRoot.OpenSubKey($@"{path}\{name}");
            string clsid = GuidPattern().IsMatch(name) ? name : handler?.GetValue(null) as string ?? string.Empty;
            if (!GuidPattern().IsMatch(clsid))
            {
                return null;
            }

            using RegistryKey? classKey = Registry.ClassesRoot.OpenSubKey($@"CLSID\{clsid}");
            using RegistryKey? server = classKey?.OpenSubKey("InprocServer32");
            string? module = server?.GetValue(null) is string dll && dll.Length > 0
                ? Environment.ExpandEnvironmentVariables(dll).Trim('"')
                : null;

            // No 64-bit server: Explorer can't load it, so it never shows. Windows' own handlers are skipped too.
            if (module is null || module.StartsWith(windows, StringComparison.OrdinalIgnoreCase)
                || !Path.IsPathRooted(module) && !module.Contains('\\'))
            {
                return null;
            }

            string text = !GuidPattern().IsMatch(name) ? name : classKey?.GetValue(null) as string ?? string.Empty;
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            var item = new ExternalItem
            {
                Id = $"shellex|{clsid}",
                KeyName = name,
                Text = s_knownNames.TryGetValue(text.Trim(), out string? known) ? known : text.Trim(),
                Kind = ExternalItemKind.ShellExtension,
                Icon = File.Exists(module) ? module : string.Empty,
                ModulePath = module,
                Publisher = Publisher(module, publishers),
            };
            Add(item, location, $@"{path}\{name}");
            return item;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }

    /// <summary>The program file a command line starts, if it exists.</summary>
    private static string? ProgramPath(string command)
    {
        string text = Environment.ExpandEnvironmentVariables(command).Trim();
        (string file, _) = CommandBuilder.Split(text);
        if (IsExistingFile(file))
        {
            return file;
        }

        // Unquoted paths with spaces: try each space as the end of the path, like Windows does.
        if (!text.StartsWith('"'))
        {
            for (int space = text.IndexOf(' '); space > 0; space = text.IndexOf(' ', space + 1))
            {
                string candidate = text[..space];
                if (IsExistingFile(candidate))
                {
                    return candidate;
                }
            }

            if (IsExistingFile(text))
            {
                return text;
            }
        }

        return null;
    }

    private static bool IsExistingFile(string path) =>
        path.Length > 0 && Path.IsPathFullyQualified(path) && File.Exists(path);

    private static string? Publisher(string? file, Dictionary<string, string?> cache)
    {
        if (string.IsNullOrEmpty(file) || !File.Exists(file))
        {
            return null;
        }

        if (!cache.TryGetValue(file, out string? publisher))
        {
            try
            {
                FileVersionInfo info = FileVersionInfo.GetVersionInfo(file);
                publisher = FirstText(info.ProductName, info.CompanyName, info.FileDescription) ?? Path.GetFileNameWithoutExtension(file);
            }
            catch (FileNotFoundException)
            {
                publisher = null;
            }

            cache[file] = publisher;
        }

        return publisher;
    }

    private static string? FirstText(params string?[] values) =>
        values.Select(v => v?.Trim()).FirstOrDefault(v => !string.IsNullOrEmpty(v));
}
