using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace ContextMenuManager.Core;

/// <summary>
/// Writes entries as static shell verbs under <c>HKEY_CURRENT_USER\Software\Classes</c>.
/// </summary>
/// <remarks>
/// <para>Only the current user is affected and no administrator rights are needed.</para>
/// <para>
/// Every key the app creates starts with <see cref="KeyPrefix"/> and carries the
/// <see cref="MarkerName"/> value. The app only ever changes or deletes keys that have
/// both, so menu items added by Windows or other programs are never touched.
/// </para>
/// </remarks>
public sealed class RegistryWriter
{
    public const string KeyPrefix = "ContextMenuManager.";
    public const string MarkerName = "CreatedBy";
    public const string MarkerValue = "ContextMenuManager";

    public const string BackgroundShellPath = @"Directory\Background\shell";
    public const string FolderShellPath = @"Directory\shell";
    public const string FileShellPath = @"*\shell";
    public const string DriveShellPath = @"Drive\shell";
    public const string FileAssociationsPath = "SystemFileAssociations";

    private static readonly MenuLocations[] s_allLocations =
        [MenuLocations.Background, MenuLocations.Folders, MenuLocations.Files, MenuLocations.Drives];

    private readonly Func<RegistryKey> _openClassesRoot;
    private readonly bool _notifyShell;

    /// <param name="classesRootPath">
    /// The key under HKEY_CURRENT_USER that plays the role of <c>Software\Classes</c>.
    /// Tests and the sandbox mode pass a throwaway key here.
    /// </param>
    public RegistryWriter(string classesRootPath = @"Software\Classes", bool notifyShell = true)
    {
        ClassesRootPath = classesRootPath;
        _notifyShell = notifyShell;
        _openClassesRoot = () => Registry.CurrentUser.CreateSubKey(classesRootPath, writable: true);
    }

    public string ClassesRootPath { get; }

    /// <summary>The shell keys (relative to the classes root) an entry is written to.</summary>
    public static IReadOnlyList<string> ShellPathsFor(MenuLocations location, IReadOnlyList<string> fileTypes) => location switch
    {
        MenuLocations.Background => [BackgroundShellPath],
        MenuLocations.Folders => [FolderShellPath],
        MenuLocations.Drives => [DriveShellPath],
        MenuLocations.Files when fileTypes.Count == 0 => [FileShellPath],
        MenuLocations.Files => [.. fileTypes.Select(ext => $@"{FileAssociationsPath}\{ext}\shell")],
        _ => [],
    };

    /// <summary>Replaces everything the app wrote before with the enabled <paramref name="entries"/>.</summary>
    public void Apply(IReadOnlyList<MenuEntry> entries)
    {
        using RegistryKey classes = _openClassesRoot();
        RemoveOwnedKeys(classes);

        List<MenuEntry> topLevel = [.. entries.Where(e => !e.IsChild && e.Enabled)];
        for (int order = 0; order < topLevel.Count; order++)
        {
            MenuEntry entry = topLevel[order];
            List<MenuEntry> children = [.. entries.Where(e => e.ParentId == entry.Id && e.Enabled)];

            foreach (MenuLocations location in s_allLocations.Where(l => entry.Locations.HasFlag(l)))
            {
                foreach (string shellPath in ShellPathsFor(location, entry.FileTypeList))
                {
                    using RegistryKey shell = classes.CreateSubKey(shellPath, writable: true);
                    WriteEntry(shell, KeyName(order, entry), entry, location, children, isTopLevel: true);
                }
            }
        }

        NotifyShell();
    }

    /// <summary>Deletes every key the app created. Returns how many menu items were removed.</summary>
    public int RemoveAll()
    {
        using RegistryKey classes = _openClassesRoot();
        int removed = RemoveOwnedKeys(classes);
        NotifyShell();
        return removed;
    }

    /// <summary>Paths (relative to the classes root) of all keys the app created.</summary>
    public IReadOnlyList<string> FindOwnedKeys()
    {
        using RegistryKey classes = _openClassesRoot();
        var result = new List<string>();

        foreach (string shellPath in EnumerateShellPaths(classes))
        {
            using RegistryKey? shell = classes.OpenSubKey(shellPath);
            if (shell is null)
            {
                continue;
            }

            foreach (string name in shell.GetSubKeyNames())
            {
                if (IsOwned(shell, name))
                {
                    result.Add(shellPath + "\\" + name);
                }
            }
        }

        return result;
    }

