# BUG-11A4-PHASE02 — A pointer that says yes, and a way out

**Item:** BUG-11A4 — A drag the compositor cannot refuse
**Branch:** `bugfix/bug-11a4-phase02-cursor`
**Run:** feature/2026-09-21-icons-tracking-dragging

## Summary

The reported symptom, answered: while a branch is carried over the list the pointer is the drag-move
cursor, and it is never the refusal.

It is set the ordinary way — `BranchList.Cursor`, from the page, on every move — which is precisely
why it works where the platform drag session's own cursor did not. Avalonia's X11 source does ask for
`dnd-move` through `XChangeActivePointerGrab`; a compositor that has bridged the drag is not listening.
An ordinary cursor on an ordinary control is not part of that argument, and this application already
sets one successfully (the history page's column grip). `StandardCursorType.DragMove` resolves to the
`dnd-move` library cursor, which the machine's theme provides.

`BranchDragGesture.CursorFor` answers the same question the old `EffectFor` did — *is this gesture
under way here* — and deliberately has no third answer: off the list the list simply keeps its own
cursor, because the refusal pointer is the bug and re-introducing it as "our" refusal would be the same
bug with a different author. Where a drop would land is still said by the ring.

And the gesture now has a way out. A platform session brought its own cancel; an in-house one has to,
so Escape ends the drag where it stands — ring down, scrolling stopped, cursor restored, capture
released, nothing dropped — and every other way a drag can end goes through the same `StopDragging`.
Losing the capture is the one path that does not release it: whoever took the pointer owns it now.

## Files / modules touched

**Modified — App**

- `Views/Pages/BranchesPageView.axaml.cs` — `BranchDragGesture.CursorFor`; the `Carrying` cursor made
  once; the list's own cursor saved at the start of a gesture and given back by `StopDragging`;
  `OnKeyDown` for Escape; the captured pointer held in a field so every ending can release it

**Modified — tests**

- `tests/Enigma.GitClient.App.UnitTests/BranchesPageTests.cs` — `CursorFor` as a theory (drag-move over
  the list, nothing elsewhere, never a refusal); the list wearing the gesture's cursor during a real
  drag and its own afterwards; Escape taking the ring, the cursor and the drag down, offering nothing,
  and leaving a gesture that can be started again

**Modified — docs**

- `RELEASENOTES.md` (documentation sweep, below)
- `docs/roadmap.md`, `docs/plan/BUG-11A4.md`

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| What `CursorFor` returns | `StandardCursorType?` — the kind, not a `Cursor` | A `Cursor` is a platform handle: a function that made one per pointer move would allocate one per move, and a test would have nothing to compare. The kind is the decision; the view owns the handle |
| Where the list's own cursor comes from | Saved when the drag starts, restored when it ends | It is `null` today, meaning "inherit", and writing `null` back would be right by accident. Saving it means the gesture gives back exactly what it took |
| Escape's handler | `KeyDown`, tunnelling, on the view | The gesture belongs to this page, and a tunnelled handler sees the key before whatever is focused inside the page does — which for a drag started by clicking a row is the row itself |
| Whether a lost capture releases the capture | No | The capture is already gone, and calling `Capture(null)` would take the pointer away from whoever has just taken it |
| What the test asserts about the cursor | That it changes during the gesture and is the same object afterwards | Asserting a particular platform cursor instance would test Avalonia's cursor cache; what this dev promises is that the list wears the gesture's pointer and gets its own back |

## Deviations & follow-ups

- **None from the plan.** All four acceptance criteria are met.
- The pointer over the list is now `dnd-move` from the desktop's cursor theme. On a theme that does not
  provide it, Avalonia falls back to the platform default — never to the refusal, which is what this
  item is about.
- **Line endings (recommendation only):** no CRLF churn in the touched files. No action taken.

## Build/test evidence

```
dotnet build Enigma.GitClient.slnx
  0 Warning(s)
  0 Error(s)

dotnet test --solution Enigma.GitClient.slnx
  Test run summary: Passed!
  total: 1816  failed: 0  succeeded: 1816  skipped: 0
```

Four tests added (1812 → 1816). No fix cycle: green on the first run.

## Documentation sweep

`RELEASENOTES.md` describes the drag gesture in full — the menu, the checkout, the list scrolling near
an edge — so the one thing that is new to a reader was added there: Escape calls the whole thing off.
The cursor itself is not something either document ever described, and `README.md`'s two lines about
dragging remain accurate. There is no `CLAUDE.md`, `CHANGELOG.md` or `CONTRIBUTING.md` in the
repository.
