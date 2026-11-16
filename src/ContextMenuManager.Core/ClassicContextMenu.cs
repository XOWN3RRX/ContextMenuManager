using Microsoft.Win32;

namespace ContextMenuManager.Core;

/// <summary>
/// Windows 11 shows a new, shorter context menu and moves most items (including the ones
/// this app adds) under "Show more options". This optional per-user setting brings back
/// the full classic menu. It is the well-known, reversible tweak of registering an empty
/// in-process server for the new menu's COM class under HKEY_CURRENT_USER.
/// </summary>
public static class ClassicContextMenu
{
    private const string ClsidPath = @"Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}";

    /// <summary>Windows 11 starts at build 22000.</summary>
    public static bool IsWindows11 => Environment.OSVersion.Version.Build >= 22000;

    public static bool IsEnabled
    {
        get
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(ClsidPath + @"\InprocServer32");
            return key is not null;
        }
    }

    /// <summary>Turns the classic menu on or off. Explorer must restart before it takes effect.</summary>
    public static void SetEnabled(bool enabled)
    {
        if (enabled)
        {
            using RegistryKey key = Registry.CurrentUser.CreateSubKey(ClsidPath + @"\InprocServer32", writable: true);
            key.SetValue(string.Empty, string.Empty, RegistryValueKind.String);
        }
        else
        {
            Registry.CurrentUser.DeleteSubKeyTree(ClsidPath, throwOnMissingSubKey: false);
        }
    }
}
