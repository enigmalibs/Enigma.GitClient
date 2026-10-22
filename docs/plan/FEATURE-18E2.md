# FEATURE-18E2 — Windows publish script

**Status:** TODO
**Type:** FEATURE
**Branch:** `feature/feature-18e2-windows-publish`
**Run:** vibe/2026-10-22-history-ages-win-publish

## Objective

The Windows release build is published with:

```powershell
dotnet publish .\src\Enigma.GitClient.Desktop\ -c Release -r win-x64 -p:PublishReadyToRun=true
```

Nobody remembers that command. A PowerShell script in `packaging/windows/` runs it.

## Context & constraints

- `packaging/linux/` already holds the Linux `install.sh` / `uninstall.sh`. Each has a header comment
  saying what it does. `packaging/windows/` does not exist yet.
- The command is framework-dependent: `-r` without `--self-contained`, which the .NET 8+ SDK does not
  imply. It is compiled ReadyToRun. The output goes to the project's default
  `bin/Release/net10.0/win-x64/publish/`, and `dotnet publish` prints that path itself.
- `.gitattributes` makes every text file LF, `.ps1` included. Only `*.bat` / `*.cmd` are CRLF.
- Windows PowerShell 5.1 reads a BOM-less script as ANSI, so the script stays ASCII.
- The default execution policy of Windows PowerShell on a client is `Restricted`.
- `docs/RELEASE.md` §5 shows a generic `dotnet publish` and §6 says "Windows has no installer:
  publish as in step 5".

## Steps

1. `packaging/windows/publish.ps1`:
   - a header comment: what it builds, where the result lands, how to run it (including the
     `-ExecutionPolicy Bypass` form);
   - `Set-StrictMode -Version Latest` and `$ErrorActionPreference = 'Stop'`;
   - the project path resolved from `$PSScriptRoot`, so the script works from any directory;
   - the exact command;
   - exits with `dotnet`'s exit code.
2. `docs/RELEASE.md`: §5 names the script for Windows, and §6's Windows line points to it.

## Acceptance criteria

- `packaging/windows/publish.ps1` runs from the repository root and from another directory, and
  produces `src/Enigma.GitClient.Desktop/bin/Release/net10.0/win-x64/publish/Enigma.GitClient.Desktop.exe`.
- A failing publish makes the script exit with a non-zero code.
- The script parses under Windows PowerShell 5.1 and is pure ASCII.
- `docs/RELEASE.md` names the script.
- Nothing else changes: no solution change and no build change.

## Out of scope

- An installer (MSI, WixSharp) or a self-contained or single-file variant.
- Parameters (runtime identifier, output folder). The script is the command the user runs today.
- Changing `.gitattributes` for `*.ps1` (line endings are recommendation-only).

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| File name | `packaging/windows/publish.ps1` | Says what it does, next to `linux/install.sh` | `build.ps1` (it publishes), `install.ps1` (it installs nothing) |
| Parameters | None: the exact command | "A script that contains this command"; no gold-plating | `-Runtime` / `-Output` / pass-through arguments |
| Working directory | Project path from `$PSScriptRoot` | Runs from anywhere; never changes the caller's location | `Push-Location` to the root; assuming the repository root |
| Failure | `Stop` preference + `exit $LASTEXITCODE` | A failed publish must fail the script | Ignoring the exit code |
| Encoding | ASCII | Windows PowerShell 5.1 misreads BOM-less UTF-8 | UTF-8 with BOM (the repo's other files have none) |
| Docs | `docs/RELEASE.md` §5/§6 name it | The runbook is where the command is looked up | README (it documents Linux install, not release publishing) |
