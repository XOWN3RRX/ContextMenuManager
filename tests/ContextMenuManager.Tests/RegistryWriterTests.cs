using ContextMenuManager.Core;
using Microsoft.Win32;

namespace ContextMenuManager.Tests;

/// <summary>
/// Writes to a throwaway key under HKEY_CURRENT_USER\Software, never to the real
/// Software\Classes, so running the tests doesn't change your context menu.
/// </summary>
public sealed class RegistryWriterTests : IDisposable
{
    private readonly string _root = $@"Software\ContextMenuManager.Tests\{Guid.NewGuid():N}";
    private readonly RegistryWriter _writer;

    public RegistryWriterTests()
    {
        _writer = new RegistryWriter(_root, notifyShell: false);
    }

    public void Dispose()
    {
        Registry.CurrentUser.DeleteSubKeyTree(_root, throwOnMissingSubKey: false);
        using RegistryKey? parent = Registry.CurrentUser.OpenSubKey(@"Software\ContextMenuManager.Tests", writable: true);
        if (parent is not null && parent.SubKeyCount == 0)
        {
            Registry.CurrentUser.DeleteSubKey(@"Software\ContextMenuManager.Tests", throwOnMissingSubKey: false);
        }
    }

    private RegistryKey? Open(string path) => Registry.CurrentUser.OpenSubKey(_root + "\\" + path);

    [Fact]
    public void Writes_a_command_with_marker_text_icon_and_flags()
    {
        var entry = new MenuEntry
        {
            Text = "Open here",
            Program = @"C:\Tools\tool.exe",
            Arguments = "\"{path}\"",
            Locations = MenuLocations.Background,
            Position = MenuPosition.Top,
            ShiftOnly = true,
            SeparatorAfter = true,
        };

        _writer.Apply([entry]);

        string name = RegistryWriter.KeyName(0, entry);
        using RegistryKey? key = Open($@"{RegistryWriter.BackgroundShellPath}\{name}");
        Assert.NotNull(key);
        Assert.Equal(RegistryWriter.MarkerValue, key.GetValue(RegistryWriter.MarkerName));
        Assert.Equal("Open here", key.GetValue("MUIVerb"));
        Assert.Equal(@"C:\Tools\tool.exe", key.GetValue("Icon"));
        Assert.Equal("Top", key.GetValue("Position"));
        Assert.NotNull(key.GetValue("Extended"));
        Assert.NotNull(key.GetValue("SeparatorAfter"));
        Assert.Null(key.GetValue("SeparatorBefore"));

        using RegistryKey? command = key.OpenSubKey("command");
        Assert.Equal("C:\\Tools\\tool.exe \"%V\"", command?.GetValue(null));
    }

    [Fact]
    public void Disabled_entries_are_not_written()
    {
        var entry = new MenuEntry { Text = "Off", Program = "a.exe", Enabled = false };

        _writer.Apply([entry]);

        Assert.Empty(_writer.FindOwnedKeys());
    }

    [Fact]
    public void Writes_every_location_and_file_type()
    {
        var entry = new MenuEntry
        {
            Text = "Edit",
            Program = "notepad.exe",
            Arguments = "\"{path}\"",
            Locations = MenuLocations.Folders | MenuLocations.Files | MenuLocations.Drives,
            FileTypes = ".txt; md",
        };

        _writer.Apply([entry]);

        string name = RegistryWriter.KeyName(0, entry);
        string[] expected =
        [
            $@"{RegistryWriter.FolderShellPath}\{name}",
            $@"{RegistryWriter.DriveShellPath}\{name}",
            $@"SystemFileAssociations\.txt\shell\{name}",
            $@"SystemFileAssociations\.md\shell\{name}",
        ];

        Assert.Equal(expected.Order(), _writer.FindOwnedKeys().Order());
    }

    [Fact]
    public void Submenu_children_are_written_in_order_inside_the_submenu()
    {
        var menu = new MenuEntry { Kind = EntryKind.Submenu, Text = "Tools", Locations = MenuLocations.Background };
        var first = new MenuEntry { Text = "First", Program = "a.exe", ParentId = menu.Id };
        var second = new MenuEntry { Text = "Second", Program = "b.exe", ParentId = menu.Id };
        var off = new MenuEntry { Text = "Off", Program = "c.exe", ParentId = menu.Id, Enabled = false };

        _writer.Apply([menu, first, second, off]);

        using RegistryKey? key = Open($@"{RegistryWriter.BackgroundShellPath}\{RegistryWriter.KeyName(0, menu)}");
        Assert.NotNull(key);
        Assert.Equal(string.Empty, key.GetValue("SubCommands"));
        using RegistryKey? shell = key.OpenSubKey("shell");
        string[] children = shell!.GetSubKeyNames();
        Assert.Equal(2, children.Length);
        using RegistryKey? firstKey = shell.OpenSubKey(children.Order().First());
        Assert.Equal("First", firstKey?.GetValue("MUIVerb"));
    }

    [Fact]
    public void Apply_replaces_old_keys_and_never_touches_other_programs_items()
    {
        // An item "another program" added, even one whose name looks like ours but has no marker.
        using (RegistryKey foreign = Registry.CurrentUser.CreateSubKey($@"{_root}\{RegistryWriter.BackgroundShellPath}\git_bash"))
        {
            foreign.SetValue(null, "Open Git Bash here");
        }

        using (Registry.CurrentUser.CreateSubKey($@"{_root}\{RegistryWriter.BackgroundShellPath}\{RegistryWriter.KeyPrefix}Lookalike"))
        {
        }

        var entry = new MenuEntry { Text = "Mine", Program = "a.exe" };
        _writer.Apply([entry]);
        _writer.Apply([]);

        Assert.Empty(_writer.FindOwnedKeys());
        using RegistryKey? shell = Open(RegistryWriter.BackgroundShellPath);
        Assert.Contains("git_bash", shell!.GetSubKeyNames());
        Assert.Contains($"{RegistryWriter.KeyPrefix}Lookalike", shell.GetSubKeyNames());
    }

    [Fact]
    public void RemoveAll_reports_how_many_items_were_removed()
    {
        var entry = new MenuEntry { Text = "Mine", Program = "a.exe", Locations = MenuLocations.Background | MenuLocations.Folders };
        _writer.Apply([entry]);

        Assert.Equal(2, _writer.RemoveAll());
        Assert.Empty(_writer.FindOwnedKeys());
    }

    [Fact]
    public void Environment_variables_are_stored_as_expandable_strings()
    {
        var entry = new MenuEntry { Text = "Cmd", Program = @"%SystemRoot%\System32\cmd.exe" };
        _writer.Apply([entry]);

        using RegistryKey? command = Open($@"{RegistryWriter.BackgroundShellPath}\{RegistryWriter.KeyName(0, entry)}\command");
        Assert.Equal(RegistryValueKind.ExpandString, command!.GetValueKind(null));
    }
}
