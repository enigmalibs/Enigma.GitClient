# FEATURE-4358 — Release 6.9.0

**Status:** TODO
**Type:** FEATURE
**Branch:** `feature/feature-4358-release-6-9-0`
**Run:** feature/2026-10-23-release-6-9-0

## Objective

Cut **6.9.0**, using FEATURE-687C (6.8.0) as the template and following the `dotnet-release` skill:

- the version;
- the release notes for what 6.9.0 adds beyond 6.8.0;
- the README callout;
- `SECURITY.md`;
- `docs/RELEASE.md`;
- a dependency check;
- a Release pre-flight;
- the merge, tag and push runbook, printed for the user.

## Context & constraints

- **An app, not a package.** `docs/RELEASE.md` is the runbook. A release is a bare `X.Y.Z` tag on
  `main`. The version lives in `Directory.Build.props` (`6.8.0` today).
- **6.8.0 is tagged on `main`** (`d5a9b1a`). Its tree is the tree of `develop`'s `44f2aac`, which
  every later commit is built on.
- **6.9.0 carries five items**, all `DONE`. They are on the run `feature/2026-10-23-larger-profile-controls`
  (`5a164bd`), which this run branch is cut from. D977 and 47AE are already on `develop` (`3aac49c`);
  715E, 0ED1 and 1540 are not.
  - **FEATURE-D977**: the repository strip draws the repository larger (an 18 px name, `title`-size
    icons). It shows the history's own checked-out-branch badge, or `detached at <sha>`. Its buttons
    moved to the History page's command bar: twelve named commands in four groups, centred, with the
    pull and push counts as pills on the icons. A window too narrow for the names gets a compact bar,
    icons only.
  - **FEATURE-47AE**: the theme switch and About left the History bar; both are on the Settings page,
    and the start window keeps its own. The search box is 200 px at most, reads *Search commits*, and
    says what it searches in its tip. The groups are set apart by wider separators.
  - **FEATURE-715E**: Escape in the History search box clears the search and keeps the focus. With
    the diffs open, the first Escape clears and the second leaves the diffs. The clear button's tip is
    *Clear the search (Escape)*.
  - **FEATURE-0ED1**: on the Profiles page each profile is a card, in larger type, with a 36 px
    monogram. The active profile is outlined in the accent and says **Active**. The others offer
    **Make active**, in place of the *Current* pill and the *Use* chip. The repository section's
    button is *Copy from active profile*.
  - **FEATURE-1540**: an account's logo is 24 px, its details line is 13 px in the lighter grey,
    *Browse repositories* (and *Replace token*, *Move to…*) is at the page's normal type, and edit,
    delete and disconnect are 20 px glyphs in 36 px squares.
- **Semantic Versioning:** a minor release. It adds backward-compatible behaviour (the command bar and
  its compact mode, Escape in the search, *Make active*) and a public Core helper
  (`GitSignature.InitialsOf`). It changes no stored format. `settings.json` stays at version 7,
  `repository-lists.json` at 1, the host accounts at 2 and the tokens file at 1: `git diff 6.8.0 HEAD
  -G CurrentVersion` is empty.
- `SECURITY.md` supports only the latest minor line.
- The Avalonia set moves only as a whole, and only together with what Enigma.Avalonia.Desktop is
  built against.
- **Tests on this machine (Linux):** the whole suite runs (Core unit, Core integration, Desktop unit),
  as it did for the five items.
- Never tag, push or merge into `develop`/`main`: those commands are printed.

## Steps

1. Check that `git tag --list 6.9.0` is empty.
2. `Directory.Build.props`: `<Version>6.9.0</Version>`.
3. `RELEASENOTES.md`: a dated `## 6.9.0` section on top, in 6.8.0's shape. A summary list, then one
   section per area:
   - *The repository strip*;
   - *The History command bar*;
   - *The History search*;
   - *Profiles as cards*;
   - *Upgrading from 6.8*;
   - *Dependencies*;
   - *Version*.

   Every label and sentence the notes quote is checked against the code (`*.axaml`, the ViewModels).
4. `README.md`: the callout names 6.9 and sums up 6.8 after it. 6.7's summary goes; the older
   pointers stay. Anything else in the README that the five items made factually wrong is corrected.
5. `SECURITY.md`: 6.9.x is supported; 6.8.x no longer is.
6. `docs/RELEASE.md`: the MSI paragraph lists 6.9.0 among the releases without a profile.
7. Dependency check (`dotnet list package --outdated`), logged. Non-coupled patch and minor bumps are
   applied. The Avalonia set is held back unless Enigma.Avalonia.Desktop moved with it.
8. Pre-flight in Release:
   - a clean build with zero warnings, AVLN included;
   - the whole suite green;
   - the assemblies carry `6.9.0`.
9. Print the runbook:
   - the run branch into `develop`, then `develop` into `main`;
   - tag `6.9.0`, push, publish.

## Acceptance criteria

- The Release build is clean with zero warnings. The whole suite is green.
- The built assemblies carry 6.9.0.
- `RELEASENOTES.md` covers FEATURE-D977, FEATURE-47AE, FEATURE-715E, FEATURE-0ED1 and FEATURE-1540,
  and its quoted labels match the code.
- The README callout names 6.9. `SECURITY.md` supports 6.9.x. `docs/RELEASE.md` lists 6.9.0 among the
  releases without an MSI profile.
- Nothing in the dev runs `git tag`, `git push`, `dotnet pack` or `dotnet nuget push`.

## Out of scope

- NuGet packaging; a Windows MSI profile.
- Moving the Avalonia set on its own.
- The follow-ups the carried items listed (Escape in the other filter boxes, the focus after *Make
  active*, the row actions' resting opacity, the compact bar's search width).
- Tagging, merging into `develop`/`main`, pushing.

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| Version number | 6.9.0 | Yours, and correct under SemVer: new backward-compatible behaviour and a new public helper, nothing stored changes | 6.8.1 (these are more than fixes); 7.0.0 (nothing is removed from the user: theme and About moved, they did not go) |
| What the release carries | The five items since 6.8.0 | The only changes since the tag | Cutting from `develop` (715E, 0ED1 and 1540 are not merged there yet) |
| Run branch base | Cut from `feature/2026-10-23-larger-profile-controls` | It is the one branch that holds all five items | Cutting from `develop` |
| Shape of the notes, callout, MSI, tag | As 6.8.0 (minor, per-area sections) | House convention | A single *Changes* list |
| The theme switch and About leaving the History bar | Said in the notes, with where they are now | A user who looks for them on the bar must find the answer in the notes | Leaving it out as a detail |
| `SECURITY.md` scope | Unchanged | Nothing in scope (git commands, tokens, providers, configuration files, installer) changed | Rewording the scope |
| README beyond the callout | Only what the items made factually wrong | Scope discipline | A new *Command bar* feature bullet |
| Dependencies | Non-coupled patch/minor bumps applied; the Avalonia set held with the library | The house rule from earlier releases | Bumping everything |
| MSI profile | Declined | The releases' packaging is the Linux installer; Windows has a publish script, not an installer | Generating a first profile |
| Tests | The whole suite, in Release, on this Linux machine | The suite runs here, as it did for the five items | A partial run |
