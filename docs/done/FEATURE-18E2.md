# FEATURE-18E2 — Windows publish script

**Item:** FEATURE-18E2 — Windows publish script
**Branch:** `feature/feature-18e2-windows-publish`
**Run:** vibe/2026-10-22-history-ages-win-publish

## Summary

`packaging/windows/publish.ps1` runs the Windows publish command, so nobody has to remember it:

```powershell
dotnet publish .\src\Enigma.GitClient.Desktop\ -c Release -r win-x64 -p:PublishReadyToRun=true
```

- **Runs from any directory.** The project path is resolved from `$PSScriptRoot`, and the
  caller's location is never changed.
- **Fails when the publish fails.** It runs under `Set-StrictMode` and `$ErrorActionPreference = 'Stop'`,
  and ends with `exit $LASTEXITCODE`. A missing project or a failed build gives a non-zero exit code.
- **Tells you how to use it.** Comment-based help (`Get-Help .\packaging\windows\publish.ps1`) says
  what it builds, where the result lands, and how to run it under Windows PowerShell's default
  `Restricted` policy (`powershell -ExecutionPolicy Bypass -File …`).
- **ASCII only**, so Windows PowerShell 5.1 reads it the same way whatever the file's encoding.

`docs/RELEASE.md` §5 names the script for Windows and §6's Windows line points to it.

## Files / modules touched

**Created**

- `packaging/windows/publish.ps1`
- `docs/done/FEATURE-18E2.md`

**Modified**

- `docs/RELEASE.md`:
  - §5 *Publish the application*: the Windows script, its `Bypass` form, and where the result lands;
  - §6 *Install it*: the Windows line names the script.
- `docs/roadmap.md`, `docs/plan/FEATURE-18E2.md`: statuses.

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| Path resolution | `Resolve-Path (Join-Path $PSScriptRoot '..\..\src\Enigma.GitClient.Desktop')` | A moved or missing project fails at once with a clear message, instead of an MSBuild error about a path |
| Help format | PowerShell comment-based help (`.SYNOPSIS` / `.DESCRIPTION` / `.EXAMPLE`) | The idiomatic header for a `.ps1`, the counterpart of `install.sh`'s comment block, and `Get-Help` reads it |
| `#Requires -Version 5.1` | Yes | States the floor it was verified on |

## Deviations & follow-ups

- No deviation from the plan.
- The output is **framework-dependent**, exactly like the command it wraps. With `-r` but no
  `--self-contained`, the .NET 10 SDK bundles no runtime: verified, no `coreclr.dll` in the result.
  The machine running it needs the .NET 10 Desktop Runtime. Add `--self-contained true` if that ever
  becomes a problem; it was left out to keep your command as it is.
- Line endings: `.gitattributes` makes `.ps1` LF (only `*.bat` / `*.cmd` are CRLF), and PowerShell
  runs LF scripts fine. If you want Windows scripts to be CRLF like `.bat`, add a `*.ps1 text eol=crlf`
  rule yourself. Recommendation only; nothing was changed.

## Build / test evidence

No compiled file changed. The script was verified by running it:

- **Parse:** `[Parser]::ParseFile` under Windows PowerShell 5.1.26100: 0 errors. `file`: ASCII text.
- **Run from another directory** (the session scratchpad), with
  `powershell -ExecutionPolicy Bypass -File …\packaging\windows\publish.ps1`:
  - exit code 0;
  - `src\Enigma.GitClient.Desktop\bin\Release\net10.0\win-x64\publish\Enigma.GitClient.Desktop.exe`
    produced (84 files, no runtime bundled).
- **Failure paths**, on a throw-away copy of the script in the scratchpad (removed afterwards):
  - a project that does not build: exit code 1;
  - a missing project: exit code 1.
- `dotnet build Enigma.GitClient.slnx`: succeeded, **0 warnings, 0 errors**.
- Test suites: not re-run, since no source or test file changed. The tree's code is the one
  FEATURE-3D77 verified: Core unit 1306/1307 passed (1 skipped), Core integration 386/388 passed
  (2 skipped), Desktop compiled, not run.

## Documentation sweep

`docs/RELEASE.md` §5 and §6 are part of the item. The README's *Install on Linux* section and
`SECURITY.md`'s scope (the Linux installer) are still accurate: the script installs nothing.
