# Contributing

Thanks for helping! Bug reports, ideas, translations of wording, templates and code are all welcome.

## Reporting bugs and ideas

Open an [issue](https://github.com/XOWN3RRX/ContextMenuManager/issues) using the templates. For bugs, include your Windows version (Win+R → `winver`), what you did, what you expected, and a screenshot if it helps. Please don't paste personal paths or data you'd rather keep private.

For security problems, follow [SECURITY.md](SECURITY.md) instead of opening a public issue.

## Building

- Windows 10 or 11
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- Visual Studio 2026 (optional) or any editor

```bash
dotnet build ContextMenuManager.slnx
```

```bash
dotnet test ContextMenuManager.slnx
```

## Trying changes safely: sandbox mode

```bash
dotnet run --project src/ContextMenuManager -- --sandbox
```

In sandbox mode the app:

- writes to `HKCU\Software\ContextMenuManager.Sandbox\Classes` instead of `HKCU\Software\Classes`, so **your real context menu is never changed**,
- keeps its list in `%TEMP%\ContextMenuManager.Sandbox`,
- shows a default Windows 11 menu in the preview and disables the settings that change Windows itself (full context menu, restart Explorer).

Clean up afterwards with:

```bash
reg delete "HKCU\Software\ContextMenuManager.Sandbox" /f
```

The unit tests also use a throwaway key (`HKCU\Software\ContextMenuManager.Tests\<random>`) and delete it when they finish.

## Project structure

| Folder | What's inside |
| --- | --- |
| `src/ContextMenuManager.Core` | The model (`MenuEntry`), `RegistryWriter`, `EntryStore` (JSON), `CommandBuilder`, `ShellVerbReader`, `Templates`. No UI; everything important is unit tested here. |
| `src/ContextMenuManager` | The WinForms app. `Forms/` has the windows, `Preview/` the pretend desktop and simulated menus, `UI/` the theme and custom controls. |
| `tests/ContextMenuManager.Tests` | xUnit tests. |
| `tools/` | `generate-icon.ps1` draws the app icon from code. |
| `docs/` | User guide, how it works, screenshots. |

See [docs/HOW-IT-WORKS.md](docs/HOW-IT-WORKS.md) for the registry layout.

## Ground rules for code

- **Never touch keys the app didn't create.** Anything that deletes or changes registry keys must check both the `ContextMenuManager.` prefix and the `CreatedBy` marker (see `RegistryWriter.IsOwned`).
- **No network access, telemetry or analytics.** The app must keep working fully offline. Opening a web page in the user's browser after they click a link is fine.
- **No administrator rights.** Write to `HKEY_CURRENT_USER` only.
- **No new dependencies** without a good reason; the app currently uses only .NET itself.
- Keep UI text short, friendly and in plain words. Match the existing Windows 11 look (`UI/Theme.cs`).
- Add or update tests for changes in `ContextMenuManager.Core`.
- The build must have no warnings.

## Adding a template

Add it to `Templates.All` in `src/ContextMenuManager.Core/Templates.cs`. Use only programs that ship with Windows or that the user installed (use `Detected(...)` with the usual install paths so the template is greyed out when the program is missing). Prefer environment variables (`%ProgramFiles%`, `%LocalAppData%`) over fixed paths.

## Pull requests

1. Fork the repository and create a branch from `main`.
2. Make your change, with tests where it makes sense.
3. Run `dotnet build` and `dotnet test`.
4. Describe what you changed and why; add a screenshot for UI changes.
5. Add a line to [CHANGELOG.md](CHANGELOG.md) under *Unreleased*.

By contributing you agree that your contribution is licensed under the [MIT License](LICENSE), and that you have the right to submit it (it's your own work or compatible with MIT). Don't include code, icons or images copied from other projects unless their license allows it and you note the source.

Please follow the [Code of Conduct](CODE_OF_CONDUCT.md).

## Releasing (maintainers)

1. Update the `<Version>` in `src/ContextMenuManager/ContextMenuManager.csproj` and the changelog.
2. Tag the commit: `git tag v1.0.0` and push the tag.
3. The *Build* workflow runs the tests, publishes `win-x64` and `win-arm64` builds and attaches them to a draft GitHub release. Review the draft and publish it.
