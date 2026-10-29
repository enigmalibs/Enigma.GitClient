# FEATURE-0D4E — Publish scripts for Windows and Linux

**Status:** DONE
**Type:** FEATURE
**Branch:** `feature/feature-0d4e-publish-scripts`
**Run:** feature/2026-10-29-uniform-enigma-apps

## Objective

One-command publish scripts on both platforms, part of the uniform baseline for every Enigma app:

- Windows — `publish.ps1` (exists) and a new `publish.bat`, both running
  `dotnet publish <Desktop project> -c Release -r win-x64 -p:PublishReadyToRun=true`;
- Linux — a new `publish.sh` next to `install.sh` / `uninstall.sh`, running
  `dotnet publish <Desktop project> -c Release -r linux-x64 -p:PublishReadyToRun=true`.

## Context & constraints

- `packaging/windows/publish.ps1` exists (resolves the project from `$PSScriptRoot`, passes
  `$LASTEXITCODE` on). `packaging/linux/install.sh` and `uninstall.sh` exist (`set -euo pipefail`,
  `script_dir`, `repo_root`, mode `100755` in the index).
- `.gitattributes` keeps `*.bat` CRLF and `*.sh` LF.
- `AssemblyNameConsistencyTests` (Desktop tests) ties the packaging files to the assembly name.
- `docs/RELEASE.md` §5 documents `publish.ps1`; the README documents the Linux installer only.
- Desktop tests are compiled, never run, on this Windows machine (user rule).

## Steps

1. `packaging/windows/publish.bat`: `@echo off`, `setlocal`, the project resolved from `%~dp0`, the
   command, `exit /b %ERRORLEVEL%`; a header comment in the `.ps1`'s spirit.
2. `packaging/linux/publish.sh`: `#!/usr/bin/env bash`, `set -euo pipefail`, the project resolved from
   the script's directory, the command (`exec`), a header comment like `install.sh`'s; staged with
   mode `100755`.
3. `AssemblyNameConsistencyTests`: every publish script names `src/Enigma.GitClient.Desktop` (the
   assembly's directory) and its runtime identifier with `-c Release` and `-p:PublishReadyToRun=true`.
4. `docs/RELEASE.md` §5 and the README: name the three scripts and where their output lands.
5. Verify: run `publish.bat` and `publish.ps1` (win-x64) and `publish.sh` through Git Bash
   (linux-x64); each exits 0 and leaves its `publish/` folder.

## Acceptance criteria

- The three scripts run from any directory and exit with `dotnet`'s exit code.
- `publish.sh` is executable in the index (`100755`); `publish.bat` is CRLF in the working tree.
- The Release build has zero warnings (Desktop tests compiled); the Core suites are green.

## Out of scope

- Self-contained publishing, other runtime identifiers, a `pack-linux` archive (install.sh covers
  those on Linux).

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| Where | `packaging/windows/publish.bat`, `packaging/linux/publish.sh` | Next to the existing scripts of each platform | A root `publish/` folder |
| `publish.bat` | Calls `dotnet` itself | No dependency on PowerShell's execution policy | Wrapping `publish.ps1` |
| Arguments | None accepted, as `publish.ps1` | The spec fixes the command; one obvious behaviour | Passing `%*` / `"$@"` through |
| Linux RID | `linux-x64`, fixed | The spec's command; `install.sh --rid` covers arm64 | Guessing from `uname -m` |
| Self-contained | Not set (framework-dependent default for a RID publish in .NET 10) | The spec's command, verbatim | Adding `--self-contained` |
| Tests | Extend `AssemblyNameConsistencyTests` + run all three scripts | The packaging files are pinned there; running is the real proof | No test |
