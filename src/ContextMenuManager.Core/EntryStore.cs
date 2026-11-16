using System.Text.Json;
using System.Text.Json.Serialization;

namespace ContextMenuManager.Core;

/// <summary>
/// The user's list of entries, saved as JSON in the data folder (by default
/// <c>%AppData%\ContextMenuManager\entries.json</c>). The order of the list is the menu order.
/// </summary>
public sealed class EntryStore
{
    public const string FileFormat = "ContextMenuManager";
    public const int CurrentVersion = 1;

    private readonly string _filePath;

    public EntryStore(string dataDirectory)
    {
        DataDirectory = dataDirectory;
        _filePath = Path.Combine(dataDirectory, "entries.json");
    }

    public string DataDirectory { get; }

    public List<MenuEntry> Entries { get; private set; } = [];

    /// <summary>Set when the file existed but could not be read. The broken file is kept as a backup.</summary>
    public string? LoadError { get; private set; }

    public void Load()
    {
        LoadError = null;
        if (!File.Exists(_filePath))
        {
            Entries = [];
            return;
        }

        try
        {
            Entries = Normalize(Deserialize(File.ReadAllText(_filePath)).Entries);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            LoadError = ex.Message;
            Entries = [];
            TryBackupBrokenFile();
        }
    }

    public void Save()
    {
        Directory.CreateDirectory(DataDirectory);
        string json = Serialize(Entries);

        // Write to a temporary file first so a crash never leaves a half-written list.
        string temp = _filePath + ".tmp";
        File.WriteAllText(temp, json);
        File.Move(temp, _filePath, overwrite: true);
    }

    public MenuEntry? Find(Guid id) => Entries.FirstOrDefault(e => e.Id == id);

    public IEnumerable<MenuEntry> ChildrenOf(MenuEntry parent) => Entries.Where(e => e.ParentId == parent.Id);

    public IEnumerable<MenuEntry> Submenus => Entries.Where(e => e.Kind == EntryKind.Submenu && !e.IsChild);

    /// <summary>The list in display order: each top-level entry followed by its children.</summary>
    public IEnumerable<MenuEntry> InDisplayOrder()
    {
        foreach (MenuEntry entry in Entries.Where(e => !e.IsChild))
        {
            yield return entry;
            foreach (MenuEntry child in ChildrenOf(entry))
            {
                yield return child;
            }
        }
    }

    public void Remove(MenuEntry entry)
    {
        Entries.RemoveAll(e => e.Id == entry.Id || e.ParentId == entry.Id);
    }

    /// <summary>Moves an entry up or down among its siblings (same parent).</summary>
    public bool Move(MenuEntry entry, int direction)
    {
        List<MenuEntry> siblings = [.. Entries.Where(e => e.ParentId == entry.ParentId)];
        int index = siblings.FindIndex(e => e.Id == entry.Id);
        int target = index + Math.Sign(direction);
        if (index < 0 || target < 0 || target >= siblings.Count)
        {
            return false;
        }

        int a = Entries.IndexOf(siblings[index]);
        int b = Entries.IndexOf(siblings[target]);
        (Entries[a], Entries[b]) = (Entries[b], Entries[a]);
        return true;
    }

    /// <summary>
    /// Moves an entry (a submenu together with its items) to a new place, as used by drag and drop.
    /// </summary>
    /// <param name="parentId">The submenu to move into, or null for the top level.</param>
    /// <param name="before">The sibling to put it in front of, or null for the end.</param>
    /// <returns>False if the move isn't possible or changes nothing.</returns>
    /// <remarks>
    /// Only commands can go into a submenu (submenus can't be nested). An entry that moves into a
    /// submenu takes the submenu's locations and loses the settings children can't have.
    /// </remarks>
    public bool MoveTo(MenuEntry entry, Guid? parentId, MenuEntry? before)
    {
        if (!Entries.Contains(entry) || before == entry || before is not null && (before.ParentId != parentId || !Entries.Contains(before)))
        {
            return false;
        }

        MenuEntry? parent = null;
        if (parentId is { } id)
        {
            parent = Find(id);
            if (parent is not { Kind: EntryKind.Submenu, IsChild: false } || entry.Kind != EntryKind.Command || parent == entry)
            {
                return false;
            }
        }

        List<Guid> oldOrder = [.. Entries.Select(e => e.Id)];
        Guid? oldParent = entry.ParentId;

        List<MenuEntry> block = [entry, .. entry.IsChild ? [] : ChildrenOf(entry)];
        Entries.RemoveAll(block.Contains);

        int index = before is not null ? Entries.IndexOf(before)
            : parent is not null ? Entries.FindLastIndex(e => e.Id == parent.Id || e.ParentId == parent.Id) + 1
            : Entries.Count;
        Entries.InsertRange(index, block);

        entry.ParentId = parentId;
        if (parent is not null && oldParent != parentId)
        {
            entry.Locations = parent.Locations;
            entry.ShiftOnly = false;
            entry.Position = MenuPosition.Default;
        }

        return oldParent != parentId || !oldOrder.SequenceEqual(Entries.Select(e => e.Id));
    }

