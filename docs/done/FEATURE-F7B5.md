# FEATURE-F7B5 — New application icon

**Item:** FEATURE-F7B5 — New application icon
**Branch:** `feature/feature-f7b5-new-app-icon`
**Run:** feature/2026-10-28-new-app-icon

## Summary

The application icon is replaced by the new set from the Branding repository (`Branding/Apps/export.py`,
GC-A2): the same commit line and branch, in the *Soft* palette (blue `#6A9BF4`, green `#63C77D`). Every
copy is the delivered file byte for byte:

- `Assets/app.ico`: the new ten-frame icon (16, 20, 24, 32, 40, 48, 64, 96, 128, 256; 32-bit PNG
  frames), was six frames;
- `Assets/app-256.png`: the new 256 frame (splash, About);
- `packaging/icon/*.svg`: the two new masters, their header comments as the export writes them;
- `packaging/linux/icons/`: eight sizes, the six plus 24 and 96. `install.sh` and `uninstall.sh` list
  them, and the README, `docs/RELEASE.md` and the scripts' comments say eight.

`AppIconTests` pins the ten frames and now also the Linux copies. The temporary
`new-icons-remove-when-done/` directory is deleted; it was never tracked.

## Files / modules touched

**Modified**

- `src/Enigma.GitClient.Desktop/Assets/app.ico`, `src/Enigma.GitClient.Desktop/Assets/app-256.png`
- `packaging/icon/enigma-git-client.svg`, `packaging/icon/enigma-git-client-small.svg`
- `packaging/linux/icons/enigma-git-client-{16,32,48,64,128,256}.png`
- `packaging/linux/install.sh`, `packaging/linux/uninstall.sh`
- `README.md`, `docs/RELEASE.md`
- `tests/Enigma.GitClient.Desktop.UnitTests/AppIconTests.cs`
- `tests/Enigma.GitClient.Desktop.UnitTests/HistoryPageTests.cs` (see *Deviations*)
- `docs/roadmap.md`, `docs/plan/FEATURE-F7B5.md`: statuses.

**Created**

- `packaging/linux/icons/enigma-git-client-24.png`, `packaging/linux/icons/enigma-git-client-96.png`
- `docs/done/FEATURE-F7B5.md`

**Deleted** (untracked, outside git)

- `new-icons-remove-when-done/`

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| The new Linux-copies test | Reads `ICON_SIZES` from both scripts; each size's PNG must be the `.ico` frame byte for byte | The scripts are the source of truth for what gets installed; the test pins agreement, not a list |
| Where `Root()` comes from | A private copy in `AppIconTests` | Each test class that reads the repository has its own (`AssemblyNameConsistencyTests`, `DisplayNameConsistencyTests`) |
| The four `HistoryPageTests` failures | Fixed in the test helper, here | Pre-existing and unrelated, but the Definition of Done needs the whole suite green; the fix is test-only and small |

## Deviations & follow-ups

- **A stale test helper, fixed here (fix cycle 1).** On Linux, where the Desktop suite runs, four
  `HistoryPageTests` failed before any fix: `Header_LinesUpWithEveryRow`,
  `Header_LinesUpWithTheRowsWhenAScrollbarTakesTheirWidth`,
  `GraphColumn_HasATitledHeaderAndAGrip_AndTheRowsFollowItsWidth` and
  `ColumnTitles_EachStartTheSameGapAfterTheSeparatorBeforeThem` (`Expected: 6, Actual: 7`).
  FEATURE-9A00 PHASE02 (`edb7ad2`, 6.10.0) put the squash base's flag in the subject's grid column on
  purpose. `AssertColumnsLineUp` still matched the header cells to the row's children one for one.
  It now reads each column's right edge per grid column (`ColumnEdges`). The Desktop suite had only
  been compiled on the Windows machine since then, so the break was never seen. The view is unchanged.
- **One intermittent failure.** The first full-solution run after the fix had 1 failure. Its log
  was not captured, so the test is unknown. The next seven runs were all green: four Desktop-only
  runs (1782 tests each) and three full-solution runs (3588 tests each). Follow-up: watch for a
  timing-sensitive Desktop test when the three suites run in parallel.
- **The 20 and 40 px PNGs ship only inside `app.ico`.** hicolor has no directory for those sizes.
- **Regeneration lives outside the repository now.** The SVG comments point to
  `Branding/Apps/export.py`. Copying the results to the three places here is still a manual step,
  and `AppIconTests` checks it.
- Line endings: the SVGs and scripts are LF, as `.gitattributes` asks; no CRLF churn.

## Build / test evidence

- `dotnet build Enigma.GitClient.slnx -c Release`: succeeded, **0 warnings, 0 errors**.
- `dotnet test --solution Enigma.GitClient.slnx -c Release --no-build`, before the fix: 3588 total,
  4 failed (the `HistoryPageTests` above), 3 skipped.
- After fix cycle 1: three consecutive full runs had **3588 total, 3585 passed, 0 failed, 3 skipped**
  (Core unit, Core integration and Desktop, all on Linux). The run before them had 1 intermittent
  failure (above).
- `cmp` confirmed every destination is identical to the file it was copied from. A script check
  confirmed that each of the `.ico`'s ten frames is the matching delivered PNG, byte for byte.
- Tests added or changed: `TheIcon_HoldsTenSquare32BitPngFramesFrom16To256` (was `…Six…`), and the new
  `TheLinuxIcons_AreTheIconsFramesByteForByteInEverySizeTheInstallerInstalls`.
- Fix budget: 1 cycle used.

## Documentation sweep

- `README.md`: the installed icon comes "in eight sizes" (was six).
- `docs/RELEASE.md`: "the icon, eight sizes" (was six).
- `install.sh`, `uninstall.sh`: the header comment and the help text say eight.
