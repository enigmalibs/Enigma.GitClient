# FEATURE-839A — Release 6.10.0

**Status:** TODO
**Type:** FEATURE
**Branch:** `feature/feature-839a-release-6-10-0`
**Run:** feature/2026-10-27-release-6-10-0

## Objective

Cut **6.10.0**, using FEATURE-4358 (6.9.0) as the template and following the `dotnet-release` skill:

- the version;
- the release notes for what 6.10.0 adds beyond 6.9.0;
- the README callout;
- `SECURITY.md`;
- `docs/RELEASE.md`;
- a dependency check;
- a Release pre-flight;
- the merge, tag and push runbook, printed for the user.

## Context & constraints

- **An app, not a package.** `docs/RELEASE.md` is the runbook. A release is a bare `X.Y.Z` tag on
  `main`. The version lives in `Directory.Build.props` (`6.9.0` today).
- **6.9.0 is tagged on `main`** (`f2a3c33`). `develop` (`3f32e23`) is 6.9.0 plus FEATURE-9A00's
  planning commit.
- **6.10.0 carries two items**, both `DONE`. Both are on the run `feature/2026-10-27-repositories-local-identity`
  (`b3f3767`), which is cut from the run `feature/2026-10-27-feature-9a00` and which this run branch is
  cut from. Neither is on `develop` yet.
  - **FEATURE-9A00**: squash a range of commits onto a new branch. On the History page, *Set as squash
    base* on a line marks it (a glyph on the line, a *Squash base: <sha>* chip); on a line above it on
    its first-parent line, *Squash N commits since <sha> into a new branch…* opens a dialog (base, tip,
    count, branch name, switch, message, warnings). Confirming records one commit — parent the base,
    files the tip's — on a new branch. Nothing that exists is rewritten or moved: no rebase, no
    cherry-pick. Core: `ISquashService`, `SquashRange`.
  - **FEATURE-D9C1**: on the start window's Repositories page, a button after the profile picker
    writes the selected profile's name and email into the local configuration of every repository on
    its list, after one question. Repositories that already have it are not rewritten; missing folders
    are skipped; one failure does not stop the others; one info bar reports the outcome. Core:
    `IBulkLocalIdentityService`.
- **Semantic Versioning:** a minor release. It adds backward-compatible behaviour (the squash, the
  bulk identity button) and public Core services. It changes no stored format: `git diff 6.9.0 HEAD
  -G CurrentVersion` is empty.
- The version is read verbatim from the informational version (`ProductVersion.From`): a two-digit
  minor needs nothing special.
- `SECURITY.md` supports only the latest minor line.
- The Avalonia set moves only as a whole, and only together with what Enigma.Avalonia.Desktop is
  built against.
- **Tests on this machine (Windows):** the Core unit and integration suites run. The Desktop suite is
  compiled, **not run**, here (house rule); the runbook asks for the whole suite on Linux before
  tagging. `BUG-3163` (the `AtomicFile` racing test) is in `HEAD`, so no Core test is excluded.
- Never tag, push or merge into `develop`/`main`: those commands are printed.

## Steps

1. Check that `git tag --list 6.10.0` is empty.
2. `Directory.Build.props`: `<Version>6.10.0</Version>`.
3. `RELEASENOTES.md`: a dated `## 6.10.0` section on top, in 6.9.0's shape. A summary list, then one
   section per area:
   - *Squash a range onto a new branch*;
   - *The profile's identity in every repository*;
   - *Upgrading from 6.9*;
   - *Dependencies*;
   - *Version*.

   Every label and sentence the notes quote is checked against the code (`*.axaml`, the ViewModels,
   `SquashOperations`).
4. `README.md`: the callout names 6.10 and sums up 6.9 after it. 6.8's summary goes; the older
   pointers (5.9, 5.1.0, 4.x) stay. Anything else in the README the two items made factually wrong is
   corrected.
5. `SECURITY.md`: 6.10.x is supported; 6.9.x no longer is.
6. `docs/RELEASE.md`: the MSI paragraph lists 6.10.0 among the releases without a profile.
7. Dependency check (`dotnet list package --outdated`), logged. Non-coupled patch and minor bumps are
   applied. The Avalonia set is held back unless Enigma.Avalonia.Desktop moved with it.
8. Pre-flight in Release:
   - a clean build with zero warnings, AVLN included;
   - the Core unit and integration suites green; the Desktop tests compiled;
   - the assemblies carry `6.10.0`.
9. Print the runbook:
   - the whole suite on Linux, the Desktop suite included;
   - the run branch into `develop`, then `develop` into `main`;
   - tag `6.10.0`, push, publish.

## Acceptance criteria

- The Release build is clean with zero warnings. The Core unit and integration suites are green; the
  Desktop tests compile.
- The built assemblies carry 6.10.0.
- `RELEASENOTES.md` covers FEATURE-9A00 and FEATURE-D9C1, and its quoted labels match the code.
- The README callout names 6.10. `SECURITY.md` supports 6.10.x. `docs/RELEASE.md` lists 6.10.0 among
  the releases without an MSI profile.
- Nothing in the dev runs `git tag`, `git push`, `dotnet pack` or `dotnet nuget push`.

## Out of scope

- NuGet packaging; a Windows MSI profile.
- Moving the Avalonia set on its own.
- The follow-ups the carried items listed.
- Running the Desktop suite on this Windows machine.
- Tagging, merging into `develop`/`main`, pushing.

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| Version number | 6.10.0 | Yours, and correct under SemVer: new backward-compatible behaviour and new public Core services, nothing stored changes | 6.9.1 (these are features, not fixes); 7.0.0 (nothing is removed or changed incompatibly) |
| What the release carries | FEATURE-9A00 and FEATURE-D9C1 | The only changes since the `6.9.0` tag | Cutting from `develop` (neither item is merged there) |
| Run branch base | Cut from `feature/2026-10-27-repositories-local-identity` | It is the one branch that holds both items | Cutting from `develop` |
| Shape of the notes, callout, MSI, tag | As 6.9.0 (minor, per-area sections) | House convention | A single *Changes* list |
| Breakdown | One phase | A routine release (`dotnet-release`), as every release since 1.0.0 | Splitting the docs from the version |
| `SECURITY.md` scope | Unchanged | It already covers the git commands the client runs; no token, provider, configuration file or installer behaviour changed | Naming the bulk identity write |
| README beyond the callout | Only what the items made factually wrong | Both items' sweeps already added their bullets; scope discipline | Re-writing the feature list |
| Dependencies | Non-coupled patch/minor bumps applied; the Avalonia set held with the library | The house rule from earlier releases | Bumping everything |
| MSI profile | Declined | The releases' packaging is the Linux installer; Windows has a publish script, not an installer | Generating a first profile |
| Tests | Release build; Core suites run; Desktop compiled, not run; the whole suite on Linux in the runbook | The house rule for this Windows machine; 6.9.0 ran the whole suite because it was cut on Linux | Running the Desktop suite here |
