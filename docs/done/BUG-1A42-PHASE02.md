# BUG-1A42-PHASE02 — Every drag event gets an answer

**Item:** BUG-1A42 — The drag cursor still says no
**Branch:** `bugfix/bug-1a42-phase02-drag-enter`
**Run:** feature/2026-09-21-refs-tags-and-dragging

## Summary

The page now states what a drag may do on **every** drag event it receives, and stops tearing its
own drop mark down mid-gesture.

`DragDropDevice` does not raise `DragOver` for every pointer move. It raises `DragOver` only while
the *deepest element* under the pointer is the same as it was last time, and otherwise raises a
`DragLeave` on the element being left and a `DragEnter` on the one being arrived at. An element here
is a text block, an icon, a border — so crossing a list of branch rows takes that second path almost
every time. `BranchesPageView` handled `DragOverEvent` alone: on all those moves it never stated its
effect, never set the drop ring, and never asked for the auto-scroll — and its `DragLeave` handler
undid the ring and stopped the scrolling although the drag had not gone anywhere.

`DragEnterEvent` is now handled by the same handler as `DragOverEvent`; the two ask the same
question. And `DragLeave` acts only when the pointer has really left the list — which the event
itself cannot say, because its source is the element being *left* and that is inside the list either
way, so the decision is taken from the pointer's position against the list's bounds.

## Files / modules touched

**Modified — App**

- `Views/Pages/BranchesPageView.axaml.cs` — `BranchDragGesture.IsLeavingTheList(Point, Size)` beside
  the other gesture decisions; `DragEnterEvent` registered to `OnDragOver`; `OnDragLeave` gated on
  the pointer having left the list

**Modified — tests**

- `tests/Enigma.GitClient.App.UnitTests/BranchesPageTests.cs` — the leave decision inside the list,
  at each of its four edges and outside each of them; and the page answering a real `DragEnter` as
  well as a real `DragOver` with `Move`, marking it handled, and putting the drop ring on the row
  under the pointer from either

**Modified — docs**

- `docs/roadmap.md`, `docs/plan/BUG-1A42.md`

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| What tells a real leave from a spurious one | The pointer's position against the list's bounds | The plan said to test "whether the event's source is still over the list". That cannot work: on a leave the source is the element being left, which is inside the list whether the drag is moving to its neighbour or off the window. The pointer is the only thing that knows |
| Where the bounds come from | `e.GetPosition(BranchList)` against `BranchList.Bounds.Size` | It keeps the helper a pure function of a point and a size, which is what makes it testable without a drag session |
| How the enter test raises its event | A real `DragEventArgs` raised on a realised `ListBoxItem` | It goes through the same routing the platform uses, so it proves the handler is registered for that event and not merely that a method exists |

## Deviations & follow-ups

- **One deviation, recorded above:** the leave decision is taken from the pointer rather than from
  the event's source, because the source cannot answer the question the plan asked it. The
  behaviour the step asked for is what shipped.
- With `DragEnter` handled, the spurious leave would have been harmless anyway — the enter follows
  it synchronously, before anything is drawn. Gating it is still right: it is what stops the
  auto-scroll timer being stopped and restarted every few pixels, and it says plainly which leave
  the page cares about.
- **Line endings (recommendation only):** no CRLF churn in the touched files. No action taken.

## Build/test evidence

```
dotnet build Enigma.GitClient.slnx
  0 Warning(s)
  0 Error(s)

dotnet test --solution Enigma.GitClient.slnx
  Test run summary: Passed!
  total: 1803  failed: 0  succeeded: 1803  skipped: 0
```

Eight tests added (1795 → 1803). No fix cycle: green on the first run. The enter test would have
failed before the change — the event had no handler, so it came back unhandled and with no effect.

## Documentation sweep

`RELEASENOTES.md` describes the drop menu and the list scrolling while a branch is held near an
edge; both are what this dev makes more reliable rather than different, so nothing in it became
wrong. `README.md` says the same in one line. No edits. There is no `CLAUDE.md`, `CHANGELOG.md` or
`CONTRIBUTING.md` in the repository.
