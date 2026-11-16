# Changelog

All notable changes are listed here. The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project uses [Semantic Versioning](https://semver.org/).

## [Unreleased]

### Added

- Reorder entries with drag and drop, including moving commands into and out of submenus.

## [1.0.0] - 2026-11-16

### Added

- Add, edit, turn on/off, reorder, duplicate and delete context menu entries.
- Entries for the desktop and folder background, folders, files (optionally only some file types) and drives.
- Submenus, separator lines, top/bottom position and Shift-only entries.
- Live preview: a pretend desktop with Windows 11 (compact and full) and Windows 10 menus, other programs' items, and click-to-see what each entry runs, with an optional test run.
- Templates: Command Prompt, PowerShell, PowerShell 7, Windows Terminal, Git Bash, VS Code, Notepad++, Notepad, Copy path, Task Manager, Control Panel, Restart Explorer and a Power submenu.
- Read-only list of the context menu items other programs added (static items and shell extensions), with preview highlighting, details and "make an editable copy".
- Import and export as JSON (imported entries start turned off).
- Optional full context menu on Windows 11, and Restart Explorer.
- Remove everything the app added with one click.
- Sandbox mode (`--sandbox`) for trying changes without touching the real menu.
- Light/dark theme and accent color, high-DPI support, keyboard shortcuts.
