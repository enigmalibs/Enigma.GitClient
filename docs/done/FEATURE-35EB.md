# FEATURE-35EB — New icon set without the tile

**Item:** FEATURE-35EB — New icon set without the tile
**Branch:** `feature/feature-35eb-new-icon-set`
**Run:** feature/2026-11-03-icons-msi-release

## Summary

The application icon is replaced by the new set from `Branding/Apps/final2/git-client`
(`export.py final2`): the same commit line and branch in the *Soft* palette, **without the dark rounded
tile**, so the background is transparent. Every copy is the delivered file byte for byte:

- `Assets/app.ico`: the new ten-frame icon (16, 20, 24, 32, 40, 48, 64, 96, 128, 256; 32-bit RGBA PNG
  frames). It is the executable's icon, the windows' icon and the `app.ico` written beside the executable;
- `Assets/app-256.png`: the new 256 frame, shown by the About dialog;
- `packaging/icon/*.svg`: the two new masters, their header comments as the export writes them;
- `packaging/linux/icons/`: the eight sizes `install.sh` installs.

The icon's shape is unchanged (same sizes, same encoding, same file names), so no code, script or test
changes.

## Files/modules touched

- Modified:
  - `src/Enigma.GitClient.Desktop/Assets/app.ico`
  - `src/Enigma.GitClient.Desktop/Assets/app-256.png`
  - `packaging/icon/enigma-git-client.svg`
  - `packaging/icon/enigma-git-client-small.svg`
  - `packaging/linux/icons/enigma-git-client-{16,24,32,48,64,96,128,256}.png`
  - `docs/roadmap.md`, `docs/plan/FEATURE-35EB.md`: statuses.
- Created: `docs/done/FEATURE-35EB.md`

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| Checking `AppIconTests` without running the Desktop suite | A Python check of the same assertions | The user's standing rule: Desktop tests are compiled, not run, on this Windows machine |
| Prose about the tile | Nothing to change | No README, runbook, comment or test describes the icon's background |

## Deviations & follow-ups

- **Before tagging:** run the Desktop suite on Linux. `AppIconTests` pins the new files; it was compiled
  here, and its assertions were checked by script (below).
- The source directory `Branding/Apps/final2/git-client` is outside this repository and was left alone.
- Line endings: the delivered SVGs have CRLF line endings. `.gitattributes` (`* text=auto eol=lf`) stores
  them as LF, so the committed SVGs differ from the source only in line endings. The binaries (`.ico`,
  `.png`) are byte for byte. No action taken (recommendation only: have `export.py` write LF).

## Build/test evidence

**The run's baseline**, taken once on `feature/2026-11-03-icons-msi-release` at `3086738` (`develop`
@ `8a526e7` plus the planning commit), before this first dev. Later devs of the run cite it:

- `dotnet build Enigma.GitClient.slnx -c Release`: succeeded, **0 warnings, 0 errors**.
- `dotnet test --project tests/Enigma.GitClient.Core.UnitTests -c Release`: 1,392 total, 1,389 passed,
  2 skipped, **1 failed**: `TokenStoreTests.AfterTheResetOfAnUnreadableStore_SecretsAreStoredAndReadAgain`.
  It is the known Windows-only failure (DPAPI rejects a key it did not protect; see
  `docs/done/FEATURE-0DED.md`).
- `dotnet test --project tests/Enigma.GitClient.Core.IntegrationTests -c Release`: 414 total, 412
  passed, 2 skipped, 0 failed.
- Desktop tests: compiled, not run (the user's standing rule for this Windows machine).

**This dev:**

- `dotnet build Enigma.GitClient.slnx -c Release`: **0 warnings, 0 errors**.
- Core unit: 1,392 total, 1,389 passed, 2 skipped, 1 failed (the baseline's `TokenStoreTests` failure,
  nothing new).
- Core integration: 414 total, 412 passed, 2 skipped, 0 failed.
- `cmp`: each of the 12 destination files is identical to its source in `Branding/Apps/final2/git-client`.
- A Python check of `AppIconTests`' assertions passed:
  - `app.ico` holds ten square 32-bit frames (16 to 256), each an 8-bit RGBA PNG whose header gives its
    size;
  - `app-256.png` is the 256 frame byte for byte;
  - `install.sh` and `uninstall.sh` list the same eight sizes, and each Linux PNG is the matching frame
    byte for byte.
- The Release `Enigma.GitClient.Desktop.dll` embeds the new `app.ico` and `app-256.png`. The `app.ico`
  in the app's output and in the Desktop test output is the new file (`TheIconFile_SitsInTheOutputDirectoryByteForByte`).
- Fix budget: 0 cycles used.

## Documentation sweep

- Nothing stale: the README and `docs/RELEASE.md` describe the icon's sizes and places, which did not
  change. The new look goes into the 6.14.0 release notes (FEATURE-AA46).
