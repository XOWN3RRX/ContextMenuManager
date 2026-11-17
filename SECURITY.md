# Security policy

## What the app can and can't do

Context Menu Manager is designed to have as little power as possible:

- It runs **without administrator rights** and only writes to your own part of the registry (`HKEY_CURRENT_USER\Software\Classes`).
- It only changes or deletes registry keys **it created itself** (marked with a name prefix and a `CreatedBy` value). Items from Windows and other programs are only read.
- It makes **no network connections**. It has no updater, telemetry or analytics.
- It **never runs a command on its own.** Commands only run when you click your entry in the real Explorer menu, or when you click **Run it now** / **Test run** in the app. Commands that look like they could shut down, sign out or delete things ask for confirmation first.
- **Imported entries are added turned off** and the app lists every command before importing, so a shared file can't silently add something to your menu.

Keep in mind that a context menu entry runs whatever program you tell it to, with your permissions. Only add or import entries you understand and trust.

## Supported versions

Security fixes are made for the latest release.

## Reporting a vulnerability

Please **don't open a public issue** for security problems.

Report it privately through GitHub: go to the repository's **Security** tab and choose **Report a vulnerability** (GitHub private vulnerability reporting). Include:

- what the problem is and what an attacker could do with it,
- steps to reproduce,
- the app version and your Windows version.

You'll get an answer as soon as possible. Once a fix is released, the report can be made public, with credit to you if you'd like.

## Verifying downloads

Releases are built by GitHub Actions from the tagged source code. Each release lists SHA-256 checksums of the zip files. You can check a download with:

```bash
certutil -hashfile ContextMenuManager-win-x64.zip SHA256
```

Code signing through SignPath Foundation has been requested (see the [code signing policy](docs/CODE-SIGNING.md)). Until then the executable is not signed, so Windows SmartScreen may show a warning the first time you run it. You can always build the app yourself from source instead.
