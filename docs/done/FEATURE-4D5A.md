# FEATURE-4D5A — Release 5.2.0

**Item:** FEATURE-4D5A — Release 5.2.0
**Branch:** `feature/feature-4d5a-release-5-2-0`
**Run:** feature/2026-10-02-tags-branches-release

## Summary

Cuts **5.2.0**, a MINOR release under Semantic Versioning. It ships the work merged since the
`5.1.1` tag:

- **FEATURE-6149:** a tag is deleted here or on the remote, from its history badge and from its Tags
  dialog line;
- **FEATURE-551C:**
  - a branch and its upstream on the same commit are one badge;
  - *Reset local to here* when checking out a remote branch whose local branch is elsewhere;
  - a double-click on a branch badge checks it out.

**Why 5.2.0:** this is what was asked, and SemVer agrees. It adds backward-compatible behaviour and
removes nothing. Nothing stored changes. The one behaviour it replaces is a refusal (*A branch called
"x" already exists*), which is now a question.

- `Directory.Build.props`: `<Version>5.2.0</Version>`.
- `RELEASENOTES.md`: a dated `## 5.2.0 — 2026-10-02` section on top, in 5.1.0's shape:
  - a summary;
  - *The tags*;
  - *The branch badges*;
  - *Upgrading from 5.1*: nothing migrated, plus a pointer to 5.1.1's rename note for anyone coming
    from 5.1.0;
  - *Dependencies*;
  - *Version*.
- `README.md`: the callout is *What's new in 5.2*. It keeps the 5.1.0 rename pointer and the 4.x
  pointer.
- `SECURITY.md`: 5.2.x is supported; 5.1.x no longer is.
- `docs/RELEASE.md`: the MSI paragraph lists 5.2.0 among the releases without a profile.

## Runbook — printed, not run

```bash
# 1. Pre-flight — already run in this dev, in Release (see below)
dotnet build Enigma.GitClient.slnx -c Release
dotnet test --solution Enigma.GitClient.slnx -c Release

# 2. Integrate: the run branch into develop, then develop into main
git switch develop
git merge --no-ff feature/2026-10-02-tags-branches-release
git switch main
git merge --no-ff develop

# 3. Tag — bare X.Y.Z, as 1.0.0 to 5.1.1 are
git tag 5.2.0

# 4. Push
git push origin develop main
git push origin 5.2.0

# 5. Publish (into an empty folder) and install (Linux)
rm -rf ./artifacts
dotnet publish src/Enigma.GitClient.Desktop -c Release -r linux-x64 --self-contained true -o ./artifacts
packaging/linux/install.sh
```

Then *Post-release verification* from `docs/RELEASE.md`. The splash screen and About should say
`Version 5.2.0`.

## Files / modules touched

**Modified**

- `Directory.Build.props`
- `RELEASENOTES.md`
- `README.md`
- `SECURITY.md`
- `docs/RELEASE.md`
- `docs/roadmap.md`, `docs/plan/FEATURE-4D5A.md`: statuses

**Created**

- `docs/done/FEATURE-4D5A.md`

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| The 5.1.0 → 5.2.0 upgrade | A bullet under *Upgrading from 5.1* and a sentence in the callout, both pointing to 5.1.1's *Upgrading from 5.1.0* | The 5.1.1 callout this one replaces carried the rename's reinstall note. A reader skipping 5.1.1 still needs it |
| Where the replaced refusal is mentioned | In *The branch badges*, one sentence | It is the one behaviour that changes: a refusal becomes a question. It is not a breaking change |
| Dependency bumps | None | The only updates are the coupled Avalonia set (12.1.1 → 12.1.3), held back with Enigma.Avalonia.Desktop 1.2.0 |
| MSI profile | None, as for every release so far | The packaging is the Linux installer (`docs/RELEASE.md`) |

## Deviations & follow-ups

- None from the plan.
- The Avalonia set (12.1.1 → 12.1.3) is still held back with Enigma.Avalonia.Desktop 1.2.0.
- Line endings: no CRLF churn; the touched files are LF.

## Documentation sweep

This dev is the documentation update: the notes, the README callout, `SECURITY.md` and
`docs/RELEASE.md`. No `CLAUDE.md` or `AGENTS.md` exists. No other prose doc names the current
version.

## Build/test evidence

- `git tag --list 5.2.0` was empty.
- `dotnet list package --outdated`: only the Avalonia set (Avalonia, Avalonia.Desktop,
  Avalonia.Fonts.Inter, Avalonia.Themes.Fluent; Avalonia.Headless and Avalonia.Skia in the tests),
  12.1.1 → 12.1.3. Held back. No other update.
- `dotnet build Enigma.GitClient.slnx -c Release --no-incremental`: 0 warnings, 0 errors.
- `dotnet test --solution Enigma.GitClient.slnx -c Release`: **2687 passed**, 0 failed, 0 skipped.
- The Release `Enigma.GitClient.Core.dll` and `Enigma.GitClient.Desktop.dll` carry
  `5.2.0+47e0bb2514d45071255a92adc10650c24650049e`, the run-branch commit this dev was cut from. The
  Release output's launcher is `Enigma.GitClient.Desktop`.
- Nothing in this dev ran `git tag`, `git push`, `dotnet pack` or `dotnet nuget push`.
- Fix budget: 0 cycles used.
