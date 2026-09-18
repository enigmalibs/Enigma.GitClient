# BUG-1AEA — The diff dialog scrolls everything

**Item:** BUG-1AEA — The diff dialog scrolls everything
**Branch:** `bugfix/bug-1aea-dialog-pane-scrolling`
**Run:** feature/2026-09-18-history-and-diffs

## Summary

The commit dialog's one scrollbar moved the changed-files list and the diff together. Each pane now
scrolls itself, and the dialog has nothing left to scroll.

The cause was measured rather than guessed. A headless capture of the opened dialog over a
2 000-line patch and a 41-file commit reported the card's `ScrollViewer` at `extent 84069` against
`viewport 695`, with the body laid out 84 069 px tall and **every** inner viewport equal to its own
extent. The card wraps its content in a `ScrollViewer` — that is what the control library's
`DialogMaxHeight` is for, and it is right for a dialog whose content is a paragraph — and a
scrolling `ScrollViewer` measures its child with infinite height. So the body asked for the full
height of the patch, got it, and handed each list exactly the height it wanted; no list had anything
left over to scroll, and the one bar that did have something was the card's.

`ScrollBarVisibility.Disabled` is the one state in which a scroll presenter measures its child
against the room it actually has, so the fix is to turn the card's own scrolling off for this body,
once, when it is attached. The card's height does not depend on its content — `DialogHeight` follows
the page through `DialogSizing.Fill` — so the body is then bounded by the card at every window size,
with no chrome constant to drift. It is guarded: a future version of the library that templates its
card differently leaves the page behaving exactly as it did.

That also gave the diff back its virtualisation. Measured at infinite height, the diff's
`VirtualizingStackPanel` realised every row of the patch the moment a file was picked — 2 000 rows
for a 2 000-line file. Bounded, it realises what is on screen.

The changed-files list and tree took `ScrollViewer.HorizontalScrollBarVisibility="Auto"` in place of
`Disabled`, so a long name or a deep tree can be reached rather than merely trimmed. The diff list
keeps `Disabled` deliberately: a diff list that scrolls sideways takes its line-number gutter with
it, which is what `BUG-1D34` and `BUG-0DC2` were about, and the viewer's own character-counted bars
are what move its text.

## Files / modules touched

**Modified — App**

- `Views/Pages/HistoryPageView.axaml.cs` — `OnDialogBodyAttached`: finds the card's `ScrollViewer`
  and disables both of its scroll directions, with the measurement and the reasoning in a remark
- `Views/Panels/ChangedFilesPanelView.axaml` — the list and the tree scroll horizontally on demand

**Modified — tests**

- `tests/Enigma.GitClient.App.UnitTests/HistoryPageTests.cs` — `DiffDialog_LetsEachPaneScrollItself`
  over a commit big enough to overflow both panes: the card has nothing left to scroll, the body is
  no taller than the card, and each list has more content than its own viewport; plus a `ScrollOf`
  helper and a `WaitUntilAsync` for the two asynchronous reads

**Modified — docs**

- `docs/roadmap.md`, `docs/plan/BUG-1AEA.md`

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| Where the scroll is disabled | On the card's viewer, from the body's `AttachedToVisualTree` | The body has to be in the tree before its ancestors can be walked, and attaching is the one moment that is true and the card is known |
| Horizontal too, not just vertical | Both | The same infinite measurement applies sideways; a body measured at infinite width would let a wide diff push the card |
| Finding the list's scroll in the test | A helper that starts from the visible `ListBox` | The first attempt took the first `ScrollViewer` under the panel, which is the filter `TextBox`'s — 19 px tall, extent equal to viewport, and green for the wrong reason |
| Proving the fix | Running the new test against the old behaviour | With the two lines removed it fails on the card's extent; a test that only passes on the fixed code could be passing because nothing rendered |

## Deviations & follow-ups

- **None from the plan.** All six acceptance criteria are covered.
- **A second defect fixed by the same change, unreported:** the diff list realised every row of a
  patch instead of virtualising, because it was measured at infinite height. It is named here
  because it is the same root cause, not a separate fix.
- **Follow-up.** The conflict-resolution page's three-way view is not in a dialog and never had this
  problem; it still has no horizontal bars of its own, which `BUG-0DC2` already recorded.
- **Line endings (recommendation only):** no CRLF churn in the touched files. No action taken.

## Build/test evidence

```
dotnet build Enigma.GitClient.slnx
  0 Warning(s)
  0 Error(s)

dotnet test --solution Enigma.GitClient.slnx
  Test run summary: Passed!
  total: 1677  failed: 0  succeeded: 1677  skipped: 0
```

One test is new, and it was run against the unfixed code to confirm it fails there. One fix cycle
was needed, for the scroll viewer the test first picked up.

## Documentation sweep

Scanned `README.md` and `RELEASENOTES.md`. Neither describes how the dialog scrolls — they describe
what it shows, which has not changed — so this dev made nothing in either untrue. There is no
`CLAUDE.md`, `CHANGELOG.md` or `CONTRIBUTING.md` in the repository. Nothing edited.
