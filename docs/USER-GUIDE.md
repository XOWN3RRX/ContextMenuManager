# User guide

## The main window

- **Left: Live preview.** A pretend desktop with a folder (*Documents*), a file (*Notes.txt*) and a drive (*Local Disk (C:)*). Right-click any of them, or the empty desktop, to open the menu. Use **Windows 11 / Windows 10** to switch the menu style and **Hold Shift** to see entries that only show while Shift is held (holding the real Shift key while you right-click works too).
- **Right: Menu entries.** Your entries in menu order. Selecting one opens the preview menu where it appears, with the entry outlined. The switch on each row turns it on (in your real menu) or off (kept in the app only).

## Adding an entry from a template

1. Click **Templates** (or press **Ctrl+T**).
2. Click **Add** next to the template you want. It goes into your menu right away.
3. Templates whose program isn't installed show **Not installed**.

## Creating your own entry

Click **New entry** (or press **Ctrl+N**). The editor shows, below the fields, the exact command Windows will run, and points out problems as you type.

| Field | What it does |
| --- | --- |
| **Type** | *Command* runs a program. *Submenu* is a folder for other entries. |
| **Name** | The text shown in the menu. |
| **Inside submenu** | Puts the entry in one of your submenus (create the submenu first). Items in a submenu use the submenu's places. |
| **Program** | The program to run. You can use environment variables such as `%ProgramFiles%` or `%SystemRoot%`. |
| **Arguments** | Passed to the program. `{path}` is replaced with the folder, file or drive you right-clicked. Put it in quotes, `"{path}"`, so paths with spaces work. **+ {path}** inserts it for you. |
| **Icon** | An `.ico`, `.exe` or `.dll` file, optionally with an index: `shell32.dll,27`. Empty uses the program's own icon. |
| **Show when you right-click** | Desktop (and the empty area of folder windows), Folders, Files, Drives. Pick one or more. |
| **Only these file types** | With *Files*: limit to extensions like `.txt; .md`. Empty means every file. |
| **Position** | *Default*, *Top of the menu* or *Bottom of the menu*. |
| **Only with Shift** | The entry is hidden unless you hold Shift while right-clicking. |
| **Line above / below** | Adds a separator line. |

### Examples

| Name | Program | Arguments | Show on |
| --- | --- | --- | --- |
| Open in VS Code | `%LocalAppData%\Programs\Microsoft VS Code\Code.exe` | `"{path}"` | Desktop, Folders, Files |
| Open Git Bash here | `%ProgramFiles%\Git\git-bash.exe` | `"--cd={path}"` | Desktop, Folders, Drives |
| Command Prompt here | `%SystemRoot%\System32\cmd.exe` | `/s /k pushd "{path}"` | Desktop, Folders |
| Edit with Notepad | `%SystemRoot%\System32\notepad.exe` | `"{path}"` | Files (`.txt; .log`) |
| Open a website | `explorer.exe` | `"https://example.com"` | Desktop |

Tip: for advanced uses you can also write Explorer's own placeholders, `%1` (clicked item) and `%V` (clicked folder), instead of `{path}`.

## Trying an entry

- Click the entry in the preview menu. A card explains what happens and shows the command, with `{path}` filled in.
- **Run it now** runs the command for real, using a sample item: your Desktop folder (desktop), your Documents folder (folders), a sample `Notes.txt` in your temp folder (files) or your system drive (drives). Commands that look like they could shut down, sign out or delete things ask first.
- Or right-click an entry in the list and choose **Test run**.

## Organizing entries

Right-click an entry in the list (or use the keyboard):

| Action | Keyboard |
| --- | --- |
| Edit | Enter or double-click |
| Turn on / off | Space, or click the switch |
| Move up / down (within its level) | Alt+Up / Alt+Down |
| Delete | Del |
| Duplicate, Test run, Show in preview, Add item to submenu | right-click menu |
| Search | Ctrl+F |
| Refresh the preview (re-read other programs' items) | F5 |

## Import and export

Use **⋯ → Export entries** to save your list as a `.json` file, for a backup or to share. **⋯ → Import entries** adds entries from such a file. Before importing, the app lists what each entry runs; imported entries are added **turned off**, so you can check them before switching them on. Only import files you trust, just as you would only run programs you trust.

## Settings

- **Full context menu on Windows 11**: skips *Show more options*. Explorer needs a restart; the app offers to do it (your open folder windows close and the taskbar blinks).
- **Restart Explorer**: restarts Explorer now.
- **Data folder**: where your list is saved (`%AppData%\ContextMenuManager`).
- **Remove everything this app added**: takes all your entries out of the Windows menu and turns them off in the list.

## Troubleshooting

**I don't see my entry in the real menu.**
- On Windows 11, look under **Show more options**, or Shift + right-click.
- Check that the entry is turned on, and that you right-click the right kind of item (desktop, folder, file or drive).
- If it is *Only with Shift*, hold Shift while right-clicking.
- If you limited it to file types, check the extension.

**Clicking the entry does nothing or shows an error.**
- Use **Test run** in the app: it shows Windows' error message.
- Check the program path. The editor warns when the file doesn't exist.
- Make sure `{path}` is in quotes: `"{path}"`.

**Paths ending in a backslash break the command (for example `C:\`).**
Some programs read `"C:\"` as an escaped quote. Use `"{path}\."` or `"{path}."` for programs that expect a folder.

**The preview looks a little different from my real menu.**
Items from shell extensions decide at run time whether to show, so the preview can't always know about them. Your own entries are shown exactly as they are written.

**How do I get rid of everything?**
**⋯ → Remove everything this app added**, then delete the app folder (and optionally `%AppData%\ContextMenuManager`).
