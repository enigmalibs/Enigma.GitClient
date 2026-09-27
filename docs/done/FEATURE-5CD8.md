# FEATURE-5CD8 — Release 3.0.0

**Item:** FEATURE-5CD8 — Release 3.0.0
**Branch:** `feature/feature-5cd8-release-3-0-0`
**Run:** feature/2026-09-27-release-3-0-0

## Summary

Version **3.0.0** is cut in the repository. The merges, the tag and the pushes are left for the user
and are printed below.

**Why 3.0.0.** 2.0.0 is released: tagged, and on `origin/main`. Since then one item has landed,
`FEATURE-6C81`: git signs in with the profile's token. It is a new feature, and it is not backward
compatible:

- **The token replaces the user's own credential helper.** For every HTTPS origin a profile's
  integration covers, `GitCredentials` passes `-c credential.<origin>.helper=` first. That empties
  git's helper list for that host, so the user's own helper is never asked there.
- **2.0 asked for read-only tokens.** Its scope hints and README asked for fine-grained *Contents:
  read* on GitHub, `read_repository` on GitLab, and *Code: Read* on Azure DevOps. They were enough
  because the integration was only the permission. 2.0's README said so in so many words: *"git still
  signs in with its own credential helper or SSH key"*.

So a 2.0 user who pushes over HTTPS through their own helper with such a token has pushes refused
after the upgrade until they replace the token. A fine-grained GitHub token limited to selected
repositories likewise stops the other private repositories on that host from fetching.

Under Semantic Versioning, a change that can make previously valid use fail after an upgrade is
incompatible, and that makes it **MAJOR**. `FEATURE-4AC8` applied the same rule to 2.0.0. The file
formats are unchanged, which the notes state, but that does not lower the version:

- not 2.1.0, because a minor release promises that nothing that worked stops working;
- not 2.0.1, because this is not only fixes.

What the release contains:

- `Directory.Build.props` — `<Version>3.0.0</Version>` for Core and App together. The built assemblies
  carry the informational version `3.0.0+<commit>`, which the splash screen, the About dialog, the
  Settings page and the hosting user agent read.
- `RELEASENOTES.md` — `## 3.0.0 — 2026-09-27` on top, in the file's themed shape:
  - *Signing in* — which operations sign in, which integration a clone uses, how the token reaches
    git, and what SSH, uncovered hosts and profile-less repositories keep;
  - *Refused tokens* — the refused-token message, 401/403 as refused sign-ins, the scope hints;
  - *Upgrading from 2.0* — replacing a read-only token, a fine-grained token's repository selection,
    who has nothing to do, and the file compatibility both ways;
  - *Dependencies* and *Version*.
- `README.md` — the callout is now *What's new in 3.0*, pointing at the upgrade notes.
- `SECURITY.md` — 3.0.x supported; 2.x and 1.x no longer.
- `docs/RELEASE.md` — the MSI paragraph records that 3.0.0 declined a profile too.

## Files / modules touched

**Created**

- `docs/done/FEATURE-5CD8.md`

**Modified**

- `Directory.Build.props` — `2.0.0 → 3.0.0`
- `RELEASENOTES.md` — the 3.0.0 section
- `README.md` — the what's-new callout
- `SECURITY.md` — the supported-versions table
- `docs/RELEASE.md` — "declined for 1.0.0, 1.1.0, 2.0.0 and 3.0.0"
- `docs/roadmap.md`, `docs/plan/FEATURE-5CD8.md` — statuses

## Dependency refresh

`dotnet list Enigma.GitClient.slnx package --outdated`, run on the release branch:

| Package | Current | Latest | Action |
|---|---|---|---|
| Avalonia, Avalonia.Desktop, Avalonia.Themes.Fluent, Avalonia.Fonts.Inter | 12.1.1 | 12.1.3 | **held back**: this is the coupled set Enigma.Avalonia.Desktop 1.1.0 is built against, and it is bumped as a whole or not at all |
| Avalonia.Headless, Avalonia.Skia (tests) | 12.1.1 | 12.1.3 | held back with the set |
| everything else (Microsoft.Extensions.*, System.Security.Cryptography.ProtectedData, CommunityToolkit.Mvvm, Enigma.Avalonia.Desktop, Enigma.Icons.Avalonia, AvaloniaUI.DiagnosticsSupport, xunit.v3, CodeCoverage) | — | — | already latest |

