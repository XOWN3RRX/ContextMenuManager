using System.Text.Json.Serialization;

namespace ContextMenuManager.Core;

/// <summary>Where an entry shows up. An entry can be in several places at once.</summary>
[Flags]
public enum MenuLocations
{
    None = 0,

    /// <summary>Right-click on the empty desktop or on the empty space inside a folder window.</summary>
    Background = 1,

    /// <summary>Right-click on a folder.</summary>
    Folders = 2,

    /// <summary>Right-click on a file (optionally only some file types).</summary>
    Files = 4,

    /// <summary>Right-click on a drive, such as C:.</summary>
    Drives = 8,
}

/// <summary>Where Windows places the entry inside the menu.</summary>
public enum MenuPosition
{
    Default,
    Top,
    Bottom,
}

public enum EntryKind
{
    /// <summary>Runs a program when clicked.</summary>
    Command,

    /// <summary>Opens a submenu with its own entries.</summary>
    Submenu,
}

/// <summary>One item that the app adds to the Windows context menu.</summary>
public sealed class MenuEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public EntryKind Kind { get; set; } = EntryKind.Command;

    /// <summary>When off, the entry is kept in the app but removed from the Windows menu.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>The text shown in the menu, for example "Open Git Bash here".</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>The program to run. May contain environment variables such as %ProgramFiles%.</summary>
    public string Program { get; set; } = string.Empty;

    /// <summary>Arguments for <see cref="Program"/>. <c>{path}</c> is replaced by the item you right-clicked.</summary>
    public string Arguments { get; set; } = string.Empty;

    /// <summary>Icon as "file" or "file,index". Empty uses the program's icon.</summary>
    public string Icon { get; set; } = string.Empty;

    public MenuLocations Locations { get; set; } = MenuLocations.Background;

    /// <summary>File extensions such as ".txt; .md". Empty means all files. Only used with <see cref="MenuLocations.Files"/>.</summary>
    public string FileTypes { get; set; } = string.Empty;

    public MenuPosition Position { get; set; } = MenuPosition.Default;

    /// <summary>Only show the entry when Shift is held while right-clicking.</summary>
    public bool ShiftOnly { get; set; }

    public bool SeparatorBefore { get; set; }

    public bool SeparatorAfter { get; set; }

    /// <summary>The submenu this entry belongs to, or null for a top-level entry.</summary>
    public Guid? ParentId { get; set; }

    [JsonIgnore]
    public bool IsChild => ParentId is not null;

    /// <summary>The icon that is written to the registry (falls back to the program).</summary>
    [JsonIgnore]
    public string EffectiveIcon
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(Icon))
            {
                return Icon.Trim();
            }

            string program = CommandBuilder.Unquote(Program.Trim());
            return Kind == EntryKind.Command && program.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? program : string.Empty;
        }
    }

    /// <summary>The parsed list from <see cref="FileTypes"/>, normalized to ".ext" in lower case.</summary>
    [JsonIgnore]
    public IReadOnlyList<string> FileTypeList => ParseFileTypes(FileTypes);

    public MenuEntry Clone() => (MenuEntry)MemberwiseClone();

    public static IReadOnlyList<string> ParseFileTypes(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        var result = new List<string>();
        foreach (string part in value.Split([';', ',', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string ext = part.TrimStart('*').ToLowerInvariant();
            if (!ext.StartsWith('.'))
            {
                ext = "." + ext;
            }

            // Only plain extensions: they become registry key names.
            if (ext.Length > 1 && ext.Skip(1).All(c => char.IsLetterOrDigit(c) || c is '_' or '-' or '.') && !result.Contains(ext))
            {
                result.Add(ext);
            }
        }

        return result;
    }

    public static string DescribeLocations(MenuLocations locations)
    {
        var parts = new List<string>();
        if (locations.HasFlag(MenuLocations.Background)) parts.Add("Desktop");
        if (locations.HasFlag(MenuLocations.Folders)) parts.Add("Folders");
        if (locations.HasFlag(MenuLocations.Files)) parts.Add("Files");
        if (locations.HasFlag(MenuLocations.Drives)) parts.Add("Drives");
        return parts.Count == 0 ? "Nowhere" : string.Join(" · ", parts);
    }
}
