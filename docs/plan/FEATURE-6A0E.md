# FEATURE-6A0E — Release 5.5.0

**Status:** TODO
**Type:** FEATURE
**Branch:** `feature/feature-6a0e-release-5-5-0`
**Run:** feature/2026-10-05-release-5-5-0

## Objective

Cut **5.5.0**, with FEATURE-4707 (5.4.0) as the template:

- the version;
- the release notes for everything since the `5.4.0` tag;
- the README callout, and the diff viewer's *Features* bullets;
- `SECURITY.md`;
- `docs/RELEASE.md`;
- a dependency check;
- a Release pre-flight;
- the merge/tag/push runbook, printed for the user.

## Context & constraints

- **An app, not a package** (no `PackageId`). `docs/RELEASE.md` is the runbook. A release is a bare
  `X.Y.Z` tag on `main`. The version lives in `Directory.Build.props` (`5.4.0` today).
- **In this release:** everything this run's branch carries beyond the `5.4.0` tag:
  - **FEATURE-BB60**, *Diffs on AvaloniaEdit* (run `feature/2026-10-05-diff-view-avaloniaedit`),
    four phases:
    - the editor control;
    - the unified diff on it;
    - side by side on two editors;
    - syntax highlighting.

    What the reader gets: native text selection and copy, Ctrl+F search, TextMate syntax
    highlighting by extension, and smooth scrolling.
  - **BUG-7823**, *A drag to the diff's end hangs the app* (run `bugfix/2026-10-05-diff-drag-hang`),
    including the reopened fix on `bugfix/bug-7823-arrange-clamp`, which this run is cut from.
- **Visible changes nobody asked for in their own install:**
  - Side by side no longer wraps. The wrap toggle and its setting are the unified view's only.
  - Rows can no longer be multi-selected in the diff: the text is selected instead, and *Copy*
    copies the selected code.
- **No stored-data change:** `settings.json` keeps schema 6. *Wrap long lines* is the same key, now
  labelled *in the unified view*.
- **New runtime dependencies** (FEATURE-BB60), in their own CPM group, versioned apart from the
  Avalonia set:
  - `Avalonia.AvaloniaEdit` 12.0.0;
  - `AvaloniaEdit.TextMate` 12.0.0, which brings TextMateSharp and the native `onigwrap`.
- **Semantic Versioning:** new behaviour, nothing in the stored data breaks: **MINOR**. 5.5.0 is
  what was asked, and it agrees.
- `SECURITY.md` supports the latest minor line only (5.4.x today).
- The Avalonia set moves only as a whole, with what Enigma.Avalonia.Desktop is built against.
- A run never tags, pushes or merges into `develop`/`main`. Those commands are printed. Neither the
  FEATURE-BB60 run nor the BUG-7823 run is in `develop` yet, and this run's branch carries both, so
  merging it into `develop` brings all three.

## Steps

1. Check `git tag --list 5.5.0` is empty.
2. `Directory.Build.props`: `<Version>5.5.0</Version>`.
3. `RELEASENOTES.md`: a dated `## 5.5.0` section on top, in 5.4.0's shape:
   - a summary;
   - one sub-section per area (*The diff*, *Fixes*);
   - *Upgrading from 5.4*;
   - *Dependencies*;
   - *Version*.
4. `README.md`:
   - the what's-new callout names 5.5 and its highlights, and keeps the 5.1.0 and 4.x pointers;
   - the diff viewer's *Features* bullets say what is now true: text selection and copy, Ctrl+F,
     syntax highlighting, and wrapping in the unified view only.
5. `SECURITY.md`: 5.5.x supported; 5.4.x no longer.
6. `docs/RELEASE.md`: the MSI paragraph lists 5.5.0 among the releases without a profile.
7. Dependency check (`dotnet list package --outdated`), logged:
   - non-coupled patch/minor bumps applied;
   - the Avalonia set held back;
   - the new AvaloniaEdit packages and their licences recorded.
8. Pre-flight in Release: the build is clean, the suite is green, and the assemblies carry `5.5.0`.
9. Print the runbook.

## Acceptance criteria

- The Release build is clean with zero warnings, and the Release suite is green.
- The built assemblies carry 5.5.0.
- `RELEASENOTES.md` covers FEATURE-BB60, BUG-7823 and the upgrade from 5.4.
- The README callout names 5.5, and its diff bullets match the app.
- `SECURITY.md` supports 5.5.x.
- Nothing in the dev runs `git tag`, `git push`, `dotnet pack` or `dotnet nuget push`.

## Out of scope

- NuGet packaging; a Windows MSI profile.
- Moving the Avalonia set.
- Tagging, merging, pushing.
- Reporting the AvaloniaEdit clamp upstream (BUG-7823's follow-up).

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| Version number | 5.5.0 | As asked. New behaviour, and the stored data does not break, so SemVer MINOR agrees | 5.4.1 (new behaviour); 6.0.0 (no stored-data or install break) |
| What the release contains | FEATURE-BB60 and BUG-7823, both fixes | All of it is on `HEAD` and none of it is in `5.4.0` | Leaving out the arrange-clamp fix: it is the fix that held |
| The unasked changes (no side-by-side wrap, no row selection) | Called out under *Upgrading from 5.4* | A 5.4 user loses something they may have used; they should read why | Burying it in the feature list |
| README features | The diff bullets are updated, not just the callout | BB60's completion doc left them for the release, and the list would otherwise omit selection, search and highlighting | Callout only |
| Dependency refresh | Checked and logged. Non-coupled bumps applied; the Avalonia set held; the new AvaloniaEdit packages recorded with their licences | The house rule for a minor release. A new native component (`onigwrap`) ships for the first time | Moving the set |
| `SECURITY.md` | 5.5.x supported, 5.4.x unsupported | The file's own rule | Supporting both lines |
| MSI profile, tag | None; bare `5.5.0`, printed | The convention every release from 1.0.0 to 5.4.0 set | Generating an MSI; `v5.5.0` |
| Breakdown | One item, one dev | A release is one reviewable commit of the version and its documents | Splitting the notes from the version |
| Where the run starts | `bugfix/bug-7823-arrange-clamp @ 316362f`, as checked out | It is the only branch carrying the fix that held; the release must ship it | Cutting from `develop` (lacks both runs) |
