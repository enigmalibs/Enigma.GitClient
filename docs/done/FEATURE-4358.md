# FEATURE-4358 — Release 6.9.0

**Item:** FEATURE-4358 — Release 6.9.0
**Branch:** `feature/feature-4358-release-6-9-0`
**Run:** feature/2026-10-23-release-6-9-0

## Summary

Release **6.9.0** is cut in the repository, with 6.8.0 (FEATURE-687C) as the template and its
minor-release notes as the shape. It carries five items, all `DONE`:

- **FEATURE-D977**: the repository strip draws the repository larger and shows the history's own
  checked-out-branch badge, or `detached at <sha>`. Its buttons moved to the History page's command
  bar: twelve named commands in four groups, centred, with icons only on a narrow window.
- **FEATURE-47AE**: the theme switch and About left the bar for the Settings page. The search box is
  narrower and reads *Search commits*. The command groups are set apart.
- **FEATURE-715E**: Escape in the History search box clears the search.
- **FEATURE-0ED1**: profiles are cards. The active one is outlined and says *Active*; the others offer
  *Make active*.
- **FEATURE-1540**: larger account logos, details, *Browse repositories*, and row actions on the
  Profiles page.

**Edits:**

- `Directory.Build.props`: `<Version>6.9.0</Version>`.
- `RELEASENOTES.md`: a dated `## 6.9.0 — 2026-10-23` section on top:
  - a summary;
  - *The repository strip*;
  - *The History command bar*;
  - *The History search*;
  - *Profiles as cards*;
  - *Upgrading from 6.8*;
  - *Dependencies*;
  - *Version*.
- `README.md`: the callout names 6.9 and sums up 6.8 after it. 6.7's summary goes; the pointers for
  5.9, 5.1.0 and 4.x stay.
- `SECURITY.md`: 6.9.x is supported; 6.8.x is not.
- `docs/RELEASE.md`: 6.9.0 is added to the releases without an MSI profile.

## Files / modules touched

**Modified**

- `Directory.Build.props`
- `RELEASENOTES.md`
- `README.md`
- `SECURITY.md`
- `docs/RELEASE.md`
- `docs/roadmap.md`, `docs/plan/FEATURE-4358.md`: statuses.

**Created**

- `docs/done/FEATURE-4358.md`

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| MSI profile | Declined, as for every release since 1.0.0 | The packaging is the Linux installer; Windows has a publish script, not an installer |
| Avalonia 12.1.1 → 12.1.4 | Held back | The set moves only with Enigma.Avalonia.Desktop, still 1.2.0 (built against 12.1.1), which has no update |
| Order of the notes' sections | Strip, command bar, search, Profiles | The plan's order, which is the order the items were built in; the strip's buttons moving makes sense before the bar that receives them |
| Texts the notes quote | Taken from the code | The twelve command names, *Search commits*, its tip, and *Clear the search (Escape)* (`HistoryPageView.axaml`); `detached at abcdef1` (`HeadState.DisplayName`) and the 6.8 strip's `detached HEAD` pill (`git show 6.8.0:…/MainWindow.axaml`); *Active*, its tip, *Make active*, *Copy from active profile* and the empty state's sentence (`ProfilesPageView.axaml`); *Using <label>* (`ProfilesPageViewModel`); *Theme* and *About Enigma Git Client* (`SettingsPageView.axaml`); *Resolve conflicts* (`ConflictResolutionPageViewModel.Title`) |
| The width the named commands need | "about 1350 px" | FEATURE-D977 PHASE03 measured about 1350 px and FEATURE-47AE PHASE02 about 1345 px; the notes round, and give 1280 px as a width that is compact |
| What *Upgrading from 6.8* promises | Every stored format unchanged; going back keeps everything | `git diff 6.8.0 HEAD -G CurrentVersion` is empty: `AppSettings` 7, `RepositoryListStore` 1, `HostAccountService` 2 and `TokenStore` 1 are unchanged |
| The pages that lost the window's commands | Said under *The History command bar* | FEATURE-D977's plan decided it; a 6.8 user who fetched from the Profiles or Settings page will look for the buttons there |
| `GitSignature.InitialsOf` | Named under *Version* only | It is a public Core helper (FEATURE-0ED1 PHASE01), so it counts for SemVer, but the user sees it only as the profiles' monograms |
| README beyond the callout | Unchanged | FEATURE-0ED1 PHASE02 already corrected the Profiles bullet; *the toolbar* (Worktrees beside Branches) and *the history's toolbar* (stash) still describe the History page's bar, which is a bar of toolbar buttons |
| `SECURITY.md` scope | Unchanged | No git command, token, provider, configuration file or installer behaviour changed |

