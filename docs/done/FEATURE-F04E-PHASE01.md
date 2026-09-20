# FEATURE-F04E-PHASE01 — The diff view replaces the history

**Item:** FEATURE-F04E — Diffs take the whole page
**Branch:** `feature/feature-f04e-phase01-full-page-diff`
**Run:** feature/2026-09-20-ui-polish-diff-page

## Summary

What a commit changed now takes the whole history page instead of a card drawn over it. The
`ContentDialog` is gone; in its place a `Border` named `DiffPage` sits as the last child of the
page's root panel, painted `EnigmaBackgroundBrush`, visible exactly while the page says so. It
carries a header — a back button, the commit's subject, and its author, date, short hash and body —
over the same changed-files / splitter / diff layout the dialog held.

**The graph underneath is left laid out, not collapsed.** That is the one decision the rest of the
dev follows from: `IsVisible="False"` on the history `DockPanel` would re-measure the list at zero
height and clamp its scroll offset, so closing the diffs would drop the reader back at the top of
the history. Covered by an opaque panel instead, the list keeps its viewport, its offset and its
selection, and coming back is free.

Two things went with the dialog:

- **`Controls/DialogSizing.cs`** — it existed to multiply the page's bounds by 0.94 for the card. A
  panel fills the page by being a panel.
- **The card-scrolling workaround** (`OnDialogBodyAttached`, from `BUG-1AEA`) — the control
  library's card wraps its content in a `ScrollViewer` that measures its child with infinite height,
  which had to be forced to `Disabled` or the patch was laid out 84 000 px tall. A panel inside the
  page has a real height, so the two panes get real viewports with nothing to work around.

The page's state is renamed to say what it now drives: `IsDiffViewOpen` and `CloseDiffViewCommand`.
When it is set and cleared has not changed at all — a double-click or the row's menu opens it, and
losing the selection still puts it away.

## Files / modules touched

**Modified — App**

- `ViewModels/Pages/HistoryPageViewModel.cs` — `IsDiffDialogOpen` → `IsDiffViewOpen`,
  `CloseDiffDialogCommand` → `CloseDiffViewCommand`, and the documentation that described a dialog
- `Views/Pages/HistoryPageView.axaml` — the `DiffPage` panel with its header and the back button,
  replacing the `ContentDialog`; the `contentDialog` namespace is gone
- `Views/Pages/HistoryPageView.axaml.cs` — the dialog's plumbing removed: `ShowAsync`/`HideAsync`,
  the `Closed` handler, the property watcher and the card-scrolling workaround; what is left is the
  columns, the viewport reporting and the double-click

**Deleted — App**

- `Controls/DialogSizing.cs`

**Modified — tests**

- `tests/Enigma.GitClient.App.UnitTests/HistoryPageTests.cs` — the `DiffDialog_*` section becomes
  `DiffView_*` against the panel's visibility: it stays closed on a mere selection, opens on a
  double-click and from the menu, names the commit, takes the whole page and follows a resize while
  the graph stays laid out underneath, closes from the command and from the back button without
  losing the selection, goes away with the selection, comes back from the menu, and lets each pane
  scroll itself. `DialogSizing_TakesMostOfWhatItIsGiven` is gone with the class
- `tests/Enigma.GitClient.App.UnitTests/TagsAndCheckoutTests.cs` — the renamed state

**Modified — docs**

- `RELEASENOTES.md`, `docs/roadmap.md`, `docs/plan/FEATURE-F04E.md`

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| Over or instead of the graph | Over it, opaque | The list keeps its scroll offset and its realised rows; a collapsed list is re-measured at nothing and comes back at the top |
| The way out | One back button, top left | A full page has a back, not a cross; a cross would read as a dialog, which is what this dev stopped being |
| The header's colour | `EnigmaSurfaceBrush`, with a rule under it | It is the same strip the history page draws above its own list, so the page keeps one shape in both states |
| The commit's meta line | The house `dim` / `faint` roles | The same quiet columns the graph's own rows use, now that those roles exist |
| The state's name | `IsDiffViewOpen` | It says what it drives; a name that says "dialog" is the next reader's bug |
| What the tests read | The panel's `IsVisible`, and a named subject `TextBlock` | The dialog's `IsOpen` and `Title` no longer exist, and visibility is what the reader actually sees |

## Deviations & follow-ups

- **None from the plan.** All five acceptance criteria are covered.
- Escape is **not** wired yet: it was the dialog's until this dev and is PHASE02's whole subject.
  Between the two commits the ways out are the back button and the row menu.
- **One unexplained test failure, not reproduced.** The first full-suite run after this dev's build
  reported `failed: 1` out of 1743 without the name surviving in the captured output; six
  consecutive full runs since — and every targeted App run — are green at 1743. Nothing in this
  dev's diff is timing-dependent, and the App suite is headless UI work where a first run after a
  rebuild is the slowest. Recorded rather than explained away: if it returns, the name is what is
  needed, so future runs in this run's remaining devs keep their full logs.
- **Line endings (recommendation only):** no CRLF churn in the touched files. No action taken.

## Build/test evidence

```
dotnet build Enigma.GitClient.slnx
  0 Warning(s)
  0 Error(s)

dotnet test --solution Enigma.GitClient.slnx        (six consecutive runs)
  Test run summary: Passed!
  total: 1743  failed: 0  succeeded: 1743  skipped: 0
```

The count falls from 1747 to 1743: the four `DialogSizing` theory cases went with the class, one
dialog test was replaced by the back-button one, and the rest were rewritten in place. No fix cycle
on the code; the only build error was in the tests that named the renamed members, which is the same
edit as the rename.

## Documentation sweep

- `RELEASENOTES.md` — the graph section said a double-click opened the changes "in a dialog over the
  graph … it closes from the cross at its top right, the Close button at its bottom, or Escape".
  That is now wrong in every clause; it names the full page and the back button instead.
- `README.md` — its diff and graph bullets never mentioned a dialog, so nothing there became wrong.

There is no `CLAUDE.md`, `CHANGELOG.md` or `CONTRIBUTING.md` in the repository.
