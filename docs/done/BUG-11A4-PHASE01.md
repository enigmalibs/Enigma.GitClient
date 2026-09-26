# BUG-11A4-PHASE01 — A drag the page runs itself

**Item:** BUG-11A4 — A drag the compositor cannot refuse
**Branch:** `bugfix/bug-11a4-phase01-in-app-drag`
**Run:** feature/2026-09-21-icons-tracking-dragging

## Summary

Dragging one branch onto another no longer opens a platform drag session. The page runs the gesture
itself, from pointer events: press, threshold, capture, move, release.

This is the third attempt at the refusal pointer, and the first two failed the same way — they changed
what the page *says* about the drag, and the pointer is not drawn from anything the page says. Reading
the decompiled `Avalonia.X11` 12.1.1 settled it:

- `X11DragSource.Handler.TryGetXdndTargetInfo` looks a window up in `platform.Windows` **first**. Our
  own window is there, so the drag is resolved as an in-process target and delivered straight to
  `DragDropDevice`: over our own window **no `XdndEnter`, `XdndPosition` or `XdndDrop` is ever sent**.
  The protocol side of the drag is never accepted by anybody, because it is never offered to anybody.
- `Handler.UpdateCurrentEffects` does ask for the right pointer — `XChangeActivePointerGrab` with
  `dnd-move` for a `Move` effect — from the very first moment of the gesture. It is asking a compositor
  that is not listening: this is a Wayland session with the application under XWayland, the compositor
  bridges the X drag the moment `XdndSelection` changes hands, and for the duration it owns the pointer
  and paints the refusal for a drag nothing has accepted.

So the cursor could not be fixed from inside that session by anything the application is allowed to
say. What the application *can* do is not open one. This gesture never leaves the window — a branch row
dropped on another row of the same list, carrying the live row object — so the platform session bought
nothing and owned the one thing that was wrong.

Everything the gesture did, it still does: the ring on the row a drop would land on, the list scrolling
while a branch is held near an edge, the menu naming both branches with the three merges, the click
that is still a click. Two things are new. The row under the pointer is hit-tested rather than read off
the event, because the capture makes this view the source of every pointer event for the length of the
gesture. And the whole thing is now **testable**: a platform drag session cannot be driven headlessly,
a pointer can, so press-move-release is covered end to end for the first time since the feature was
written.

## Files / modules touched

**Modified — App**

- `Views/Pages/BranchesPageView.axaml.cs` — the gesture: `_dragging` for the row being carried, a
  pointer capture on the list, `Steer` on every move, `OnPointerReleased` resolving the pair and
  opening the menu, `StopDragging` for every way it ends. `DragAsync` and the four `DragDrop` handlers
  are gone, and so are `BranchDragGesture.EffectFor` and `IsLeavingTheList` — replaced by
  `IsOverTheList`, which the capture made necessary. `DropMenu` and `IsDragging` are internal, for the
  tests. The class comment records why this page does not use the platform's drag machinery
- `Views/Pages/BranchesPageView.axaml` — `DragDrop.AllowDrop` removed from the list
- `ViewModels/Pages/BranchesPageViewModel.cs` — `BranchDrop.DragFormat` and `BranchDrop.TransferFor`
  removed; the pair, the request and the menu headers are untouched

**Modified — tests**

- `tests/Enigma.GitClient.App.UnitTests/BranchesPageTests.cs` — the platform-drag tests replaced by the
  gesture's own: the whole press-move-release offering the three merges with both names and the page's
  commands; the ring following the pointer and let go at the end; a click that selects and starts
  nothing; a release on no row; a drag onto a row the policy refuses. Plus `IsOverTheList` as a theory,
  and `Drag` / `Centre` helpers

**Modified — docs**

- `docs/roadmap.md`, `docs/plan/BUG-11A4.md`

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| What the release reads the target from | A hit test at the pointer, in the list's coordinates | With the pointer captured the event's source is the capture target, so the source cannot say which row is under the pointer any more. The pointer can, and the same call serves the move and the release |
| How a test sees what a drop offered | `internal ContextMenu? DropMenu` on the view | The menu is the page's answer to a drop; keeping it is honest (it was being dropped on the floor before) and it lets a test read the three items without reaching into a popup |
| Whether the press still carries the `PointerPressedEventArgs` | No — `PendingDrag` keeps the row and the origin | The event only existed to hand to `DoDragDropAsync`. Holding an event object past its handler is the kind of thing that quietly goes stale |
| `ForgetHover` after an in-house drag | Kept | The capture takes the pointer the same way the platform session did, so the row it was taken from never gets its exit. It is cheap and self-correcting on the next move |
| The `AllowDrop` assertion in the list test | Inverted rather than deleted | "This page is not a platform drop target" is now a property worth stating: it is what the bug was about |

## Deviations & follow-ups

- **None from the plan.** All five acceptance criteria are met; the cursor itself is PHASE02.
- **Lost with the platform session:** a branch dragged onto a terminal or an editor no longer types its
  name. That was a side effect of `BUG-1A42-PHASE01`'s workaround, never a request, and it is not in
  the README. If it turns out to be wanted, a "Copy branch name" item on the row's menu is the way to
  offer it — a drag out of the window would bring the refusal pointer back with it.
- **A flaky test, not this dev's:** `Enigma.GitClient.Core.IntegrationTests.Sync.SyncServiceTests.FetchAsync_ReportsItsProgress`
  failed once again and passed on the re-run. It asserts that git narrated a local-path fetch on
  stderr, which git decides for itself. It has now failed twice in this run, on devs that touch nothing
  it can reach; it is being fixed as its own item rather than smuggled into this one.
- **Line endings (recommendation only):** no CRLF churn in the touched files. No action taken.

## Build/test evidence

```
dotnet build Enigma.GitClient.slnx
  0 Warning(s)
  0 Error(s)

dotnet test --solution Enigma.GitClient.slnx
  Test run summary: Passed!
  total: 1812  failed: 0  succeeded: 1812  skipped: 0
```

Five tests and one seven-case theory added; seven platform-drag tests removed with the machinery they
covered (1816 → 1812). No fix cycle: everything this dev touches was green on the first run, and the
one failure in that run was the unrelated flake above, which passed on re-run.

## Documentation sweep

`README.md` and `RELEASENOTES.md` describe the drag by what it does — the menu naming both branches,
the three merges, the list scrolling while a branch is held near an edge — and all of that is exactly
as it was. Nothing in either became wrong. There is no `CLAUDE.md`, `CHANGELOG.md` or `CONTRIBUTING.md`
in the repository. No edits.
