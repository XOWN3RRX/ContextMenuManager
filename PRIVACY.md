# Privacy

Context Menu Manager doesn't collect, send or share any data. There are no accounts, ads, analytics, crash reporting or update checks, and the app makes no network connections.

## What is stored on your PC

| What | Where |
| --- | --- |
| Your list of entries | `%AppData%\ContextMenuManager\entries.json` |
| App preferences (preview style) | `%AppData%\ContextMenuManager\settings.json` |
| The entries that are turned on | `HKEY_CURRENT_USER\Software\Classes\...\shell\ContextMenuManager.*` |
| A sample file for test runs on files | `%TEMP%\ContextMenuManager\Notes.txt` (created only when you test a file entry) |
| Full context menu setting (Windows 11, optional) | `HKEY_CURRENT_USER\Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}` |

All of it stays on your PC and can be removed: use **Remove everything this app added**, turn off the Windows 11 setting, and delete the folders above.

## What the app reads

To draw the preview, the app reads the context menu items other programs registered in `HKEY_CLASSES_ROOT`, and Windows' theme and accent color. This information is only displayed in the app and never saved or sent anywhere.

## Exported files

When you export your entries, the file contains your entries' names, program paths and arguments. Program paths can include your Windows user name (for example `C:\Users\<name>\...`). Check the file before sharing it publicly; using environment variables such as `%LocalAppData%` in your entries avoids this.

## Links

The **GitHub** button and the **Help and source code** menu item open the project page in your browser. Your browser, not the app, makes that connection.
