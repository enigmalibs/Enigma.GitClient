# BUG-876A-PHASE01 — Deselected rows go back to normal

**Item:** BUG-876A — Branch rows: selection and dragging
**Branch:** `bugfix/bug-876a-phase01-drag-threshold`
**Run:** feature/2026-09-20-ui-polish-diff-page

## Summary

A branch row that loses the selection goes back to looking like every other row, and a click on one
is a click.

Both came from the same line: the page started a platform drag session from **every** left press on
a branch row. A drag session takes the pointer, so the row under it never received the exit that
clears its hover — which is the light-grey plate a row kept after the selection had moved elsewhere
— and a plain click spent its life inside a drag, which is where the "no" cursor came from.

Now a press only **remembers** the row it landed on, and the drag starts from the first pointer move
past a four-pixel threshold. The session is still started from the press itself, because that is
what `DragDrop.DoDragDropAsync` takes — it is the pointer it tracks, and a pointer still down is
still that one — but *when* it starts is the move.

For the state a drag may still strand, the page clears it explicitly: when the session ends, every
realised row is told to forget `:pointerover`. It has to be the pseudo-class rather than the
property, because the property belongs to the input system, which never saw the pointer leave. The
next pointer move puts the state back on the row it is really over.

## Files / modules touched

**Modified — App**

- `Views/Pages/BranchesPageView.axaml.cs` — `BranchDragGesture.IsDrag` and its threshold; the
  `PendingDrag` a press records; the move that starts the session; the release and the capture loss
  that forget it; `DragAsync` with the `ForgetHover` that runs whatever the session did

**Modified — tests**

- `tests/Enigma.GitClient.App.UnitTests/BranchesPageTests.cs` — the threshold: a press and release
  in place is a click, a one-pixel tremor is a click, a deliberate move in any direction is a drag,
  and the threshold is a distance rather than a box

**Modified — docs**

- `docs/roadmap.md`, `docs/plan/BUG-876A.md`

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| What starts the session | Still the press, from the move | `DoDragDropAsync` takes `PointerPressedEventArgs` — a move is not one — so the press is held and used when the move says it is a drag |
| The threshold | 4 px, as a distance | What a desktop toolkit means by a drag; a distance rather than a box so the gesture behaves the same in every direction |
| Forgetting the hover | The pseudo-class, on every realised row | The input system never saw the pointer leave, so nothing else will clear it; clearing all of them and letting the next move re-apply is simpler than guessing which row the pointer is over |
| Where the tags list fits | Cleared too | It shares the page and the pointer, and a stranded hover is no better there |
| What is tested | The threshold | Headless Avalonia has no drag backend, so the session cannot be driven; the decision it depends on can |

## Deviations & follow-ups

- **None from the plan.** All three acceptance criteria are covered.
- The drag gesture itself is still untestable here — there is no headless drag backend. The two
  phases that follow cover their own decisions the same way: as functions the tests can call.
- **Line endings (recommendation only):** no CRLF churn in the touched files. No action taken.

## Build/test evidence

```
dotnet build Enigma.GitClient.slnx
  0 Warning(s)
  0 Error(s)

dotnet test --solution Enigma.GitClient.slnx
  Test run summary: Passed!
  total: 1761  failed: 0  succeeded: 1761  skipped: 0
```

Six tests added (1755 → 1761). One fix cycle: `DoDragDropAsync` takes the press's own event
arguments, so the pending drag holds them rather than starting from the move's.

## Documentation sweep

`README.md` and `RELEASENOTES.md` describe the drag by what it does — "drag one branch onto another
to merge them" — and say nothing about when it starts, so the diff made nothing in them wrong. No
edits. There is no `CLAUDE.md`, `CHANGELOG.md` or `CONTRIBUTING.md` in the repository.
