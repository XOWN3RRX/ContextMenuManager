# Code signing policy

Free code signing provided by [SignPath.io](https://about.signpath.io), certificate by [SignPath Foundation](https://signpath.org).

> **Status:** the project has applied for SignPath Foundation signing. Until it is approved, releases are not signed. This page describes how signing works once it is.

## What is signed

Only the program built from this repository is signed: `ContextMenuManager.exe` for `win-x64` and `win-arm64`, as published on the [Releases page](https://github.com/XOWN3RRX/ContextMenuManager/releases).

- Every signed file is built by the [Build workflow](../.github/workflows/build.yml) on GitHub-hosted runners, from the source code of a tagged commit (`v*`) in this repository. Nothing built on a personal computer is signed.
- The project contains no closed-source code. Its only dependency is the .NET runtime from Microsoft.
- The signing request is checked against [`.signpath/artifact-configuration.xml`](../.signpath/artifact-configuration.xml): only `ContextMenuManager.exe` with the product name *Context Menu Manager* and the version of the release tag can be signed.
- Every release is approved by hand before it is signed.

## Team roles

| Role | Who |
| --- | --- |
| Committers and reviewers | [Repository owner](https://github.com/XOWN3RRX) |
| Approvers | [Repository owner](https://github.com/XOWN3RRX) |

- **Committers** may change the source code directly.
- **Reviewers** review every change from other contributors (pull requests) before it is merged.
- **Approvers** approve each signing request.

All team members use multi-factor authentication for GitHub and SignPath.

## Privacy policy

This program will not transfer any information to other networked systems unless specifically requested by the user or the person installing or operating it.

See [PRIVACY.md](../PRIVACY.md) for what the app stores on your PC.

## Checking a signature

Right-click `ContextMenuManager.exe`, choose **Properties → Digital Signatures**, and check that the signer is **SignPath Foundation**. Or in PowerShell:

```powershell
Get-AuthenticodeSignature .\ContextMenuManager.exe | Format-List Status, SignerCertificate
```

A signed program can still show a SmartScreen warning for a while after a new release, until Windows has seen enough downloads of it.

## Reporting misuse

If you think a file was signed that shouldn't have been, report it privately through the repository's **Security** tab (see [SECURITY.md](../SECURITY.md)), or to SignPath at support@signpath.io.
