namespace ContextMenuManager.Core;

/// <summary>A ready-made entry the user can add with one click.</summary>
public sealed class EntryTemplate
{
    public required string Name { get; init; }

    public required string Description { get; init; }

    public required string Category { get; init; }

    /// <summary>Builds the entries (a submenu template returns the submenu first, then its children).</summary>
    public required Func<string, IReadOnlyList<MenuEntry>> Create { get; init; }

    /// <summary>The program the template needs, or null when it always works.</summary>
    public string? RequiredProgram { get; init; }

    /// <summary>Where the template looks for its program, in order. The first one found wins.</summary>
    public IReadOnlyList<string> ProgramCandidates { get; init; } = [];

    /// <summary>The program path found on this PC (with environment variables kept), or null.</summary>
    public string? FindProgram() => ProgramCandidates.FirstOrDefault(p => File.Exists(Environment.ExpandEnvironmentVariables(p)));

    public bool IsAvailable => ProgramCandidates.Count == 0 || FindProgram() is not null;
}

/// <summary>The built-in templates. Every command here only uses this app, programs that ship with Windows, or programs the user installed.</summary>
public static class Templates
{
    private const MenuLocations FolderPlaces = MenuLocations.Background | MenuLocations.Folders | MenuLocations.Drives;

    /// <summary>Makes the app copy the path that follows to the clipboard and exit, without opening a window.</summary>
    public const string CopyPathSwitch = "--copy-path";

    public static IReadOnlyList<EntryTemplate> All { get; } =
    [
        Simple("Open Command Prompt here", "Opens cmd.exe in the folder you right-clicked.", "Terminals",
            @"%SystemRoot%\System32\cmd.exe", "/s /k pushd \"{path}\"", FolderPlaces),

        Simple("Open PowerShell here", "Opens Windows PowerShell in the folder you right-clicked.", "Terminals",
            @"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe", "-NoExit -Command Set-Location -LiteralPath '{path}'", FolderPlaces),

        Detected("Open PowerShell 7 here", "Opens PowerShell 7 (pwsh) in the folder you right-clicked.", "Terminals",
            [@"%ProgramFiles%\PowerShell\7\pwsh.exe"], "-NoExit -Command Set-Location -LiteralPath '{path}'", FolderPlaces),

        Detected("Open Windows Terminal here", "Opens Windows Terminal in the folder you right-clicked.", "Terminals",
            [@"%LocalAppData%\Microsoft\WindowsApps\wt.exe"], "-d \"{path}\\.\"", FolderPlaces),

        Detected("Open Git Bash here", "Opens Git Bash in the folder you right-clicked.", "Developer",
            [@"%ProgramFiles%\Git\git-bash.exe", @"%LocalAppData%\Programs\Git\git-bash.exe"], "\"--cd={path}\"", FolderPlaces),

        Detected("Open with Visual Studio Code", "Opens the folder or file you right-clicked in VS Code.", "Developer",
            [@"%LocalAppData%\Programs\Microsoft VS Code\Code.exe", @"%ProgramFiles%\Microsoft VS Code\Code.exe"], "\"{path}\"",
            MenuLocations.Background | MenuLocations.Folders | MenuLocations.Files),

        Detected("Edit with Notepad++", "Opens the file you right-clicked in Notepad++.", "Developer",
            [@"%ProgramFiles%\Notepad++\notepad++.exe", @"%ProgramFiles(x86)%\Notepad++\notepad++.exe"], "\"{path}\"", MenuLocations.Files),

        Simple("Open with Notepad", "Opens the file you right-clicked in Notepad.", "Files",
            @"%SystemRoot%\System32\notepad.exe", "\"{path}\"", MenuLocations.Files),

        // Runs this app with --copy-path instead of PowerShell: no console window flashes up and it
        // finishes in a fraction of the time PowerShell needs to start.
        new()
        {
            Name = "Copy path",
            Description = "Copies the full path of the item you right-clicked to the clipboard.",
            Category = "Files",
            Create = _ =>
            [
                new MenuEntry
                {
                    Text = "Copy path",
                    Program = Environment.ProcessPath ?? string.Empty,
                    Arguments = CopyPathSwitch + " \"{path}\"",
                    Locations = MenuLocations.Background | MenuLocations.Folders | MenuLocations.Files | MenuLocations.Drives,
                    Icon = @"%SystemRoot%\System32\imageres.dll,-5302",
                },
            ],
        },

        Simple("Task Manager", "Opens Task Manager.", "System",
            @"%SystemRoot%\System32\Taskmgr.exe", string.Empty, MenuLocations.Background),

        Simple("Control Panel", "Opens the classic Control Panel.", "System",
            @"%SystemRoot%\System32\control.exe", string.Empty, MenuLocations.Background),

        Simple("Restart Explorer", "Restarts Windows Explorer (the desktop and taskbar). Handy when they act up.", "System",
            @"%SystemRoot%\System32\cmd.exe", "/c taskkill /f /im explorer.exe & start explorer.exe", MenuLocations.Background,
            icon: @"%SystemRoot%\explorer.exe"),

        new()
        {
            Name = "Power menu",
            Description = "A submenu with Lock, Sign out, Restart and Shut down.",
            Category = "System",
            Create = _ =>
            {
                var menu = new MenuEntry
                {
                    Kind = EntryKind.Submenu,
                    Text = "Power",
                    Icon = @"%SystemRoot%\System32\shell32.dll,27",
                    Locations = MenuLocations.Background,
                    Position = MenuPosition.Bottom,
                };

                return
                [
                    menu,
                    Child(menu, "Lock", @"%SystemRoot%\System32\rundll32.exe", "user32.dll,LockWorkStation", @"%SystemRoot%\System32\imageres.dll,-59"),
                    Child(menu, "Sign out", @"%SystemRoot%\System32\shutdown.exe", "/l", @"%SystemRoot%\System32\imageres.dll,-5373"),
                    Child(menu, "Restart", @"%SystemRoot%\System32\shutdown.exe", "/r /t 0", @"%SystemRoot%\System32\shell32.dll,238", separatorBefore: true),
                    Child(menu, "Shut down", @"%SystemRoot%\System32\shutdown.exe", "/s /t 0", @"%SystemRoot%\System32\shell32.dll,27"),
                ];
            },
        },
    ];

    private static EntryTemplate Simple(string name, string description, string category, string program, string arguments, MenuLocations locations, string? icon = null) => new()
    {
        Name = name,
        Description = description,
        Category = category,
        Create = _ => [new MenuEntry { Text = name, Program = program, Arguments = arguments, Locations = locations, Icon = icon ?? string.Empty }],
    };

    private static EntryTemplate Detected(string name, string description, string category, string[] candidates, string arguments, MenuLocations locations) => new()
    {
        Name = name,
        Description = description,
        Category = category,
        RequiredProgram = Path.GetFileName(candidates[0]),
        ProgramCandidates = candidates,
        Create = program => [new MenuEntry { Text = name, Program = program, Arguments = arguments, Locations = locations }],
    };

    private static MenuEntry Child(MenuEntry parent, string text, string program, string arguments, string icon, bool separatorBefore = false) => new()
    {
        Text = text,
        Program = program,
        Arguments = arguments,
        Icon = icon,
        ParentId = parent.Id,
        Locations = parent.Locations,
        SeparatorBefore = separatorBefore,
    };

    /// <summary>Creates the entries for a template, using the program found on this PC.</summary>
    public static IReadOnlyList<MenuEntry> Instantiate(EntryTemplate template) =>
        template.Create(template.FindProgram() ?? string.Empty);
}
