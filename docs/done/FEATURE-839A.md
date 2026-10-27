# FEATURE-839A — Release 6.10.0

**Item:** FEATURE-839A — Release 6.10.0
**Branch:** `feature/feature-839a-release-6-10-0`
**Run:** feature/2026-10-27-release-6-10-0

## Summary

Release **6.10.0** is cut in the repository. 6.9.0 (FEATURE-4358) is the template, and the notes take
the shape of a minor release. It carries two items, both `DONE`:

- **FEATURE-9A00**: squash a range of commits onto a new branch from the History page. A line is set
  as the squash base, and everything since it, up to a line above it, becomes one commit on a new
  branch. Nothing that exists is rewritten or moved.
- **FEATURE-D9C1**: a button on the start window's Repositories page writes the picked profile's
  name and email into every repository on its list.

**Edits:**

- `Directory.Build.props`: `<Version>6.10.0</Version>`.
- `Directory.Packages.props`: Microsoft.Extensions.Hosting, Microsoft.Extensions.Http and
  System.Security.Cryptography.ProtectedData 10.0.13 → 10.0.14.
- `RELEASENOTES.md`: a dated `## 6.10.0 — 2026-10-27` section on top:
  - a summary;
  - *Squash a range onto a new branch*;
  - *The profile's identity in every repository*;
  - *Upgrading from 6.9*;
  - *Dependencies*;
  - *Version*.
- `README.md`: the callout names 6.10 and sums up 6.9 after it. 6.8's summary goes; the pointers for
  5.9, 5.1.0 and 4.x stay.
- `SECURITY.md`: 6.10.x is supported; 6.9.x is not.
- `docs/RELEASE.md`: 6.10.0 is added to the releases without an MSI profile.

## Files / modules touched

**Modified**

- `Directory.Build.props`
- `Directory.Packages.props`
- `RELEASENOTES.md`
- `README.md`
- `SECURITY.md`
- `docs/RELEASE.md`
- `docs/roadmap.md`, `docs/plan/FEATURE-839A.md`: statuses.

**Created**

- `docs/done/FEATURE-839A.md`

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| Microsoft.Extensions.* and ProtectedData 10.0.13 → 10.0.14 | Applied, all three together | Patch bumps of the .NET 10 servicing band, outside the Avalonia set; 6.1.0 moved the same three together |
| Avalonia 12.1.1 → 12.1.4 | Held back | The set moves only with Enigma.Avalonia.Desktop, still 1.2.0 (built against 12.1.1), which has no update |
| MSI profile | Declined, as for every release since 1.0.0 | The packaging is the Linux installer; Windows has a publish script, not an installer |
| Texts the notes quote | Taken from the code | *Set as squash base*, *Clear the squash base*, `Squash N commits since <sha> into a new branch…` and its greyed form (`CommitRowViewModel`); `Squash base: <sha>` and the flag's *Squash base* tip (`HistoryPageViewModel`, `HistoryPageView.axaml`); *Base*, *Tip*, *Replaces*, *New branch*, *Switch to the new branch*, *Message — the first line is the title* (`SquashDialogView.axaml`); the `-squashed` / `squash/<sha>` names, the range line and the three warnings (`SquashDialogViewModel`); the success bar's sentence (`SquashOperations.ReportSuccess`); the refusals (`ResetOperations.DescribeOperationInProgress`, `SquashService.DescribeBlockedSwitch`); the identity button's tooltip (`RepositoriesPageView.axaml`), its question, *Use it* / *Cancel* and the three bars (`RepositoriesPageViewModel`) |
| How N is described | The commits that lead from the base to the line, merges and the line included; outside commits counted apart | `SquashRange.Measure` and `SquashPreview.CommitCount` count the ancestry path; the dialog adds `plus K merged in from outside the range` |
| Hooks on the squash commit | Said in the notes | `commit-tree` runs no hooks; a user with a `commit-msg` hook must not be surprised |
| What *Upgrading from 6.9* promises | Every stored format unchanged; going back keeps everything | `git diff 6.9.0 HEAD -G CurrentVersion` is empty |
| Public Core types | Named under *Version* only | `ISquashService`, `SquashRange` and `IBulkLocalIdentityService` count for SemVer; the user sees them only as the two features |
| README beyond the callout | Unchanged | Both items' sweeps already added their feature bullets and the *Where your things are kept* line |
| `SECURITY.md` scope | Unchanged | It already covers the git commands the client runs; no token, provider, configuration file or installer behaviour changed |
| The Windows-only `TokenStoreTests` failure | Recorded, not fixed; the release is not held red by it | See *Deviations & follow-ups* |

## Deviations & follow-ups

