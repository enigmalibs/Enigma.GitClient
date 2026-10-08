# FEATURE-62B7 — A simpler About dialog

**Item:** FEATURE-62B7 — A simpler About dialog
**Branch:** `feature/feature-62b7-simple-about`
**Run:** vibe/2026-10-08-file-panel-about-release

## Summary

The About dialog is now a plain box:

- the application's icon;
- its name;
- the version;
- the build line, when the build recorded a revision;
- the copyright;
- a single **Close** button.

**Removed:**

- the "About" title and the Info icon under it, which the card drew as a column on the left of the
  content;
- the *BUILT WITH* box.

**How:**

- `AboutDialogService`:
  - sets `Title = null` and no `IconData`, the pattern the tool dialogs and the host repository browser
    already use for an untitled card;
  - makes *Close* the default button, so Enter closes the box;
  - drops the static Info glyph and its Phosphor usings.
- `AboutView.axaml`: the *BUILT WITH* border and its three styles (`caption`, `credit`, `license`) are
  removed. Everything else is unchanged: the 96 px icon, the name, "Version X", the build line (the
  revision stays selectable for bug reports) and the copyright.
- `AboutViewModel`:
  - `CreditEntry`, `DefaultCredits` and `Credits` are removed;
  - the testing constructor is `(version, buildSha, copyright)`.

## Files / modules touched

**Modified**

- `src/Enigma.GitClient.Desktop/Services/AboutDialogService.cs`
- `src/Enigma.GitClient.Desktop/ViewModels/Dialogs/AboutViewModel.cs`
- `src/Enigma.GitClient.Desktop/Views/Dialogs/AboutView.axaml`
- `src/Enigma.GitClient.Desktop/Views/Dialogs/AboutView.axaml.cs` (summary comment only)
- `tests/Enigma.GitClient.Desktop.UnitTests/AboutDialogTests.cs`:
  - the credits test is removed;
  - the service test asserts no title, no icon, and *Close* as the only and default button;
  - the "opens from" tests tell the dialog by its `AboutView` content, now that it has no title;
  - the view test asserts the name, version, build and copyright, and no *BUILT WITH* and no list;
  - the in-window test asserts that no "About" title is drawn, and that *Close* sits under the view on
    its right.
- `docs/roadmap.md`, `docs/plan/FEATURE-62B7.md`: statuses.

**Created**

- `docs/done/FEATURE-62B7.md`

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| How to tell the dialog apart in the "opens from" tests | By its content (`AboutView`) | It has no title to compare any more |
| How strictly to pin "bottom right" | *Close* under the view, with its right edge past the view's centre | The card lays the button row out itself; this holds whether it right-aligns the button or stretches it |

## Deviations & follow-ups

- **The Desktop suite is not run** (your instruction for this session). The updated tests are written
  and compiled.
- **Worth a click before release:** open About from the repository toolbar, from Settings and from the
  start window. Check that there is no left column, no *BUILT WITH*, and *Close* at the bottom right.
- **Follow-up — third-party notices.** The in-app list carried only names and licence ids, never the
  notices that MIT and BSD ask to be kept with copies. A `THIRD-PARTY-NOTICES` file shipped beside the
  published application is the proper home for that attribution. It is suggested, not done.
- The product name is kept above the version (planning decision). Delete its `TextBlock` in
  `AboutView.axaml` if you want the box even barer.
- Line endings: the touched files are LF; no CRLF churn.

## Documentation sweep

Nothing stale:

- `docs/RELEASE.md` mentions the About dialog's version and build line, which are both still there.
- `README.md` does not describe the dialog. Its "built with Avalonia" is the project's own
  description.
- Older release notes are history.
- No `CLAUDE.md` or `AGENTS.md` exists.

## Build/test evidence

- `dotnet build Enigma.GitClient.slnx`: 0 warnings, 0 errors.
- `Enigma.GitClient.Core.UnitTests`: 1165 total, 0 failed, 1 skipped (the AtomicFile race excluded on
  Windows, BUG-6EAA).
- `Enigma.GitClient.Core.IntegrationTests`: 358 total, 0 failed, 2 skipped.
- `Enigma.GitClient.Desktop.UnitTests`: **compiled, not run**, at your instruction.
- Fix budget: 0 cycles.
