# FEATURE-5DFF — Release 5.0.0

**Item:** FEATURE-5DFF — Release 5.0.0
**Branch:** `feature/feature-5dff-release-5-0-0`
**Run:** feature/2026-09-30-rename-and-release

## Summary

Cuts **5.0.0**, a MAJOR release under Semantic Versioning, covering everything merged into `develop`
since 4.1.1 plus this run's rename:

- the history's details panel, which replaces the Changes page (FEATURE-5261);
- per-profile repository lists with a picker, drag ordering and a "Default" profile; profiles without a
  name and email; the start window's theme switch (FEATURE-711F);
- a README as a new repository's first commit (FEATURE-1669);
- selectable, copyable diff text (BUG-6787);
- the checked-out line's tint and *Create a tag*'s focus (FEATURE-0842);
- clones remembering their directory (FEATURE-2087);
- narrower rails (FEATURE-3071);
- the name "Enigma Git Client" (FEATURE-3988).

**Why 5.0.0:**

- functionality is removed: the Changes page, double-clicking a line to open its diffs, and *Pin to
  the top*;
- the 4.x recent-repositories list is not carried over.

An upgrade can therefore stop an existing workflow, which this project has treated as MAJOR since
2.0.0. Nothing else is incompatible.

- `Directory.Build.props`: `<Version>5.0.0</Version>`.
- `RELEASENOTES.md`: a dated `## 5.0.0 — 2026-09-30` section on top, in the file's shape: a summary
  saying why it is 5.0, then:
  - *The history's details panel*;
  - *The start window's repositories*;
  - *Creating, cloning and tagging*;
  - *The look*;
  - *Fixes*;
  - *Upgrading from 4.x*: the Changes page's work in the panel, no double-click, the list starting
    empty with the old file left unread, no pin, the Default profile and how 4.x treats it,
    `selectedProfileId`, the launcher's new name;
  - *Dependencies*;
  - *Version*.
- `README.md`: the what's-new callout names 5.0, its highlights and the removals.
- `SECURITY.md`: 5.0.x supported; 4.x (both lines) not.
- `docs/RELEASE.md`: the MSI paragraph lists 5.0.0 among the releases without a profile.

## Runbook — printed, not run

```bash
# 1. Pre-flight — already run in this dev, in Release (see below)
dotnet build Enigma.GitClient.slnx -c Release
dotnet test --solution Enigma.GitClient.slnx -c Release

# 2. Integrate: the run branch into develop, then develop into main
git switch develop
git merge --no-ff feature/2026-09-30-rename-and-release
git switch main
git merge --no-ff develop

# 3. Tag — bare X.Y.Z, as 1.0.0 to 4.1.1 are
git tag 5.0.0

# 4. Push
git push origin develop main
git push origin 5.0.0

# 5. Publish and install (Linux)
dotnet publish src/Enigma.GitClient.App -c Release -r linux-x64 --self-contained true -o ./artifacts
packaging/linux/install.sh
```

Then *Post-release verification* from `docs/RELEASE.md`:

- **Enigma Git Client** is in the launcher;
- the splash screen and About say `Version 5.0.0`;
- the start window shows the "Default" profile's (empty) list;
- a line's click opens the details panel.

## Files / modules touched

`Directory.Build.props`, `RELEASENOTES.md`, `README.md`, `SECURITY.md`, `docs/RELEASE.md`,
`docs/roadmap.md`, `docs/plan/FEATURE-5DFF.md`

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| Where the diff selection goes in the notes | *Fixes* | It was filed as a bug (BUG-6787): the text could not be selected |
| What the notes say about the Default profile and 4.x | 4.x lists it, and its *Use* there only says there is no name to write | Checked against 4.1.1: `GitIdentityService.WriteAsync` validates and refuses an empty identity, and the page reports it. Nothing is written |
| The 4.x line in `SECURITY.md` | One `4.x` row, unsupported | Both 4.0.x and 4.1.x are past; the table's older majors are one row each already |

## Deviations & follow-ups

- **None from the plan.**
- The Avalonia set (12.1.1 → 12.1.3) is still held back, as logged in the notes. Moving it is a
  decision of its own, with Enigma.Avalonia.Desktop.
- Line endings: no CRLF churn; the touched files are LF.

## Documentation sweep

This dev is the documentation update: the notes, the README callout, `SECURITY.md` and
`docs/RELEASE.md`.

## Build/test evidence

- `git tag --list 5.0.0` was empty.
- `dotnet list package --outdated`: only the Avalonia set (Avalonia, Avalonia.Desktop,
  Avalonia.Fonts.Inter, Avalonia.Themes.Fluent; Avalonia.Headless and Avalonia.Skia in the tests),
  12.1.1 → 12.1.3. Held back.
- `dotnet build Enigma.GitClient.slnx -c Release`: 0 warnings, 0 errors.
- `dotnet test --solution Enigma.GitClient.slnx -c Release`: **2624 passed**, 0 failed.
- The Release `Enigma.GitClient.App.dll` and `Enigma.GitClient.Core.dll` carry
  `5.0.0+482239eb2e2e830250a6d5a7286c9da5dbd20fc5`, the run-branch commit this dev was cut from.
- Nothing in this dev ran `git tag`, `git push`, `dotnet pack` or `dotnet nuget push`.