- **No deviation from the plan.**
- **Pre-existing, Windows-only test failure:**
  `Core.UnitTests.Security.TokenStoreTests.AfterTheResetOfAnUnreadableStore_SecretsAreStoredAndReadAgain`.
  It is a defect of the test, not of the product. The test's `RefusingProtector.Protect` writes the
  new key raw, and the real store then reads it through DPAPI, which refuses (`The parameter is
  incorrect`). On Linux the platform protector stores the key as it is, so the test passes there, as
  it did in 6.9.0's Linux pre-flight. The product writes and reads the key with one and the same
  protector. FEATURE-9A00 and FEATURE-D9C1 recorded the same failure at their starting commits.
  Follow-up: a BUG item for the test (BUG-3024's area).
- **Desktop suite not run here** (house rule on this Windows machine). The runbook runs the whole
  suite on Linux before tagging. That includes the 33 Desktop test methods the two items added and
  have never run: `HistorySquashTests` (11), `SquashDialogTests` (16) and six new
  `RepositoriesPageTests`.
- **Follow-ups the carried items listed, not in this release:** the squash has no *Replace
  <original>* action and no squash in place; the identity button has no reverse action and no
  progress overlay.
- **`develop` and the run.** Neither item is on `develop` (`3f32e23`, 6.9.0 plus FEATURE-9A00's
  planning commit). The run branch holds both, on top of it.
- Line endings: the touched files are LF, as `.gitattributes` asks; no CRLF churn.

## Dependency check

`dotnet list Enigma.GitClient.slnx package --outdated`, 2026-10-27:

| Package | Requested | Latest | Action |
|---|---|---|---|
| Microsoft.Extensions.Hosting, Microsoft.Extensions.Http, System.Security.Cryptography.ProtectedData | 10.0.13 | 10.0.14 | Applied |
| Avalonia, Avalonia.Desktop, Avalonia.Fonts.Inter, Avalonia.Themes.Fluent | 12.1.1 | 12.1.4 | Held (coupled set) |
| Avalonia.Headless, Avalonia.Skia (tests) | 12.1.1 | 12.1.4 | Held (coupled set) |
| Everything else (Core tests, Core integration tests) | — | — | Up to date |

## Build / test evidence

- `dotnet build Enigma.GitClient.slnx -c Release`: succeeded, **0 warnings, 0 errors** (AVLN
  included); the Desktop tests compile.
- `dotnet test --project tests/Enigma.GitClient.Core.UnitTests -c Release --no-build`: **1392 total,
  1389 passed, 1 failed, 2 skipped**. The failure is the pre-existing Windows-only `TokenStoreTests`
  one above; the skips are the two `TokenStoreTests` key-mode checks, which do not apply under
  DPAPI. The run came after the README edit, so
  `DisplayNameConsistencyTests` read the new README.
- `dotnet test --project tests/Enigma.GitClient.Core.IntegrationTests -c Release --no-build`:
  **414 total, 412 passed, 0 failed, 2 skipped** (file modes on Windows), in 4 min 15 s.
- `Desktop.UnitTests`: compiled, **not run** on this Windows machine (house rule).
- The built assemblies carry the version: `Enigma.GitClient.Core.dll` and `Enigma.GitClient.Desktop.dll`
  read `6.10.0+7a91d13…`. The suffix is the commit the build was cut from; the tagged build will show
  the release commit.
- No `git tag`, `git push`, `dotnet pack` or `dotnet nuget push` was run.
- Fix budget: 0 cycles used.

## Runbook (printed, not run)

```bash
# 0. On Linux: the whole suite, the Desktop suite included (never run on the Windows machine)
dotnet test --solution Enigma.GitClient.slnx -c Release

# 1. Integrate: the run branch, which carries FEATURE-9A00, FEATURE-D9C1 and this release on top of
#    develop, into develop, then develop into main
git switch develop
git merge --no-ff feature/2026-10-27-release-6-10-0
git switch main
git merge --no-ff develop

# 2. Tag (bare X.Y.Z, as 6.9.0)
git tag 6.10.0

# 3. Push
git push origin develop main
git push origin 6.10.0

# 4. Publish (per platform; always pass -r)
dotnet publish src/Enigma.GitClient.Desktop -c Release -r linux-x64 --self-contained true -o ./artifacts
# On Windows:
powershell -ExecutionPolicy Bypass -File packaging\windows\publish.ps1
```

## Documentation sweep

This dev is the documentation update: the README callout, `RELEASENOTES.md`, `SECURITY.md` and
`docs/RELEASE.md` are edited above. Nothing else the two items changed made a prose doc wrong (see
*README beyond the callout*). There is no `CLAUDE.md` in the repository.
