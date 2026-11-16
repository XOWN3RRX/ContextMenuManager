using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace ContextMenuManager.Core;

/// <summary>A menu item that Windows or another program registered (read only).</summary>
public sealed record ExistingVerb(
    string KeyName,
    string Text,
    string Icon,
    string Command,
    MenuPosition Position,
    bool ShiftOnly,
    bool SeparatorBefore,
    bool SeparatorAfter,
    IReadOnlyList<ExistingVerb>? Children,
    string SourcePath = "");

/// <summary>
/// Reads the static menu items other programs added (for example "Open Git Bash here"),
/// so the preview can show them next to the user's own entries. Nothing is written.
/// </summary>
/// <remarks>
/// Items added by shell extensions (COM handlers) can't be read this way, so the preview
/// is a close approximation of the real menu, not a pixel-perfect copy.
/// </remarks>
public static class ShellVerbReader
{
    // Verbs that Windows itself handles; the preview draws its own built-in items instead.
    private static readonly HashSet<string> s_builtInVerbs = new(StringComparer.OrdinalIgnoreCase)
    {
        "open", "opennewwindow", "opennewprocess", "opennewtab", "explore", "find", "edit", "print", "printto",
        "runas", "runasuser", "pintohome", "pintostartscreen", "properties", "cmd", "powershell", "openinterminal",
        "UpdateEncryptionSettings", "UpdateEncryptionSettingsWork", "change-passphrase", "change-pin",
        "encrypt-bde", "encrypt-bde-elev", "manage-bde", "resume-bde", "resume-bde-elev", "unlock-bde",
        "format", "Windows.Share", "Windows.ModernShare", "Display", "Personalize", "EditStickers",
    };

    public static IReadOnlyList<ExistingVerb> Read(MenuLocations location, string? fileExtension = null)
    {
        var paths = location switch
        {
            MenuLocations.Background => new List<string> { @"Directory\Background\shell", @"DesktopBackground\shell" },
            MenuLocations.Folders => [@"Directory\shell", @"Folder\shell"],
            MenuLocations.Drives => [@"Drive\shell"],
            MenuLocations.Files => [@"*\shell", @"AllFilesystemObjects\shell"],
            _ => [],
        };

        if (location == MenuLocations.Files && !string.IsNullOrEmpty(fileExtension))
        {
            paths.Add($@"SystemFileAssociations\{fileExtension}\shell");
        }

        var result = new List<ExistingVerb>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string path in paths)
        {
            try
            {
                using RegistryKey? shell = Registry.ClassesRoot.OpenSubKey(path);
                if (shell is null)
                {
                    continue;
                }

                foreach (string name in shell.GetSubKeyNames())
                {
                    // Names starting with "." are hidden Windows commands (for example Spotlight).
                    if (s_builtInVerbs.Contains(name) || name.StartsWith('.') || !seen.Add(name))
                    {
                        continue;
                    }

                    using RegistryKey? key = shell.OpenSubKey(name);
                    if (key is not null && ReadVerb(name, key, depth: 0, path + "\\" + name) is { } verb)
                    {
                        result.Add(verb);
                    }
                }
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
            {
                // Some keys are protected; skip them.
            }
        }

        return result;
    }

    private static ExistingVerb? ReadVerb(string name, RegistryKey key, int depth, string sourcePath)
    {
        // Skip items that are hidden, only for programs, or our own (the app draws those itself).
        if (key.GetValue("LegacyDisable") is not null
            || key.GetValue("ProgrammaticAccessOnly") is not null
            || key.GetValue("OnlyInBrowserWindow") is not null
            || key.GetValue("ShowBasedOnVelocityId") is not null
            || key.GetValue("HideBasedOnVelocityId") is not null
            // Command handlers decide at runtime whether and how they show; their text can't be read here.
            || key.GetValue("ExplorerCommandHandler") is not null && key.GetValue("MUIVerb") is null && key.GetValue(null) is null
            || key.GetValue(RegistryWriter.MarkerName) is string marker && marker == RegistryWriter.MarkerValue)
        {
            return null;
        }

        string text = ResolveText(key.GetValue("MUIVerb") as string)
            ?? ResolveText(key.GetValue(null) as string)
            ?? name;

        IReadOnlyList<ExistingVerb>? children = null;
        if (key.GetValue("SubCommands") is not null || key.GetValue("ExtendedSubCommandsKey") is not null)
        {
            var list = new List<ExistingVerb>();
            if (depth == 0)
            {
                using RegistryKey? subShell = key.OpenSubKey("shell");
                foreach (string childName in subShell?.GetSubKeyNames() ?? [])
                {
                    using RegistryKey? child = subShell!.OpenSubKey(childName);
                    if (child is not null && ReadVerb(childName, child, depth + 1, sourcePath + @"\shell\" + childName) is { } verb)
                    {
                        list.Add(verb);
                    }
                }
            }

            children = list;
        }

        using RegistryKey? command = key.OpenSubKey("command");

        return new ExistingVerb(
            name,
            text,
            key.GetValue("Icon") as string ?? string.Empty,
            command?.GetValue(null) as string ?? string.Empty,
            Enum.TryParse(key.GetValue("Position") as string, ignoreCase: true, out MenuPosition position) ? position : MenuPosition.Default,
            key.GetValue("Extended") is not null,
            key.GetValue("SeparatorBefore") is not null,
            key.GetValue("SeparatorAfter") is not null,
            children,
            sourcePath);
    }

    /// <summary>Turns "@shell32.dll,-8506" style resource strings into text and removes the "&amp;" access key marker.</summary>
    public static string? ResolveText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (value.StartsWith('@'))
        {
            var buffer = new StringBuilder(512);
            if (SHLoadIndirectString(value, buffer, buffer.Capacity, 0) != 0)
            {
                return null;
            }

            value = buffer.ToString();
        }

        return StripAccessKey(value);
    }

    public static string StripAccessKey(string value) => value.Replace("&&", "\u0001").Replace("&", "").Replace("\u0001", "&");

    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
    private static extern int SHLoadIndirectString(string pszSource, StringBuilder pszOutBuf, int cchOutBuf, nint ppvReserved);
}
