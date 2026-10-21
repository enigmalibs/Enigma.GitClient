# FEATURE-6E21 — Release 6.5.0

**Item:** FEATURE-6E21 — Release 6.5.0
**Branch:** `feature/feature-6e21-release-6-5-0`
**Run:** feature/2026-10-21-ui-polish-release-650

## Summary

Release **6.5.0** is cut in the repository, using 6.4.0 (FEATURE-C56E) as the template. It carries
the four items of this run, all `DONE`:

- **FEATURE-1DC2**: new repositories join the top of the list;
- **FEATURE-1585**: the info bar in the bottom-left corner, a quarter of the window wide;
- **FEATURE-5595**: the branch tree opens fully; *Expand all* / *Collapse all*;
- **FEATURE-1085**: lighter, larger details on branch and remote lines.

**Edits:**

- `Directory.Build.props`: `<Version>6.5.0</Version>`.
- `RELEASENOTES.md`: a dated `## 6.5.0 — 2026-10-21` section on top, in 6.4.0's shape:
  - a summary list;
  - *The repositories list*;
  - *The info bar*;
  - *Branches*;
  - *Branch and remote lines*;
  - *Upgrading from 6.4*;
  - *Dependencies*;
  - *Version*.
- `README.md`: the callout names 6.5 and sums up 6.4 after it. 6.3's summary goes; the pointers for
  5.9, 5.1.0 and 4.x stay.
- `SECURITY.md`: 6.5.x is supported; 6.4.x is not.
- `docs/RELEASE.md`: 6.5.0 is added to the releases without an MSI profile.

## Files / modules touched

**Modified**

- `Directory.Build.props`
- `RELEASENOTES.md`
- `README.md`
- `SECURITY.md`
- `docs/RELEASE.md`
- `docs/roadmap.md`, `docs/plan/FEATURE-6E21.md`: statuses.

**Created**

- `docs/done/FEATURE-6E21.md`

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| MSI profile | Declined, as for every release since 1.0.0 | The packaging is the Linux installer; `docs/RELEASE.md` records the policy |
| Avalonia 12.1.1 → 12.1.4 | Held back | The set moves only with Enigma.Avalonia.Desktop, still 1.2.0 (built against 12.1.1), which has no update |
| README features list | Unchanged | Nothing in it became wrong; the callout and the notes carry the news |

## Deviations & follow-ups

- **The Desktop suite is not run** on this machine (standing instruction). Run it on Linux before
  tagging: this release's four items changed or added Desktop tests that are compiled only.
- **Manual checks before tagging** (dark theme):
  - add a repository and see it on top;
  - trigger a warning (open a folder that is no repository) and see the bar bottom-left, then resize
    the window;
  - open *Branches*: everything is open, *Collapse all* / *Expand all* work, the selection stays;
  - read the badges and a remote's URL.
- Line endings: the touched files are LF; no CRLF churn.

## Dependency check

`dotnet list Enigma.GitClient.slnx package --outdated`, 2026-10-21:

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
- The built assemblies carry the version: `Enigma.GitClient.Core.dll` and `Enigma.GitClient.Desktop.dll`
  are `FileVersion 6.5.0.0`, `ProductVersion 6.5.0+3bfb4a3…`. The suffix is the commit the build was cut
  from; the tagged build will show the release commit.
- No `git tag`, `git push`, `dotnet pack` or `dotnet nuget push` was run.

## Runbook (printed, not run)

```bash
# 0. On Linux, before tagging: the Desktop suite, then the manual checks above
dotnet test --solution Enigma.GitClient.slnx -c Release

# 1. Integrate: the run branch into develop, develop into main
git switch develop
git merge --no-ff feature/2026-10-21-ui-polish-release-650
git switch main
git merge --no-ff develop

# 2. Tag (bare X.Y.Z, as 6.4.0)
git tag 6.5.0

# 3. Push
git push origin develop main
git push origin 6.5.0

# 4. Publish (per platform; always pass -r)
dotnet publish src/Enigma.GitClient.Desktop -c Release -r linux-x64 --self-contained true -o ./artifacts
```

## Documentation sweep

This dev is the documentation update: the README callout, `RELEASENOTES.md`, `SECURITY.md` and
`docs/RELEASE.md` are edited above. No `CLAUDE.md` in the repository.
