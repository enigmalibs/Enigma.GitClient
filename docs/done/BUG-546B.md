# BUG-546B — Tool dialog headers stay on top

**Item:** BUG-546B — Tool dialog headers stay on top
**Branch:** `bugfix/bug-546b-fixed-tool-dialog-headers`
**Run:** feature/2026-10-01-polish-release-5-1

## Summary

In the Branches, Tags and Remotes dialogs, a long list now scrolls under its header. The header (the
filter, the sort and *Create*) stays at the top, and in Branches so does the manual merge band under
it. Only the list scrolls, and the card shows no bar of its own.

- **Cause, confirmed first by the new test failing in all three dialogs.**
  - The Enigma `ContentDialog` wraps its content in a `ScrollViewer`, and a scrolling `ScrollViewer`
    measures its child at infinite height.
  - Each page was laid out at its list's full height. In a 653-high card the card's extent was
    Branches 1 593, Tags 1 786 and Remotes 1 835.
  - So the list had nothing of its own to scroll, and the card's bar moved the whole page, header
    included.
  - This is the defect BUG-1AEA fixed for the diff dialog. That fix left with the dialog in
    FEATURE-F04E, and the tool dialogs (FEATURE-7514) never had it.
- **Fix: one handler in `ToolDialogService`, for every tool page.**
  - When the page is attached under the card, `BoundByTheCard` sets the card's `ScrollViewer` to
    `ScrollBarVisibility.Disabled` in both directions.
  - That is the one state in which the presenter measures the page against the room it has. The
    window sets the card's size, so each page is bounded at every window size. Its `DockPanel` then
    keeps the docked strips at the top and gives the list the rest.
  - It is guarded: a card with no `ScrollViewer` above the page is left alone.
  - The handler is subscribed before the page becomes the card's content, and removed when the dialog
    closes.
- **What it does not touch.** `HostDialog`, where every question is asked, is a different host. The
  handler only walks the tool page's own ancestors, so questions keep their scrolling.

## Files / modules touched

**Modified**

- `src/Enigma.GitClient.App/Services/ToolDialogService.cs`: `BoundByTheCard`, with the measurement
  and the reasoning in its remarks; subscribed and unsubscribed in `ShowAsync`.
- `tests/Enigma.GitClient.App.UnitTests/ToolDialogTests.cs`: the theory
  `ALongList_ScrollsUnderItsHeader_WhichStaysAtTheTop`, once per dialog.
  - It runs on the real `MainWindow` with its real `ToolDialog`, over a real repository holding 40
    branches, 40 tags and 30 remotes.
  - The card's extent is no taller than its viewport, and the list's own viewer has more than its
    viewport.
  - After the list is scrolled to its end, the docked strips are at the same place and drawn whole:
    the header, plus the merge band in Branches (2 strips).
  - Helpers: `BuildCrowdedRepositoryAsync`, which writes every reference in one `git update-ref
    --stdin` and the remotes straight into `.git/config`; `Git`; `AssertWhollyShown`.
- `docs/roadmap.md`, `docs/plan/BUG-546B.md`: statuses.

**Created**

- `docs/done/BUG-546B.md`

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| When the card's scrolling is turned off | On the page's `AttachedToVisualTree` | Its ancestors can be walked only once it is in the tree. Attaching is the one moment that is true and the card is known. BUG-1AEA's choice |
| Horizontal too | Both | The same infinite measure applies sideways. A page measured at infinite width could let a wide row push the card |
| Building 110 references for the test | One `update-ref --stdin` plus a config append | Two processes rather than a hundred and ten |
| A short list "as before" | Covered by the existing `ShowAsync_ShowsThePageWithItsViewModelUntilItIsClosed` cases, which open each page empty | They still pass with the card's scrolling off |

## Deviations & follow-ups

- **None from the plan.**
- **A side effect worth knowing:** the branches page's auto-scroll while dragging (BUG-876A PHASE03)
  scrolls the list's own `ScrollViewer`, which in the dialog had nothing to scroll. Now that the list
  is bounded it does. Not tested separately here: `BranchesPageTests` covers the auto-scroll on the
  page itself.
- **Virtualisation:** measured at infinite height, the lists realised every row. Bounded, they realise
  the rows on screen.
- Line endings: no CRLF churn.

## Documentation sweep

Nothing in `README.md` or the other docs describes how the tool dialogs scroll. No edit. The fix goes
into the 5.1.0 notes with FEATURE-0C53.

## Build/test evidence

- Before the fix, all three cases failed with `the card scrolls the page: an extent of 1593 / 1786 /
  1835 in a viewport of 653`.
- `dotnet build Enigma.GitClient.slnx`: 0 warnings, 0 errors.
- `dotnet test --solution Enigma.GitClient.slnx`: **2642 passed**, 0 failed, 0 skipped (3 new).
- Fix budget: 0 cycles used.
