# BUG-546B — Tool dialog headers stay on top

**Status:** TODO
**Type:** BUG
**Branch:** `bugfix/bug-546b-fixed-tool-dialog-headers`
**Run:** feature/2026-10-01-polish-release-5-1

## Objective

In the Branches, Tags and Remotes dialogs, the header holds the filter, the sort and the *Create*
button. When the list is long, scrolling it down takes the header with it and the header disappears.
The header must stay fixed at the top while only the list scrolls. In the Branches dialog the manual
merge band, under the header, must stay fixed too.

## Context & constraints

- **The pages are already laid out for it.** `BranchesPageView`, `TagsPageView` and
  `RemotesPageView` are each a `DockPanel`: the header docked `Top` (and in Branches the
  `ManualMerge` band docked `Top` under it), then a `Grid` holding the `ListBox`. Given a bounded
  height, the list scrolls and the docked strips do not.
- **The cause is the card.** `ToolDialogService` shows the page as the `ToolDialog` host's content,
  and the Enigma `ContentDialog` wraps its content in a `ScrollViewer`. A scrolling `ScrollViewer`
  measures its child at infinite height, so the page is laid out at the list's full height, the list
  has nothing of its own to scroll, and the card's bar moves the whole page, header included. This was
  measured in BUG-1AEA for the diff dialog: extent 84 069 against a 695 viewport. That fix
  (`ScrollBarVisibility.Disabled` on the card's viewer, from the body's `AttachedToVisualTree`, guarded)
  left with the diff dialog in FEATURE-F04E.
- **The card's height does not depend on the content:** `MainWindow.SizeToolDialog` sets
  `DialogWidth`, `DialogHeight` and `DialogMaxHeight` from the window, so a page bounded by the card
  is bounded at every window size.
- `ToolDialog` shows only these three pages. `HostDialog` (the questions) is separate and keeps its
  scrolling.
- `ToolDialogTests` drives the real `MainWindow` with its real `ToolDialog` host.
- The branches page's auto-scroll while dragging (BUG-876A PHASE03) scrolls the list's own
  `ScrollViewer`, which only has something to scroll once the list is bounded.

## Steps

1. Confirm with a headless test first: the Branches dialog in the real window over enough branches to
   overflow. The card's `ScrollViewer` extent exceeds its viewport, and the list's equals its own.
2. `ToolDialogService.ShowAsync`: when the page is attached under the card, turn off the card's own
   scrolling (`ScrollBarVisibility.Disabled`, both directions). Guard it: a card with no
   `ScrollViewer` above the page is left alone. Write the reasoning and the BUG-1AEA reference in a
   remark. Detach the handler when the dialog closes.
3. Tests (one per dialog, on the real window, with lists long enough to overflow):
   - the card has nothing to scroll (extent ≤ viewport);
   - the list's own viewer has more content than its viewport;
   - after scrolling the list to its end, the header is still at the top of the card, whole and
     visible, and in Branches the manual merge band too;
   - a short list still lays out as before.
4. Whole suite.

## Acceptance criteria

- In the Branches, Tags and Remotes dialogs, scrolling a long list leaves the header (filter, sort,
  *Create*) fixed at the top, and in Branches the manual merge band fixed under it.
- Only the list scrolls; the card shows no scroll bar of its own.
- Questions asked on `HostDialog` keep scrolling as before.
- Build clean with zero warnings; the whole suite green.

## Out of scope

- Restyling the headers or the merge band.
- Making the merge band collapsible.

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| Where to fix it | Once, in `ToolDialogService`, for every tool page | One cause, three dialogs. The service is what puts a page in the card | A fix in each page's code-behind; re-templating the library's dialog |
| How | Turn off the card's `ScrollViewer` for the page (guarded) | The one state in which the presenter measures the page against the room it has. The page's own `DockPanel` then does the rest. Proven in BUG-1AEA | Wrapping each list in another `ScrollViewer` (still measured at infinity) |
| Type | BUG | A header that scrolls away is a layout defect of the dialogs | FEATURE |
