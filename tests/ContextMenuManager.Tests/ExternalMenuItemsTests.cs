using ContextMenuManager.Core;
using Xunit.Abstractions;

namespace ContextMenuManager.Tests;

public class ExternalMenuItemsTests(ITestOutputHelper output)
{
    [Fact]
    public void Reading_the_items_on_this_PC_works_and_skips_our_own()
    {
        IReadOnlyList<ExternalItem> items = ExternalMenuItems.ReadAll();

        foreach (ExternalItem item in items)
        {
            output.WriteLine($"{item.Kind,-15} {item.Text,-40} {MenuEntry.DescribeLocations(item.Locations),-30} {item.Publisher}");
            Assert.False(string.IsNullOrWhiteSpace(item.Text));
            Assert.NotEqual(MenuLocations.None, item.Locations);
            Assert.NotEmpty(item.RegistryPaths);
            Assert.DoesNotContain(item.RegistryPaths, p => p.Contains(RegistryWriter.KeyPrefix, StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public void A_command_becomes_an_editable_entry_with_path_token()
    {
        var item = new ExternalItem
        {
            Id = "verb|git_shell|Open Git Bash here",
            KeyName = "git_shell",
            Text = "Open Git Bash here",
            Kind = ExternalItemKind.Command,
            Command = "\"C:\\Program Files\\Git\\git-bash.exe\" \"--cd=%v.\"",
            Icon = "C:\\Program Files\\Git\\git-bash.exe",
            ShiftOnly = true,
        };
        item.Locations = MenuLocations.Background | MenuLocations.Folders;

        MenuEntry entry = Assert.Single(item.ToEditableEntries());

        Assert.Equal("C:\\Program Files\\Git\\git-bash.exe", entry.Program);
        Assert.Equal("\"--cd={path}.\"", entry.Arguments);
        Assert.Equal(item.Locations, entry.Locations);
        Assert.True(entry.ShiftOnly);
        Assert.False(entry.Enabled);
    }

    [Fact]
    public void A_submenu_is_copied_with_its_items_and_extensions_cannot_be_copied()
    {
        var submenu = new ExternalItem
        {
            Id = "verb|tools|Tools",
            KeyName = "tools",
            Text = "Tools",
            Kind = ExternalItemKind.Submenu,
            Children =
            [
                new ExistingVerb("a", "First", "", "a.exe %1", MenuPosition.Default, false, false, false, null),
                new ExistingVerb("b", "Nested", "", "", MenuPosition.Default, false, false, false, []),
            ],
        };
        submenu.Locations = MenuLocations.Files;

        IReadOnlyList<MenuEntry> entries = submenu.ToEditableEntries();

        Assert.Equal(2, entries.Count);
        Assert.Equal(EntryKind.Submenu, entries[0].Kind);
        Assert.Equal(entries[0].Id, entries[1].ParentId);
        Assert.Equal("{path}", entries[1].Arguments);

        var extension = new ExternalItem { Id = "shellex|x", KeyName = "7-Zip", Text = "7-Zip", Kind = ExternalItemKind.ShellExtension };
        Assert.Empty(extension.ToEditableEntries());
    }
}