    public static string Serialize(IEnumerable<MenuEntry> entries) =>
        JsonSerializer.Serialize(new EntryFile { Entries = [.. entries] }, CoreJsonContext.Default.EntryFile);

    /// <summary>Reads an exported or saved list. Throws <see cref="InvalidDataException"/> for other files.</summary>
    public static EntryFile Deserialize(string json)
    {
        EntryFile file = JsonSerializer.Deserialize(json, CoreJsonContext.Default.EntryFile)
            ?? throw new InvalidDataException("The file is empty.");

        if (file.Format != FileFormat)
        {
            throw new InvalidDataException("This is not a Context Menu Manager file.");
        }

        if (file.Version > CurrentVersion)
        {
            throw new InvalidDataException("This file was made by a newer version of Context Menu Manager.");
        }

        return file;
    }

    /// <summary>
    /// Prepares entries from a file to be added to the list: new ids (so importing twice
    /// doesn't clash), turned off so the user can review the commands first, and
    /// children only if their submenu is part of the same file.
    /// </summary>
    public static List<MenuEntry> PrepareImport(IEnumerable<MenuEntry> imported)
    {
        List<MenuEntry> entries = Normalize([.. imported.Select(e => e.Clone())]);
        var ids = entries.ToDictionary(e => e.Id, _ => Guid.NewGuid());

        foreach (MenuEntry entry in entries)
        {
            entry.Id = ids[entry.Id];
            entry.ParentId = entry.ParentId is { } parent ? ids[parent] : null;
            entry.Enabled = false;
        }

        return entries;
    }

    /// <summary>Drops broken data: unknown parents, nested submenus, duplicate ids, oversized text.</summary>
    private static List<MenuEntry> Normalize(List<MenuEntry>? entries)
    {
        var result = new List<MenuEntry>();
        var seen = new HashSet<Guid>();

        foreach (MenuEntry entry in entries ?? [])
        {
            if (entry is null || !seen.Add(entry.Id))
            {
                continue;
            }

            entry.Text = Limit(entry.Text, 200);
            entry.Program = Limit(entry.Program, 2000);
            entry.Arguments = Limit(entry.Arguments, 8000);
            entry.Icon = Limit(entry.Icon, 2000);
            entry.FileTypes = Limit(entry.FileTypes, 500);
            result.Add(entry);
        }

        var submenus = result.Where(e => e.Kind == EntryKind.Submenu && e.ParentId is null).Select(e => e.Id).ToHashSet();
        result.RemoveAll(e => e.ParentId is { } parent && (!submenus.Contains(parent) || e.Kind == EntryKind.Submenu));
        return result;
    }

    private static string Limit(string? value, int max) => value is null ? string.Empty : value.Length <= max ? value : value[..max];

    private void TryBackupBrokenFile()
    {
        try
        {
            File.Copy(_filePath, _filePath + $".broken-{DateTime.Now:yyyyMMdd-HHmmss}", overwrite: false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Nothing else we can do; the app starts with an empty list.
        }
    }
}

public sealed class EntryFile
{
    public string Format { get; set; } = EntryStore.FileFormat;

    public int Version { get; set; } = EntryStore.CurrentVersion;

    public List<MenuEntry> Entries { get; set; } = [];
}

[JsonSerializable(typeof(EntryFile))]
[JsonSourceGenerationOptions(WriteIndented = true, UseStringEnumConverter = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal sealed partial class CoreJsonContext : JsonSerializerContext;
