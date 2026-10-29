# FEATURE-5096 — App icon in the output directory

## Summary

The application icon is now written as a file, `app.ico`, at the root of the build output and of the
publish folder, next to `Enigma.GitClient.Desktop(.exe)`. Shortcuts, launchers and installers can point
at it. The icon is still embedded as an `AvaloniaResource`, and the windows and the About dialog read it
from there unchanged.

The mechanism is one `None Update` item on `Assets/app.ico` with `CopyToOutputDirectory="PreserveNewest"`
and `TargetPath="app.ico"`. The SDK's default `None` glob already holds the file, so `Update` is enough
and no item is duplicated. Items copied to the output are published too, and they flow to a referencing
project's output, the Desktop test project included.

## Files/modules touched

- Modified:
  - `src/Enigma.GitClient.Desktop/Enigma.GitClient.Desktop.csproj`
  - `tests/Enigma.GitClient.Desktop.UnitTests/AppIconTests.cs` (new test, class comment)

## Deviations & follow-ups

- None from the plan.
- Follow-up: run the Desktop suite on Linux before tagging. It is not run on this Windows machine.

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| `Include` or `Update` | `Update` | The default `None` glob already includes `Assets/app.ico`; `Include` would add a second item |
| `TargetPath` or `Link` | `TargetPath="app.ico"` | Says the output path directly; `Link` is about how the file shows in the IDE |

## Build/test evidence

- `dotnet build Enigma.GitClient.slnx -c Release`: 0 warnings, 0 errors. Desktop tests compiled.
- `src/Enigma.GitClient.Desktop/bin/Release/net10.0/app.ico`: `cmp` reports it byte-identical to `Assets/app.ico`.
  There is no `Assets/` sub-folder in the output.
- `tests/Enigma.GitClient.Desktop.UnitTests/bin/Release/net10.0/app.ico`: byte-identical too. This is
  what the new test reads.
- `dotnet publish src/Enigma.GitClient.Desktop -c Release -r win-x64 -o <scratch>`: `app.ico` sits next
  to `Enigma.GitClient.Desktop.exe`, byte-identical.
- New test `AppIconTests.TheIconFile_SitsInTheOutputDirectoryByteForByte`: compiled, not run (user rule).
- Core integration suite: 414 total, 412 passed, 0 failed, 2 skipped (5 min 40 s).
- Core unit suite: 1,392 total, 1,389 passed, 2 skipped, 1 failed. The failure is
  `TokenStoreTests.AfterTheResetOfAnUnreadableStore_SecretsAreStoredAndReadAgain`, the pre-existing
  Windows-only DPAPI failure recorded in `docs/done/FEATURE-0DED.md`. This dev touches nothing in Core.

## Documentation sweep

- README, `docs/RELEASE.md`: nothing they say became wrong. The new file is listed in the 6.13.0 notes
  (FEATURE-530E).
