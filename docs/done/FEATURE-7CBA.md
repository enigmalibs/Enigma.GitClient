# FEATURE-7CBA — Release 6.11.0

**Item:** FEATURE-7CBA — Release 6.11.0
**Branch:** `feature/feature-7cba-release-6-11-0`
**Run:** feature/2026-10-27-rename-branch-badge

## Summary

Release **6.11.0** is cut in the repository. 6.10.0 (FEATURE-839A) is the template, and the notes take
the shape of a minor release. It carries one item, `DONE` on this run branch:

- **FEATURE-075D**: a local branch's badge in the history offers `Rename "<name>"…`, through the
  branches dialog's rename dialog, which now opens with the name focused and selected.

**Edits:**

- `Directory.Build.props`: `<Version>6.11.0</Version>`.
- `RELEASENOTES.md`: a dated `## 6.11.0 — 2026-10-27` section on top:
  - a summary;
  - *Rename a branch from its badge*;
  - *Upgrading from 6.10*;
  - *Dependencies*;
  - *Version*.
- `README.md`: the callout names 6.11 and sums up 6.10 after it. 6.9's summary goes; the pointers for
  5.9, 5.1.0 and 4.x stay.
- `SECURITY.md`: 6.11.x is supported; 6.10.x is not.
- `docs/RELEASE.md`: 6.11.0 is added to the releases without an MSI profile.

## Files / modules touched

**Modified**

- `Directory.Build.props`
- `RELEASENOTES.md`
- `README.md`
- `SECURITY.md`
- `docs/RELEASE.md`
- `docs/roadmap.md`, `docs/plan/FEATURE-7CBA.md`: statuses.

**Created**

- `docs/done/FEATURE-7CBA.md`

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| Avalonia 12.1.1 → 12.1.4 | Held back | The set moves only with Enigma.Avalonia.Desktop, still 1.2.0 (built against 12.1.1), which has no update |
| Other packages | None to move | `dotnet list package --outdated` lists only the Avalonia set |
| MSI profile | Declined, as for every release since 1.0.0 | The packaging is the Linux installer; Windows has a publish script, not an installer |
| Texts the notes quote | Taken from the code | `Rename "<name>"…`, `Delete "<name>"…` (`HistoryBranchViewModel`); *Rename branch*, *Rename*, *Cancel*, `Could not rename "<name>"` (`BranchOperations`); `Renaming <name>`, *New name* (`RenameBranchDialogView.axaml`); *A branch called "x" already exists.* and the same-name refusal (`RenameBranchDialogViewModel`); the forgotten merge source (`HistoryPageViewModel.ForgetAMergeSourceThatIsGone`) |
| Placeholders in the notes and the callout | In code spans (`` `Rename "<name>"…` ``) | A bare `<name>` is read as an HTML tag and dropped on GitHub; the notes already quote `Push "<name>"` this way |
| What *Upgrading from 6.10* promises | Every stored format unchanged; going back keeps everything | `git diff cad4844 HEAD -G CurrentVersion` touches only the plan file; `AppSettings` 7, `RepositoryListStore` 1, `HostAccountService` 2, `TokenStore` 1 unchanged |
| What the notes say about worktrees | Nothing | The rename of a branch checked out in another worktree is git's own doing and was not exercised by this release's tests |
| `SECURITY.md` scope | Unchanged beyond the supported line | The rename runs the git command the branches dialog already ran |

## Deviations & follow-ups

- **No deviation from the plan.**
- **6.10.0 has no tag.** `develop` holds it (`cad4844`), `main` is still at 6.9.0. The runbook prints
  an optional `git tag 6.10.0 cad4844` for the user to decide on.
- **Pre-existing, Windows-only Core failure:**
  `Core.UnitTests.Security.TokenStoreTests.AfterTheResetOfAnUnreadableStore_SecretsAreStoredAndReadAgain`,
  as recorded by FEATURE-839A and FEATURE-075D. A test defect under DPAPI; it passes on Linux.
  Follow-up: a BUG item for the test (BUG-3024's area).
- **Desktop suite not run here** (house rule). The runbook runs the whole suite on Linux before
  tagging, including FEATURE-075D's six `HistoryRenameBranchTests`, which have never run.
- Line endings: the touched files are LF, as `.gitattributes` asks; no CRLF churn.

## Dependency check

`dotnet list Enigma.GitClient.slnx package --outdated`, 2026-10-27:

| Package | Requested | Latest | Action |
|---|---|---|---|
| Avalonia, Avalonia.Desktop, Avalonia.Fonts.Inter, Avalonia.Themes.Fluent | 12.1.1 | 12.1.4 | Held (coupled set) |
| Avalonia.Headless, Avalonia.Skia (tests) | 12.1.1 | 12.1.4 | Held (coupled set) |
| Everything else | — | — | Up to date |

## Build / test evidence

- `dotnet build Enigma.GitClient.slnx -c Release`: succeeded, **0 warnings, 0 errors** (AVLN
  included); the Desktop tests compile.
- `dotnet test --project tests/Enigma.GitClient.Core.UnitTests -c Release --no-build`: **1392 total,
  1389 passed, 1 failed, 2 skipped**. The failure is the pre-existing Windows-only `TokenStoreTests`
  one above. The run came after the README edit, so `DisplayNameConsistencyTests` read the new README.
- `dotnet test --project tests/Enigma.GitClient.Core.IntegrationTests -c Release --no-build`:
  **414 total, 412 passed, 0 failed, 2 skipped** (file modes on Windows), in 4 min 20 s.
- `Desktop.UnitTests`: compiled, **not run** on this Windows machine (house rule).
- The built assemblies carry the version: `Enigma.GitClient.Core.dll` and
  `Enigma.GitClient.Desktop.dll` read `6.11.0+9e848bd…`, the commit the build was cut from; the tagged
  build will show the release commit.
- No `git tag`, `git push`, `dotnet pack` or `dotnet nuget push` was run.
- Fix budget: 0 cycles used.

## Runbook (printed, not run)

```bash
# 0. On Linux: the whole suite, the Desktop suite included (never run on the Windows machine)
dotnet test --solution Enigma.GitClient.slnx -c Release

# 1. Optional: 6.10.0 was merged into develop but never tagged; tag it where it was released
git tag 6.10.0 cad4844

# 2. Integrate: the run branch (FEATURE-075D and this release, on top of develop) into develop,
#    then develop into main
git switch develop
git merge --no-ff feature/2026-10-27-rename-branch-badge
git switch main
git merge --no-ff develop

# 3. Tag (bare X.Y.Z)
git tag 6.11.0

# 4. Push
git push origin develop main
git push origin 6.11.0        # and 6.10.0, if tagged in step 1

# 5. Publish (per platform; always pass -r)
dotnet publish src/Enigma.GitClient.Desktop -c Release -r linux-x64 --self-contained true -o ./artifacts
# On Windows:
powershell -ExecutionPolicy Bypass -File packaging\windows\publish.ps1
```

## Documentation sweep

The release's own edits are the documentation. Nothing else is stale; the repository has no
`CLAUDE.md` or `AGENTS.md`.
