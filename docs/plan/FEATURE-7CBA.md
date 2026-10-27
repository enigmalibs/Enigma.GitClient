# FEATURE-7CBA — Release 6.11.0

**Status:** DONE
**Type:** FEATURE
**Branch:** `feature/feature-7cba-release-6-11-0`
**Run:** feature/2026-10-27-rename-branch-badge

## Objective

Cut **6.11.0**, using FEATURE-839A (6.10.0) as the template and following the `dotnet-release` skill:

- the version;
- the release notes for what 6.11.0 adds beyond 6.10.0;
- the README callout;
- `SECURITY.md`;
- `docs/RELEASE.md`;
- a dependency check;
- a Release pre-flight;
- the merge, tag and push runbook, printed for the user.

## Context & constraints

- **An app, not a package.** `docs/RELEASE.md` is the runbook. A release is a bare `X.Y.Z` tag on
  `main`. The version lives in `Directory.Build.props` (`6.10.0` today).
- **6.10.0 is released in the repository but not tagged.** `develop` (`cad4844`) holds it, merged from
  its run; `main` is still at 6.9.0 (`f2a3c33`), and `git tag --list` ends at `6.9.0`. The run
  branch is cut from `develop` at `cad4844`.
- **6.11.0 carries one item**, FEATURE-075D, `DONE` on this run branch before this dev starts: a
  local branch's badge on the History page offers *Rename "<name>"…*. It opens the *Rename branch*
  dialog, the Branches page's, with the name focused and selected.
- **Semantic Versioning:** a minor release. It adds backward-compatible behaviour, changes no public
  Core API and no stored format (`git diff cad4844 HEAD -G CurrentVersion` must be empty).
- `SECURITY.md` supports only the latest minor line.
- The Avalonia set moves only as a whole, and only together with what Enigma.Avalonia.Desktop is
  built against.
- **Tests on this machine (Windows):** the Core unit and integration suites run. The Desktop suite is
  compiled, **not run**, here (house rule); the runbook asks for the whole suite on Linux before
  tagging.
- Never tag, push or merge into `develop`/`main`: those commands are printed.

## Steps

1. Check that `git tag --list 6.11.0` is empty.
2. `Directory.Build.props`: `<Version>6.11.0</Version>`.
3. `RELEASENOTES.md`: a dated `## 6.11.0` section on top, in 6.10.0's shape. A summary list, then one
   section per area:
   - *Rename a branch from its badge*;
   - *Upgrading from 6.10*;
   - *Dependencies*;
   - *Version*.

   Every label and sentence the notes quote is checked against the code (`HistoryPageView.axaml`,
   `HistoryBranchViewModel`, `BranchOperations`, `RenameBranchDialogViewModel`).
4. `README.md`: the callout names 6.11 and sums up 6.10 after it. 6.9's summary goes; the older
   pointers (5.9, 5.1.0, 4.x) stay. Anything else in the README the item made factually wrong is
   corrected.
5. `SECURITY.md`: 6.11.x is supported; 6.10.x no longer is.
6. `docs/RELEASE.md`: the MSI paragraph lists 6.11.0 among the releases without a profile.
7. Dependency check (`dotnet list package --outdated`), logged. Non-coupled patch and minor bumps are
   applied. The Avalonia set is held back unless Enigma.Avalonia.Desktop moved with it.
8. Pre-flight in Release:
   - a clean build with zero warnings, AVLN included;
   - the Core unit and integration suites green; the Desktop tests compiled;
   - the assemblies carry `6.11.0`.
9. Print the runbook:
   - the whole suite on Linux, the Desktop suite included;
   - optionally, the missing `6.10.0` tag, on `develop`'s 6.10.0 commit (`cad4844`);
   - the run branch into `develop`, then `develop` into `main`;
   - tag `6.11.0`, push, publish.

## Acceptance criteria

- The Release build is clean with zero warnings. The Core unit and integration suites are green; the
  Desktop tests compile.
- The built assemblies carry 6.11.0.
- `RELEASENOTES.md` covers FEATURE-075D, and its quoted labels match the code.
- The README callout names 6.11. `SECURITY.md` supports 6.11.x. `docs/RELEASE.md` lists 6.11.0 among
  the releases without an MSI profile.
- Nothing in the dev runs `git tag`, `git push`, `dotnet pack` or `dotnet nuget push`.

## Out of scope

- NuGet packaging; a Windows MSI profile.
- Moving the Avalonia set on its own.
- Tagging 6.10.0, merging into `develop`/`main`, pushing.
- Running the Desktop suite on this Windows machine.

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| Version number | 6.11.0 | Yours, and correct under SemVer: new backward-compatible behaviour, nothing removed or changed incompatibly | 6.10.1 (a new menu item is a feature, not a fix); 7.0.0 |
| What the release carries | FEATURE-075D | The only change since 6.10.0 (`cad4844`) | — |
| The untagged 6.10.0 | Notes cover 6.10.0 → 6.11.0; the runbook prints an optional `git tag 6.10.0 cad4844` | 6.10.0 has its own notes section already; where tags go is the user's call | Folding 6.10.0 into 6.11.0's notes; tagging it here (forbidden in a run) |
| Shape of the notes, callout, MSI, tag | As 6.10.0 (minor, per-area sections) | House convention | A single *Changes* list |
| Breakdown | One phase | A routine release (`dotnet-release`), as every release since 1.0.0 | Splitting the docs from the version |
| `SECURITY.md` scope | Unchanged beyond the supported line | The rename runs a git command the client already ran from the Branches page; no token, provider, configuration file or installer behaviour changed | Naming the rename |
| Dependencies | Non-coupled patch/minor bumps applied; the Avalonia set held with the library | The house rule from earlier releases | Bumping everything |
| MSI profile | Declined | The releases' packaging is the Linux installer; Windows has a publish script, not an installer | Generating a first profile |
| Tests | Release build; Core suites run; Desktop compiled, not run; the whole suite on Linux in the runbook | The house rule for this Windows machine | Running the Desktop suite here |