## Deviations & follow-ups

- **No deviation from the plan.**
- **Follow-ups the carried items listed, not in this release:**
  - the branches', tags' and changed files' filter boxes do not clear on Escape yet (FEATURE-715E);
  - the keyboard focus is lost after *Make active*, which disappears from the card it was pressed on
    (FEATURE-0ED1 PHASE02);
  - the row actions keep the app-wide 0.55 resting opacity (FEATURE-1540);
  - on a compact bar the search box is at its 130 px minimum (FEATURE-D977 PHASE03, FEATURE-47AE
    PHASE02).
- **`main` and the run.** The `6.8.0` tag is on `main`'s `d5a9b1a`, a merge commit `develop` does not
  hold; its tree is `develop`'s `44f2aac`, so merging `develop` into `main` again brings only 6.9.0's
  changes.
- Line endings: the touched files are LF, as `.gitattributes` asks; no CRLF churn.

## Dependency check

`dotnet list Enigma.GitClient.slnx package --outdated`, 2026-10-23:

| Package | Requested | Latest | Action |
|---|---|---|---|
| Avalonia, Avalonia.Desktop, Avalonia.Fonts.Inter, Avalonia.Themes.Fluent | 12.1.1 | 12.1.4 | Held (coupled set) |
| Avalonia.Headless, Avalonia.Skia (tests) | 12.1.1 | 12.1.4 | Held (coupled set) |
| Everything else (Core, Core tests, Desktop) | — | — | Up to date |

## Build / test evidence

- `dotnet build Enigma.GitClient.slnx -c Release`: succeeded, **0 warnings, 0 errors** (AVLN included).
- `dotnet test --solution Enigma.GitClient.slnx -c Release --no-build`: **3469 total, 3466 passed,
  0 failed, 3 skipped** (pre-existing), across Core unit, Core integration and Desktop unit. The run
  came after the README edit, so `DisplayNameConsistencyTests` read the new README.
- The built assemblies carry the version: `Enigma.GitClient.Core.dll` and `Enigma.GitClient.Desktop.dll`
  read `6.9.0+b12bf94…`. The suffix is the commit the build was cut from; the tagged build will show
  the release commit.
- No `git tag`, `git push`, `dotnet pack` or `dotnet nuget push` was run.

## Runbook (printed, not run)

```bash
# 1. Integrate: the run branch, which carries FEATURE-715E, FEATURE-0ED1 and FEATURE-1540 on top of
#    develop, into develop, then develop into main
git switch develop
git merge --no-ff feature/2026-10-23-release-6-9-0
git switch main
git merge --no-ff develop

# 2. Tag (bare X.Y.Z, as 6.8.0)
git tag 6.9.0

# 3. Push
git push origin develop main
git push origin 6.9.0

# 4. Publish (per platform; always pass -r)
dotnet publish src/Enigma.GitClient.Desktop -c Release -r linux-x64 --self-contained true -o ./artifacts
# On Windows:
powershell -ExecutionPolicy Bypass -File packaging\windows\publish.ps1
```

## Documentation sweep

This dev is the documentation update: the README callout, `RELEASENOTES.md`, `SECURITY.md` and
`docs/RELEASE.md` are edited above. Nothing else the five items changed made a prose doc wrong (see
*README beyond the callout*). There is no `CLAUDE.md` in the repository.
