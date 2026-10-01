# FEATURE-0C53 — Release 5.1.0

**Item:** FEATURE-0C53 — Release 5.1.0
**Branch:** `feature/feature-0c53-release-5-1-0`
**Run:** feature/2026-10-01-polish-release-5-1

## Summary

Cuts **5.1.0**, a MINOR release under Semantic Versioning, covering everything this run merged since
5.0.0:

- a click on the selected file lets go of it and puts its diff away, in a commit's files and in the
  working tree (FEATURE-A35A);
- the Branches, Tags and Remotes dialogs keep their header, and Branches its merge band, at the top
  while the list scrolls (BUG-546B);
- softer info bars in the dark theme, from Enigma.Avalonia.Desktop 1.2.0 (FEATURE-85E2);
- success and info bars close after 2.5 seconds (FEATURE-45D3);
- the repository browser no longer cut off (BUG-15B8).

**Why 5.1.0:** this is what was asked, and SemVer agrees. The release adds backward-compatible
behaviour and fixes, removes nothing, and changes nothing stored.

- `Directory.Build.props`: `<Version>5.1.0</Version>`.
- `RELEASENOTES.md`: a dated `## 5.1.0 — 2026-10-01` section on top, in the file's shape: a summary,
  then:
  - *The history's details panel*;
  - *The dialogs*;
  - *The info bars*;
  - *Fixes*;
  - *Upgrading from 5.0* (nothing migrated; the stored files named);
  - *Dependencies* (Enigma.Avalonia.Desktop 1.2.0; the Avalonia set held);
  - *Version*.
- `README.md`: the callout is *What's new in 5.1*, with a pointer to *Upgrading from 4.x* for anyone
  skipping 5.0. The features list's details-panel line says a second click on the file puts the diff
  away.
- `SECURITY.md`: 5.1.x supported; 5.0.x not.
- `docs/RELEASE.md`: the MSI paragraph lists 5.1.0 among the releases without a profile, rewrapped to
  the file's width.

## Runbook — printed, not run

```bash
# 1. Pre-flight — already run in this dev, in Release (see below)
dotnet build Enigma.GitClient.slnx -c Release
dotnet test --solution Enigma.GitClient.slnx -c Release

# 2. Integrate: the run branch into develop, then develop into main
git switch develop
git merge --no-ff feature/2026-10-01-polish-release-5-1
git switch main
git merge --no-ff develop

# 3. Tag — bare X.Y.Z, as 1.0.0 to 5.0.0 are
git tag 5.1.0

# 4. Push
git push origin develop main
git push origin 5.1.0

# 5. Publish and install (Linux)
dotnet publish src/Enigma.GitClient.App -c Release -r linux-x64 --self-contained true -o ./artifacts
packaging/linux/install.sh
```

Then *Post-release verification* from `docs/RELEASE.md`:

- the splash screen and About say `Version 5.1.0`;
- in the dark theme, an info bar is a soft tint and a success closes after 2.5 seconds;
- a long branches list scrolls under its header;
- a profile's *Browse* shows each row's *Clone* whole.

## Files / modules touched

`Directory.Build.props`, `RELEASENOTES.md`, `README.md`, `SECURITY.md`, `docs/RELEASE.md`,
`docs/roadmap.md`, `docs/plan/FEATURE-0C53.md`

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| Keep 5.0's removals in the README | One sentence pointing a 4.x reader to *Upgrading from 4.x* | The 5.0 callout was the only place on the README that warned of the removals. Someone going from 4.x straight to 5.1 still needs it |
| The README features list | One clause on the details-panel line | 4.1.0 set the precedent of updating the features list where the run changed it. It was the one line the run changed |
| Where the browser fix goes in the notes | *Fixes* | It was filed as a bug (BUG-15B8) |
| Where the fixed headers go | *The dialogs*, not *Fixes* | They change how every tool dialog behaves with a long list, which a reader looks for by area |

## Deviations & follow-ups

- **The plan's README step is extended:** the features-list clause and the 4.x pointer, as above.
- The Avalonia set (12.1.1 → 12.1.3) is still held back. Enigma.Avalonia.Desktop 1.2.0 is built
  against 12.1.1.
- Line endings: no CRLF churn; the touched files are LF.

## Documentation sweep

This dev is the documentation update: the notes, the README, `SECURITY.md` and `docs/RELEASE.md`.

## Build/test evidence

- `git tag --list 5.1.0` was empty.
- `dotnet list package --outdated`: only the Avalonia set (Avalonia, Avalonia.Desktop,
  Avalonia.Fonts.Inter, Avalonia.Themes.Fluent; Avalonia.Headless and Avalonia.Skia in the tests),
  12.1.1 → 12.1.3. Held back. No other update.
- `dotnet build Enigma.GitClient.slnx -c Release`: 0 warnings, 0 errors.
- `dotnet test --solution Enigma.GitClient.slnx -c Release`: **2642 passed**, 0 failed.
- The Release `Enigma.GitClient.App.dll` and `Enigma.GitClient.Core.dll` carry
  `5.1.0+dc4fe0d4b7cde6c53db10d89551de78ee9322350`, the run-branch commit this dev was cut from.
- Nothing in this dev ran `git tag`, `git push`, `dotnet pack` or `dotnet nuget push`.
- Fix budget: 0 cycles used.
