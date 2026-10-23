# FEATURE-687C — Release 6.8.0

**Status:** DONE — see `docs/done/FEATURE-687C.md`
**Type:** FEATURE
**Branch:** `feature/feature-687c-release-6-8-0`
**Run:** feature/2026-10-23-release-6-8-0

## Objective

Cut **6.8.0**, using FEATURE-3789 (6.7.0) as the template and following the `dotnet-release` skill:

- the version;
- the release notes for what 6.8.0 adds beyond 6.7.0;
- the README callout;
- `SECURITY.md`;
- `docs/RELEASE.md`;
- a dependency check;
- a Release pre-flight;
- the merge, tag and push runbook, printed for the user.

## Context & constraints

- **An app, not a package.** `docs/RELEASE.md` is the runbook. A release is a bare `X.Y.Z` tag on
  `main`. The version lives in `Directory.Build.props` (`6.7.0` today).
- **6.7.0 is tagged on `main`** (`091c631`), and `develop` (`41dc222`) holds it.
- **6.8.0 carries three items**, all `DONE` on the run `bugfix/2026-10-23-crash-net-token-reset`,
  which this run branch is cut from (`d13bade`):
  - **BUG-9DE3**: an error nothing caught no longer ends the application. It is logged, shown in a
    dialog that says the application is still running, and the application carries on. Work started
    and not awaited reaches the same net.
  - **BUG-3024**: tokens that can no longer be read (after a Windows password reset) can be reset.
    Connecting offers the reset. The Profiles page shows a band and, per line, the token's state with
    *Replace token*. *Browse repositories* no longer throws. When git falls back to its own sign-in
    because a token cannot be read, the repository window says so. Connecting an account already
    listed replaces its token instead of adding a second line.
  - **BUG-E74A**: two stale Desktop tests. Tests only; nothing the user sees.
- **Semantic Versioning:** a minor release (new, backward-compatible behaviour: the error dialog, the
  reset, the Profiles band, *Replace token*, the sign-in notice). No stored format changes:
  `settings.json` stays at version 7, `repository-lists.json` at 1, the host accounts at 2 and the
  tokens file at 1.
- `SECURITY.md` supports only the latest minor line.
- The Avalonia set moves only as a whole, and only together with what Enigma.Avalonia.Desktop is
  built against.
- **Tests on this machine (Linux):** the whole suite runs (Core unit, Core integration, Desktop unit),
  as it did for the three items. BUG-3024's Windows manual check was not performed (no DPAPI on
  Linux); the runbook carries it before tagging.
- Never tag, push or merge into `develop`/`main`: those commands are printed.

## Steps

1. Check that `git tag --list 6.8.0` is empty.
2. `Directory.Build.props`: `<Version>6.8.0</Version>`.
3. `RELEASENOTES.md`: a dated `## 6.8.0` section on top, in 6.7.0's shape. A summary list, then one
   section per area:
   - *Errors no longer close the application*;
   - *Tokens that can no longer be read*;
   - *Git's own sign-in*;
   - *Connecting an account again*;
   - *Upgrading from 6.7*;
   - *Dependencies*;
   - *Version*.

   Every label and sentence the notes quote is checked against the code (`*.axaml`, the ViewModels,
   `TokenRecovery`).
4. `README.md`: the callout names 6.8 and sums up 6.7 after it. 6.6's summary goes; the older
   pointers stay.
5. `SECURITY.md`: 6.8.x is supported; 6.7.x no longer is.
6. `docs/RELEASE.md`: the MSI paragraph lists 6.8.0 among the releases without a profile.
7. Dependency check (`dotnet list package --outdated`), logged. Non-coupled patch and minor bumps are
   applied. The Avalonia set is held back unless Enigma.Avalonia.Desktop moved with it.
8. Pre-flight in Release:
   - a clean build with zero warnings, AVLN included;
   - the whole suite green;
   - the assemblies carry `6.8.0`.
9. Print the runbook:
   - the run branch into `develop`, then `develop` into `main`;
   - tag `6.8.0`, push, publish;
   - before tagging, BUG-3024's manual check on Windows.

## Acceptance criteria

- The Release build is clean with zero warnings. The whole suite is green.
- The built assemblies carry 6.8.0.
- `RELEASENOTES.md` covers BUG-9DE3 and BUG-3024, and its quoted labels match the code.
- The README callout names 6.8. `SECURITY.md` supports 6.8.x. `docs/RELEASE.md` lists 6.8.0 among the
  releases without an MSI profile.
- Nothing in the dev runs `git tag`, `git push`, `dotnet pack` or `dotnet nuget push`.

## Out of scope

- NuGet packaging; a Windows MSI profile.
- Moving the Avalonia set on its own.
- The follow-ups the carried items listed (`GitNotFoundException` in three background reads, the
  sign-in notice for a clone from the start window, the add-account hint while replacing a token).
- Tagging, merging into `develop`/`main`, pushing.

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| Version number | 6.8.0 | Yours, and correct under SemVer: new backward-compatible behaviour (the error dialog, the reset, the Profiles band, the sign-in notice) | 6.7.1 (these are more than fixes: new UI and new Core API) |
| What the release carries | BUG-9DE3 and BUG-3024 in the notes; BUG-E74A not described | The only changes since 6.7.0; E74A changed tests only, which the notes (user-facing) do not list | A *Tests* section in the notes |
| Run branch base | Cut from `bugfix/2026-10-23-crash-net-token-reset` | The release must carry its three items | Cutting from `develop` (none of them is merged there yet) |
| Shape of the notes, callout, MSI, tag | As 6.7.0 (minor, per-area sections) | House convention | A *Fixes*-only layout as 6.6.0 (6.8.0 adds behaviour, not only fixes) |
| `SECURITY.md` scope | Unchanged | Token storage is already in scope ("stores and uses access tokens") | Naming the reset in the scope |
| README beyond the callout | Unchanged unless something became wrong | Scope discipline; BUG-3024 PHASE02 already corrected the integrations paragraph | A new *Tokens* section |
| Dependencies | Non-coupled patch/minor bumps applied; the Avalonia set held with the library | The house rule from earlier releases | Bumping everything |
| MSI profile | Declined | The releases' packaging is the Linux installer; Windows has a publish script, not an installer | Generating a first profile |
| Tests | The whole suite, in Release, on this Linux machine; the Windows manual check in the runbook | The suite runs here, as it did for the three items; DPAPI does not exist on Linux | Compiling the Desktop suite only (the 6.7.0 Windows-machine instruction does not apply here) |
