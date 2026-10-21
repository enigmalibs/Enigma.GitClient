# FEATURE-DDBC — Release 6.6.0

**Item:** FEATURE-DDBC — Release 6.6.0
**Branch:** `feature/feature-ddbc-release-6-6-0`
**Run:** feature/2026-10-21-release-660

## Summary

Release **6.6.0** is cut in the repository, using 6.5.0 (FEATURE-6E21) as the template and 5.7.0's
fix-only notes as the shape. It carries one item, `DONE`:

- **BUG-1E83**: selecting or deselecting a file no longer freezes the application (the TextMate
  installation is replaced, never stripped of its grammar).

**Edits:**

- `Directory.Build.props`: `<Version>6.6.0</Version>`.
- `RELEASENOTES.md`: a dated `## 6.6.0 — 2026-10-21` section on top:
  - a summary;
  - *Fixes*;
  - *Upgrading from 6.5*;
  - *Dependencies*;
  - *Version*, which says a patch release (6.5.1) could have carried the fix, as 5.7.0's did.
- `README.md`: the callout names 6.6 and sums up 6.5 after it. 6.4's summary goes; the pointers for
  5.9, 5.1.0 and 4.x stay.
- `SECURITY.md`: 6.6.x is supported; 6.5.x is not.
- `docs/RELEASE.md`: 6.6.0 is added to the releases without an MSI profile.

## Files / modules touched

**Modified**

- `Directory.Build.props`
- `RELEASENOTES.md`
- `README.md`
- `SECURITY.md`
- `docs/RELEASE.md`
- `docs/roadmap.md`, `docs/plan/FEATURE-DDBC.md`: statuses.

**Created**

- `docs/done/FEATURE-DDBC.md`

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| MSI profile | Declined, as for every release since 1.0.0 | The packaging is the Linux installer; `docs/RELEASE.md` records the policy |
| Avalonia 12.1.1 → 12.1.4 | Held back | The set moves only with Enigma.Avalonia.Desktop, still 1.2.0 (built against 12.1.1), which has no update |
| How much of the fix the notes explain | What froze, when, and that the highlighting now starts afresh | Enough to recognise the symptom; the lock details live in `docs/done/BUG-1E83.md` |
| README features list | Unchanged | Nothing in it became wrong |

## Deviations & follow-ups

- **6.5.0 is not tagged and not on `main` yet.** It is on `develop` (`f0a104f`). The runbook below
  tags it on its own merge into `main` first, should you want it published as its own release;
  otherwise skip that step and 6.6.0 ships both sections.
- **The Desktop suite is not run** on this machine (standing instruction). Run it on Linux before
  tagging: BUG-1E83 added four Desktop tests that are compiled only, and 6.5.0's items changed others.
- **Manual check before tagging:** in the history's details panel, select and deselect files quickly,
  alternating `.cs` files with `.txt` / `.slnx` / extension-less ones, in both renderings; switch
  Light/Dark, go to a plain file and back, and check the colours follow the theme.
- **Reporting the lock-order inversion upstream** (AvaloniaEdit / TextMateSharp) is yours to do.
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
- The built assemblies carry the version: `Enigma.GitClient.Core.dll`, `Enigma.GitClient.Desktop.dll`
  and the Desktop test assembly are `FileVersion 6.6.0.0`, `ProductVersion 6.6.0+ef106e2…`. The suffix
  is the commit the build was cut from; the tagged build will show the release commit.
- No `git tag`, `git push`, `dotnet pack` or `dotnet nuget push` was run.

## Runbook (printed, not run)

```bash
# 0. On Linux, before tagging: the Desktop suite, then the manual check above
dotnet test --solution Enigma.GitClient.slnx -c Release

# 1. (Optional) Publish 6.5.0 on its own first: develop already holds it
git switch main
git merge --no-ff develop
git tag 6.5.0

# 2. Integrate: the run branch (which carries the BUG-1E83 run) into develop, develop into main
git switch develop
git merge --no-ff feature/2026-10-21-release-660
git switch main
git merge --no-ff develop

# 3. Tag (bare X.Y.Z, as 6.4.0)
git tag 6.6.0

# 4. Push
git push origin develop main
git push origin 6.6.0          # and 6.5.0, if step 1 was run

# 5. Publish (per platform; always pass -r)
dotnet publish src/Enigma.GitClient.Desktop -c Release -r linux-x64 --self-contained true -o ./artifacts
```

## Documentation sweep

This dev is the documentation update: the README callout, `RELEASENOTES.md`, `SECURITY.md` and
`docs/RELEASE.md` are edited above. No `CLAUDE.md` in the repository.