Nothing outside the coupled set was outdated, so `Directory.Packages.props` is not touched.

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| MAJOR, although the file formats stay compatible | MAJOR | What an upgrade breaks is behaviour: a push, for users of profiles who push over HTTPS with a read-only token and their own helper. SemVer measures that, not only formats. The notes say plainly that going back to 2.0 keeps the accounts |
| What *Upgrading from 2.0* tells the user to do | Replace a read-only token with one that has write access, per host, and give a fine-grained token every private repository used under the profile | The two ways a 2.0 set-up stops working; everything else (classic `repo`, SSH, no profiles) is named as having nothing to do |
| Quoting the refused-token message | Its first sentence, in italics, then what to do | Enough for a user to recognise it on screen; the full wording lives in the app |
| `SECURITY.md` rows | `3.0.x` supported, then one `2.x` and one `1.x` row, unsupported | The file's policy: fixes go to the latest release; one row per earlier major, as 2.0.0 wrote for 1.x |
| Licence audit | Not repeated | A routine release that adds or changes no dependency |
| MSI profile | None | Declined for 1.0.0 to 2.0.0, and nothing about Windows packaging changed |
| Snippet-verification gate | Not run | Neither a guide nor the README quick-start was touched; the callout names no API |
| Tag | Bare `3.0.0` | `git tag` lists `1.0.0`, `1.1.0` and `2.0.0`: the convention holds |

## Runbook — printed, not run

```bash
# 1. Pre-flight — done in this dev, repeat after merging if anything else lands
dotnet build Enigma.GitClient.slnx -c Release
dotnet test --solution Enigma.GitClient.slnx -c Release

# 2. Integrate: the run branch into develop, then develop into main
git switch develop
git merge --no-ff feature/2026-09-27-release-3-0-0
git switch main
git merge --no-ff develop

# 3. Tag — bare X.Y.Z, as 1.0.0, 1.1.0 and 2.0.0 are
git tag 3.0.0

# 4. Push
git push origin develop main
git push origin 3.0.0

# 5. Publish and install (Linux)
dotnet publish src/Enigma.GitClient.App -c Release -r linux-x64 --self-contained true -o ./artifacts
packaging/linux/install.sh
```

Then *Post-release verification* from `docs/RELEASE.md` §7:

- the launcher entry starts the application;
- the splash screen and the About dialog say `Version 3.0.0`;
- `git tag` lists `3.0.0` on the merge commit on `main`, and the tag is on the remote.

## Deviations & follow-ups

- None from the plan.
- Follow-up, not in this release: when the host refuses a profile's token, git could retry with the
  user's own helper. That would soften the upgrade for read-only tokens, but it is a behaviour change
  of its own, and the plan leaves `FEATURE-6C81` as shipped.
- Line endings: no CRLF churn. The touched files are LF.

## Documentation sweep

This dev is the documentation update. A search for `2.0.0` / `2.0.x` / `2.0` across the README,
`SECURITY.md`, `docs/RELEASE.md`, `packaging/` and `src/` leaves only:

- the historical MSI sentence;
- the callout's *Upgrading from 2.0*;
- a `2.0` literal in `WordDiff`'s similarity arithmetic.

None of these is the app's version. There is no `CLAUDE.md` or `AGENTS.md`.

## Build/test evidence

- `dotnet build Enigma.GitClient.slnx -c Release`: 0 warnings, 0 errors.
- `dotnet test --solution Enigma.GitClient.slnx -c Release`: **2355 passed**, 0 failed, 0 skipped, with
  no fix cycle.
- The generated assembly info of both `Enigma.GitClient.Core` and `Enigma.GitClient.App` (Release)
  carries `AssemblyVersion 3.0.0.0`, `FileVersion 3.0.0.0` and `InformationalVersion
  3.0.0+defa17a654d70ac0b152f6d5c65688b114ccf3ee`, the commit this dev was cut from. `strings` on the
  built `Enigma.GitClient.Core.dll` finds the same.
- Nothing in this dev ran `git tag`, `git push`, `dotnet pack` or `dotnet nuget push`.
