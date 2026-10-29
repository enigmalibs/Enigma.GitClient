# FEATURE-0D4E — Publish scripts for Windows and Linux

## Summary

Each platform now has a one-command publish that runs from any directory and exits with `dotnet`'s exit
code:

- Windows: `packaging/windows/publish.ps1` (it already existed) and the new `publish.bat`. Both run
  `dotnet publish <Desktop project> -c Release -r win-x64 -p:PublishReadyToRun=true`.
- Linux: the new `packaging/linux/publish.sh`, next to `install.sh` and `uninstall.sh`. It runs the same
  command with `-r linux-x64`.

`publish.bat` calls `dotnet` itself rather than wrapping the `.ps1`, so no PowerShell execution policy
applies. `AssemblyNameConsistencyTests` now ties the three scripts to the project and to the command.
`docs/RELEASE.md` §5 and a new README *Publish* section name the scripts and say where their output lands.

## Files/modules touched

- Created:
  - `packaging/windows/publish.bat` (CRLF)
  - `packaging/linux/publish.sh` (LF, mode `100755`)
- Modified:
  - `tests/Enigma.GitClient.Desktop.UnitTests/AssemblyNameConsistencyTests.cs`
  - `docs/RELEASE.md`
  - `README.md`

## Deviations & follow-ups

- None from the plan.
- Follow-up: run the Desktop suite on Linux before tagging. It is not run on this Windows machine.

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| `publish.sh` ends with | `exec dotnet publish …` | The script's exit code is `dotnet`'s with no extra line; `set -euo pipefail` as `install.sh` |
| `publish.bat` ends with | `exit /b %ERRORLEVEL%` | Passes `dotnet`'s code to the caller without closing a parent `cmd` |
| A `pause` at the end of the `.bat` | None | It would block any automated caller; `publish.ps1` has none |
| What the test pins | The project directory and the `-c Release -r <rid> -p:PublishReadyToRun=true` arguments | A rename of the assembly or a drift in the command breaks it; running the scripts is the behavioural proof |

## Build/test evidence

- `dotnet build Enigma.GitClient.slnx -c Release`: 0 warnings, 0 errors. Desktop tests compiled; the new theory
  `ThePublishScriptsPublishTheProjectOfTheAssembly` (3 cases) is compiled, not run (user rule).
- Each script was run from the scratchpad directory (not the repository):
  - `cmd /c packaging\windows\publish.bat`: exit 0. Writes `…\net10.0\win-x64\publish\` with
    `Enigma.GitClient.Desktop.exe` and `app.ico`.
  - `powershell -ExecutionPolicy Bypass -File packaging\windows\publish.ps1`: exit 0, same folder.
  - `bash packaging/linux/publish.sh` (Git Bash, a cross-publish): exit 0. Writes
    `…/net10.0/linux-x64/publish/` with the `Enigma.GitClient.Desktop` launcher and `app.ico`.
- ReadyToRun applied: `Enigma.GitClient.Core.dll` is 556,544 bytes as IL, 1,404,928 in the win-x64
  publish and 1,422,848 in the linux-x64 one.
- Failures propagate: copies of `publish.bat` and `publish.sh` in a directory where the project path does
  not resolve both exit 1.
- `bash -n packaging/linux/publish.sh`: OK. `publish.sh` has no CR byte; every line of `publish.bat` is CRLF.
- Core integration suite: 414 total, 412 passed, 0 failed, 2 skipped (5 min 52 s).
- Core unit suite: 1,392 total, 1,389 passed, 2 skipped, 1 failed. The failure is
  `TokenStoreTests.AfterTheResetOfAnUnreadableStore_SecretsAreStoredAndReadAgain`, the pre-existing
  Windows-only DPAPI failure recorded in `docs/done/FEATURE-0DED.md`. This dev touches nothing in Core.

## Documentation sweep

- `docs/RELEASE.md` §5 and §6 and the README were updated as planned steps. No other doc became wrong.
