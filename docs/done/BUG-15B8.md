# BUG-15B8 — The repository browser is cut off

**Item:** BUG-15B8 — The repository browser is cut off
**Branch:** `bugfix/bug-15b8-repository-browser-width`
**Run:** feature/2026-10-01-polish-release-5-1

## Summary

The dialog that browses a connected account's repositories (*Browse* on a profile's integration) now
shows everything, in both windows. Before, its title, filter, visibility toggles and each row's
*Open* and *Clone* buttons were cut off at both sides.

- **Cause, confirmed first by the new test failing on the unfixed view.**
  - `HostRepositoriesDialogView` asked for `Width="760"`.
  - Both windows' `HostDialog` is at most 720 wide, and its card gives the content 670 of that (25
    each side).
  - The card centred the 760-wide view in those 670: it was drawn at x = −45 and clipped by 45 on each
    side.
  - It had been that way since the dialog appeared in 2.0.0 (FEATURE-1406 PHASE03).
- **Fix.** The view is `Width="660"`, with a comment saying where the number comes from. The rows'
  star column takes the difference, and a long name or description trims with an ellipsis as it
  already did. The host's bounds and every other dialog stay as they were.

## Files / modules touched

**Modified**

- `src/Enigma.GitClient.App/Views/Dialogs/HostRepositoriesDialogView.axaml`: the width, and why.
- `tests/Enigma.GitClient.App.UnitTests/HostRepositoriesDialogTests.cs`: one new theory,
  `TheDialogFitsTheWindowsDialogCard_NothingCutAtEitherSide`, run once on `MainWindow` and once on
  `StartWindow`.
  - It opens the real browser through the real `ContentDialogService` on each window's own
    `HostDialog`, over three repositories: one with a long description, one private, one with a long
    name.
  - It asserts that these are drawn whole inside every ancestor that clips, up to the window: the
    view; the filter; the three toggles; every row's *Open* and *Clone*.
  - Helpers: `AssertWhollyShown`, `Named`.
- `docs/roadmap.md`, `docs/plan/BUG-15B8.md`: statuses.

**Created**

- `docs/done/BUG-15B8.md`

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| 670 (the exact room) or less | 660 | Exactly the room would break on a one-pixel change in the library's card. 10 spare costs the rows nothing visible |
| How "not cut" is tested | The control's box, mapped into every clipping ancestor and the window, must lie inside each | That is what "cut" means on screen, and it does not depend on knowing which part of the library's template clips. The failure message names the ancestor and both boxes, which is how the 670 and the −45 were read |
| The description's `MaxWidth` (480) | Kept | With the long description in the test, the *Open* and *Clone* buttons are still whole: the star column bounds it first |

## Deviations & follow-ups

- **None from the plan.**
- The other views on `HostDialog` (About 380, Commit details 560, the forms 460–560 minimum) fit within
  the 670 already. Nothing to change.
- Line endings: no CRLF churn.

## Documentation sweep

Nothing in the README or the other docs describes the browser's size. No edit. The fix goes into the
5.1.0 notes with FEATURE-0C53.

## Build/test evidence

- Before the fix, both cases of the new theory failed with: `the dialog's content is drawn at -45, 0,
  760, 480 in a ScrollContentPresenter of 670, 480: it is cut off`.
- `dotnet build Enigma.GitClient.slnx`: 0 warnings, 0 errors.
- `dotnet test --solution Enigma.GitClient.slnx`: **2627 passed**, 0 failed, 0 skipped (2 new).
- Fix budget: 0 cycles used.