    internal static string KeyName(int order, MenuEntry entry) => $"{KeyPrefix}{order:D3}.{entry.Id.ToString("N")[..8]}";

    private static void WriteEntry(RegistryKey shell, string keyName, MenuEntry entry, MenuLocations location, List<MenuEntry> children, bool isTopLevel)
    {
        using RegistryKey key = shell.CreateSubKey(keyName, writable: true);

        if (isTopLevel)
        {
            key.SetValue(MarkerName, MarkerValue, RegistryValueKind.String);
        }

        key.SetValue(string.Empty, entry.Text, RegistryValueKind.String);
        key.SetValue("MUIVerb", entry.Text, RegistryValueKind.String);

        string icon = entry.EffectiveIcon;
        if (icon.Length > 0)
        {
            key.SetValue("Icon", icon, KindFor(icon));
        }

        if (isTopLevel && entry.Position != MenuPosition.Default)
        {
            key.SetValue("Position", entry.Position.ToString(), RegistryValueKind.String);
        }

        if (isTopLevel && entry.ShiftOnly)
        {
            key.SetValue("Extended", string.Empty, RegistryValueKind.String);
        }

        if (entry.SeparatorBefore)
        {
            key.SetValue("SeparatorBefore", string.Empty, RegistryValueKind.String);
        }

        if (entry.SeparatorAfter)
        {
            key.SetValue("SeparatorAfter", string.Empty, RegistryValueKind.String);
        }

        if (entry.Kind == EntryKind.Submenu)
        {
            // An empty SubCommands value tells Explorer to read the submenu from the "shell" subkey.
            key.SetValue("SubCommands", string.Empty, RegistryValueKind.String);
            using RegistryKey subShell = key.CreateSubKey("shell", writable: true);
            for (int i = 0; i < children.Count; i++)
            {
                string childName = $"{i:D3}.{children[i].Id.ToString("N")[..8]}";
                WriteEntry(subShell, childName, children[i], location, [], isTopLevel: false);
            }
        }
        else
        {
            string command = CommandBuilder.BuildRegistryCommand(entry, location);
            using RegistryKey commandKey = key.CreateSubKey("command", writable: true);
            commandKey.SetValue(string.Empty, command, KindFor(entry.Program));
        }
    }

    private static RegistryValueKind KindFor(string value) =>
        CommandBuilder.ContainsEnvironmentVariable(value) ? RegistryValueKind.ExpandString : RegistryValueKind.String;

    private static int RemoveOwnedKeys(RegistryKey classes)
    {
        int removed = 0;
        foreach (string shellPath in EnumerateShellPaths(classes))
        {
            using RegistryKey? shell = classes.OpenSubKey(shellPath, writable: true);
            if (shell is null)
            {
                continue;
            }

            foreach (string name in shell.GetSubKeyNames())
            {
                if (IsOwned(shell, name))
                {
                    shell.DeleteSubKeyTree(name, throwOnMissingSubKey: false);
                    removed++;
                }
            }
        }

        return removed;
    }

    /// <summary>A key is ours only if it has our prefix and our marker value.</summary>
    private static bool IsOwned(RegistryKey shell, string name)
    {
        if (!name.StartsWith(KeyPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        using RegistryKey? key = shell.OpenSubKey(name);
        return key?.GetValue(MarkerName) is string marker && marker == MarkerValue;
    }

    private static IEnumerable<string> EnumerateShellPaths(RegistryKey classes)
    {
        yield return BackgroundShellPath;
        yield return FolderShellPath;
        yield return FileShellPath;
        yield return DriveShellPath;

        using RegistryKey? associations = classes.OpenSubKey(FileAssociationsPath);
        if (associations is null)
        {
            yield break;
        }

        foreach (string ext in associations.GetSubKeyNames())
        {
            yield return $@"{FileAssociationsPath}\{ext}\shell";
        }
    }

    private void NotifyShell()
    {
        if (_notifyShell)
        {
            // Tells Explorer that file associations changed, so open windows pick up the new menu.
            SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, 0, 0);
        }
    }

    private const int SHCNE_ASSOCCHANGED = 0x08000000;
    private const uint SHCNF_IDLIST = 0;

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(int wEventId, uint uFlags, nint dwItem1, nint dwItem2);
}
