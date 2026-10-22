# FEATURE-3789 — Release 6.7.0

**Item:** FEATURE-3789 — Release 6.7.0
**Branch:** `feature/feature-3789-release-6-7-0`
**Run:** vibe/2026-10-22-release-6-7-0

## Summary

Release **6.7.0** is cut in the repository, using 6.6.0 (FEATURE-DDBC) as the template and 6.5.0's
minor-release notes as the shape. It carries two items, both `DONE`:

- **FEATURE-3D77**: when the history shows *How long ago*, each line's age is measured again on
  every automatic refresh, and on every refresh, fetch, pull or push you ask for.
- **FEATURE-18E2**: `packaging/windows/publish.ps1` runs the Windows publish (`win-x64`,
  ReadyToRun, framework-dependent) from any directory.

**Edits:**

- `Directory.Build.props`: `<Version>6.7.0</Version>`.
- `RELEASENOTES.md`: a dated `## 6.7.0 — 2026-10-22` section on top:
  - a summary;
  - *The history's ages*;
  - *Publishing on Windows*;
  - *Upgrading from 6.6*;
  - *Dependencies*;
  - *Version*.
- `README.md`: the callout names 6.7 and sums up 6.6 after it. 6.5's summary goes; the pointers for
  5.9, 5.1.0 and 4.x stay.
- `SECURITY.md`: 6.7.x is supported; 6.6.x is not.
- `docs/RELEASE.md`: 6.7.0 is added to the releases without an MSI profile.

## Files / modules touched

**Modified**

- `Directory.Build.props`
- `RELEASENOTES.md`
- `README.md`
- `SECURITY.md`
- `docs/RELEASE.md`
- `docs/roadmap.md`, `docs/plan/FEATURE-3789.md`: statuses.

**Created**

- `docs/done/FEATURE-3789.md`

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| MSI profile | Declined, as for every release since 1.0.0 | The packaging is the Linux installer; Windows gets a publish script, not an installer |
| Avalonia 12.1.1 → 12.1.4 | Held back | The set moves only with Enigma.Avalonia.Desktop, still 1.2.0 (built against 12.1.1), which has no update |
| Which refreshes the notes say move the ages | Automatic refresh, plus a requested refresh, fetch, pull or push | Checked against the call sites: `MainWindowViewModel`'s `Refreshed` handler and `RunSyncAsync` reach `RefreshInPlaceAsync`. A working-tree-only refresh (a file saved on disk) goes to `RefreshWorkingTreeAsync`, which does not, so the notes don't promise it |
| UI labels in the notes | *Dates* → *How long ago* / *The date and time*, and *Fetch and refresh automatically* | The exact texts in `SettingsPageView.axaml` and `SettingsDescriptions.cs` |
| How much of the script the notes explain | What it builds, where the result lands, the runtime it needs, the `Bypass` form, the exit code | Enough to use it; the full help is `Get-Help`, and `docs/RELEASE.md` §5 has the runbook form |
| README beyond the callout | Unchanged | Nothing in it became wrong: *Install on Linux* is still accurate, and the layout table never listed `packaging/` |
| `SECURITY.md` scope | Unchanged | The Windows script publishes and installs nothing, as FEATURE-18E2's sweep found |

## Deviations & follow-ups

- No deviation from the plan.
- **The Desktop suite is not run** on this machine (standing instruction). Run it on Linux before
  tagging: FEATURE-3D77 added five Desktop tests that are compiled only.
- **Manual check before tagging:**
  - with *Dates* on *How long ago* and a short automatic refresh, make a commit, leave the window
    alone for more than a minute, and check its line goes from "just now" to "1 minute ago" or
    later, with the selection and scroll unchanged;
  - switch to *The date and time* and check the tooltip's age moves the same way;
  - on Windows, run `powershell -ExecutionPolicy Bypass -File packaging\windows\publish.ps1` from
    another directory and start the `.exe` from its result.
- **Follow-up, if wanted** (carried from FEATURE-3D77): the relative dates in the commit details
  dialog and on the branches and tags pages still freeze at the moment they were drawn.
- Line endings: the touched files are LF, as `.gitattributes` asks; no CRLF churn.

## Dependency check

`dotnet list Enigma.GitClient.slnx package --outdated`, 2026-10-22:

| Package | Requested | Latest | Action |
|---|---|---|---|
| Avalonia, Avalonia.Desktop, Avalonia.Fonts.Inter, Avalonia.Themes.Fluent | 12.1.1 | 12.1.4 | Held (coupled set) |
| Avalonia.Headless, Avalonia.Skia (tests) | 12.1.1 | 12.1.4 | Held (coupled set) |
| Everything else (Core, Core tests) | — | — | Up to date |

## Build / test evidence

- `dotnet build Enigma.GitClient.slnx -c Release`: succeeded, **0 warnings, 0 errors** (AVLN included).
- `Enigma.GitClient.Core.UnitTests` (Release): 1307 total, 1306 passed, 0 failed, 1 skipped
  (pre-existing).
- `Enigma.GitClient.Core.IntegrationTests` (Release): 388 total, 386 passed, 0 failed, 2 skipped
  (pre-existing).
- `Enigma.GitClient.Desktop.UnitTests`: **compiled, not run** (standing instruction).
- The built assemblies carry the version: `Enigma.GitClient.Core.dll`, `Enigma.GitClient.Desktop.dll`
  and the Desktop test assembly are `FileVersion 6.7.0.0`, `ProductVersion 6.7.0+947642c…`. The suffix
  is the commit the build was cut from; the tagged build will show the release commit.
- No `git tag`, `git push`, `dotnet pack` or `dotnet nuget push` was run.

## Runbook (printed, not run)

```bash
# 0. On Linux, before tagging: the Desktop suite, then the manual check above
dotnet test --solution Enigma.GitClient.slnx -c Release

# 1. Integrate: the run branch (which carries the history-ages / Windows-publish run) into develop,
#    develop into main
git switch develop
git merge --no-ff vibe/2026-10-22-release-6-7-0
git switch main
git merge --no-ff develop

# 2. Tag (bare X.Y.Z, as 6.6.0)
git tag 6.7.0

# 3. Push
git push origin develop main
git push origin 6.7.0

# 4. Publish (per platform; always pass -r)
dotnet publish src/Enigma.GitClient.Desktop -c Release -r linux-x64 --self-contained true -o ./artifacts
# On Windows:
powershell -ExecutionPolicy Bypass -File packaging\windows\publish.ps1
```

## Documentation sweep

This dev is the documentation update: the README callout, `RELEASENOTES.md`, `SECURITY.md` and
`docs/RELEASE.md` are edited above. No `CLAUDE.md` in the repository.
