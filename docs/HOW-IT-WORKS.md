# How it works

Context Menu Manager adds **static shell verbs**: the simplest, documented way Windows offers to put an item in the Explorer context menu. There are no shell extensions, DLLs or background services. When you click an entry, Windows Explorer itself starts the program; Context Menu Manager doesn't need to be running.

## Where the entries are written

Everything is written under `HKEY_CURRENT_USER\Software\Classes`, so:

- only your Windows account is affected,
- no administrator rights are needed (the app runs "as invoker"),
- Windows merges these keys into `HKEY_CLASSES_ROOT`, which is what Explorer reads.

| "Show when you right-click" | Registry key (under `HKCU\Software\Classes`) | `{path}` becomes |
| --- | --- | --- |
| Desktop (and the empty area of any folder window) | `Directory\Background\shell` | `%V` (the folder) |
| Folders | `Directory\shell` | `%1` (the folder) |
| Files (all) | `*\shell` | `%1` (the file) |
| Files (only some types, e.g. `.txt`) | `SystemFileAssociations\.txt\shell` | `%1` (the file) |
| Drives | `Drive\shell` | `%1` (for example `C:\`) |

## What one entry looks like

An entry named "Open Git Bash here", shown on the desktop background, becomes:

```text
HKCU\Software\Classes\Directory\Background\shell\ContextMenuManager.000.1a2b3c4d
    (Default)      = Open Git Bash here
    MUIVerb        = Open Git Bash here
    Icon           = %ProgramFiles%\Git\git-bash.exe          (REG_EXPAND_SZ)
    CreatedBy      = ContextMenuManager                       (ownership marker)
    Position       = Top | Bottom                             (only if not "Default")
    Extended       = ""                                       (only if "Only with Shift")
    SeparatorBefore / SeparatorAfter = ""                     (only if turned on)
    \command
        (Default)  = "%ProgramFiles%\Git\git-bash.exe" "--cd=%V"
```

- The key name is `ContextMenuManager.<order>.<id>`. The order number keeps your entries in the order of your list.
- Values that contain environment variables (like `%ProgramFiles%`) are stored as `REG_EXPAND_SZ` so Windows expands them. This also keeps exported files portable between PCs.

### Submenus

A submenu gets `SubCommands = ""` (an empty string tells Explorer to read the items from the `shell` subkey) and its items go below it:

```text
...\shell\ContextMenuManager.002.5e6f7a8b
    MUIVerb      = Power
    SubCommands  = ""
    \shell
        \000.9c0d1e2f    MUIVerb = Lock      \command = rundll32.exe user32.dll,LockWorkStation
        \001.3a4b5c6d    MUIVerb = Restart   \command = shutdown.exe /r /t 0
```

## Why it is safe for other programs' items

The app only changes or deletes a key when **both** are true:

1. its name starts with `ContextMenuManager.`, and
2. it contains the value `CreatedBy = ContextMenuManager`.

Items added by Windows or other programs (for example Git's own `git_shell` key) are only ever **read**, to show them in the preview.

Every change is applied the same way: the app removes all of its own keys and writes the entries that are turned on again. This keeps the registry exactly in sync with your list and never leaves stale items behind. Afterwards it calls `SHChangeNotify(SHCNE_ASSOCCHANGED)` so open Explorer windows pick up the change; no restart is needed.

**Remove everything this app added** deletes all marked keys and turns every entry in your list off.

## Your saved list

Your entries are stored in `%AppData%\ContextMenuManager\entries.json` (written atomically through a temporary file). If the file is ever damaged, the app keeps a `entries.json.broken-<date>` backup and starts with an empty list. Small preferences, such as the preview style, are in `settings.json` next to it.

The registry is the "output"; the JSON file is the "source". If you delete the JSON file, your entries stay in the menu until you use **Remove everything this app added**.

## Windows 11 and "Show more options"

Windows 11 introduced a new, shorter context menu. Classic static verbs (from any program, not only this one) are shown in the full menu behind **Show more options**, or right away with Shift + right-click. Adding items to the new menu requires a packaged app with a COM shell extension, which is out of scope for a small, portable, no-admin tool.

The optional **Full context menu on Windows 11** setting uses a well-known, per-user and reversible tweak: it registers an empty `InprocServer32` for the new menu's COM class:

```text
HKCU\Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\InprocServer32
    (Default) = ""
```

Explorer must restart before it takes effect. Turning the setting off deletes that key again.

## Items from other programs

The **From other programs** section of the list is read from `HKEY_CLASSES_ROOT` (read only) for the same four places:

- **Static items**: the `shell` keys (`Directory\Background\shell`, `DesktopBackground\shell`, `Directory\shell`, `Folder\shell`, `*\shell`, `AllFilesystemObjects\shell`, `Drive\shell`). Windows' own verbs (open, print, runas...) and hidden ones (`LegacyDisable`, `ProgrammaticAccessOnly`, feature-flagged) are skipped.
- **Shell extensions**: the `shellex\ContextMenuHandlers` keys. The handler's CLSID is looked up to find its DLL. Handlers whose DLL is in the Windows folder (Windows' own), that have no 64-bit DLL (Explorer can't load them), or that were disabled by renaming with a leading `-` are skipped.

The program name shown next to an item comes from the version information of its program or DLL. Reading happens in the background when the app starts (and again with F5).

## The live preview

The preview is drawn by the app; it is not the real Explorer menu.

- **Your entries** come from your list (the selected entry is outlined, even when it's turned off).
- **Other programs' items** are read from `HKEY_CLASSES_ROOT` for the same locations. Hidden items (`LegacyDisable`, `ProgrammaticAccessOnly`, feature-flagged items) are skipped.
- **Windows' own items** (View, Sort by, Cut, Copy, Properties...) are drawn from a fixed list that matches a default Windows install.

Some items can't be predicted by reading the registry: shell extensions (COM context menu handlers) decide at run time what they show. So the preview is a close approximation of the real menu.

Clicking an item in the preview never runs anything. The app shows the command Windows would run, with `{path}` filled in for a sample item (your Desktop folder, your Documents folder, a sample `Notes.txt` in the temp folder, or the system drive). **Run it now** starts that command, and asks first if it looks like it could close programs, restart the PC or delete files.

## Project structure

```text
src/ContextMenuManager.Core   Model, registry writer, JSON store, templates (no UI, unit tested)
src/ContextMenuManager        WinForms app: main window, editor, preview, settings
tests/ContextMenuManager.Tests  xUnit tests (use a throwaway registry key, never the real menu)
tools/generate-icon.ps1       Draws the app icon from code
```
