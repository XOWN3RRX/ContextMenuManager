using ContextMenuManager.Core;

namespace ContextMenuManager.Tests;

public sealed class EntryStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ContextMenuManager.Tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void Save_and_load_keep_every_field_and_the_order()
    {
        var store = new EntryStore(_directory);
        var menu = new MenuEntry { Kind = EntryKind.Submenu, Text = "Tools", Locations = MenuLocations.Background | MenuLocations.Folders };
        var child = new MenuEntry { Text = "Child", Program = "a.exe", ParentId = menu.Id, SeparatorBefore = true };
        var file = new MenuEntry { Text = "Edit", Program = "b.exe", Locations = MenuLocations.Files, FileTypes = ".txt", ShiftOnly = true, Position = MenuPosition.Bottom, Enabled = false };
        store.Entries.AddRange([menu, child, file]);
        store.Save();

        var loaded = new EntryStore(_directory);
        loaded.Load();

        Assert.Null(loaded.LoadError);
        Assert.Equal(3, loaded.Entries.Count);
        Assert.Equal(menu.Locations, loaded.Entries[0].Locations);
        Assert.Equal(menu.Id, loaded.Entries[1].ParentId);
        Assert.True(loaded.Entries[1].SeparatorBefore);
        Assert.Equal(MenuPosition.Bottom, loaded.Entries[2].Position);
        Assert.False(loaded.Entries[2].Enabled);
        Assert.True(loaded.Entries[2].ShiftOnly);
    }

    [Fact]
    public void A_broken_file_is_backed_up_and_the_list_starts_empty()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path.Combine(_directory, "entries.json"), "{ not json");

        var store = new EntryStore(_directory);
        store.Load();

        Assert.NotNull(store.LoadError);
        Assert.Empty(store.Entries);
        Assert.Contains(Directory.GetFiles(_directory), f => f.Contains("entries.json.broken-"));
    }

    [Fact]
    public void Import_turns_entries_off_and_gives_them_new_ids()
    {
        var menu = new MenuEntry { Kind = EntryKind.Submenu, Text = "Tools" };
        var child = new MenuEntry { Text = "Child", Program = "a.exe", ParentId = menu.Id };
        string json = EntryStore.Serialize([menu, child]);

        List<MenuEntry> imported = EntryStore.PrepareImport(EntryStore.Deserialize(json).Entries);

        Assert.All(imported, e => Assert.False(e.Enabled));
        Assert.NotEqual(menu.Id, imported[0].Id);
        Assert.Equal(imported[0].Id, imported[1].ParentId);
    }

    [Fact]
    public void Import_drops_children_of_missing_submenus()
    {
        var orphan = new MenuEntry { Text = "Orphan", Program = "a.exe", ParentId = Guid.NewGuid() };

        List<MenuEntry> imported = EntryStore.PrepareImport(EntryStore.Deserialize(EntryStore.Serialize([orphan])).Entries);

        Assert.Empty(imported);
    }

    [Fact]
    public void Other_json_files_are_rejected()
    {
        Assert.Throws<InvalidDataException>(() => EntryStore.Deserialize("{\"format\":\"something else\",\"entries\":[]}"));
    }

    [Fact]
    public void Move_only_swaps_with_siblings()
    {
        var store = new EntryStore(_directory);
        var a = new MenuEntry { Text = "A" };
        var menu = new MenuEntry { Kind = EntryKind.Submenu, Text = "Menu" };
        var child = new MenuEntry { Text = "Child", ParentId = menu.Id };
        var b = new MenuEntry { Text = "B" };
        store.Entries.AddRange([a, menu, child, b]);

        Assert.False(store.Move(child, -1));
        Assert.True(store.Move(b, -1));
        Assert.Equal(["A", "B", "Child", "Menu"], store.Entries.Select(e => e.Text));
        Assert.Equal(["A", "B"], store.InDisplayOrder().Where(e => !e.IsChild && e.Kind == EntryKind.Command).Select(e => e.Text));
    }

    [Theory]
    [InlineData(".txt; .md", new[] { ".txt", ".md" })]
    [InlineData("TXT, *.Log", new[] { ".txt", ".log" })]
    [InlineData("", new string[0])]
    [InlineData(". bad\\name", new string[0])]
    public void File_types_are_parsed_and_normalized(string input, string[] expected)
    {
        Assert.Equal(expected, MenuEntry.ParseFileTypes(input));
    }

    [Fact]
    public void Validator_explains_missing_fields()
    {
        var entry = new MenuEntry { Text = " ", Program = "", Locations = MenuLocations.None };

        IReadOnlyList<string> problems = EntryValidator.Validate(entry);

        Assert.Equal(3, problems.Count);
    }
}
