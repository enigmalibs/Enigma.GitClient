# BUG-11A4 — A drag the compositor cannot refuse

**Status:** DONE
**Type:** BUG
**Branch:** one per phase, see below
**Run:** feature/2026-09-21-icons-tracking-dragging

## Objective

Dragging one branch row onto another never draws the "impossible" pointer — the white circle with a red
border and a red bar across it — because the gesture no longer opens a platform drag session at all.

## Context & constraints

This is the **third** attempt, and the first two failed for the same reason: they changed what the
application says about the drag, and the pointer is not drawn from anything the application says.

- `BUG-876A-PHASE02` made `EffectFor` report `DragDropEffects.Move` everywhere over the list. The
  symptom survived.
- `BUG-1A42-PHASE01` added `DataFormat.Text` beside the in-process row, on the diagnosis that an empty
  `XdndTypeList` left the compositor with nothing to offer. `PHASE02` answered `DragEnter` as well as
  `DragOver`. The symptom survived.

What this run established, by decompiling `Avalonia.X11` 12.1.1 (`ilspycmd`) and reading the real
implementation rather than inferring it:

- `X11DragSource.Handler.TryGetXdndTargetInfo(window)` looks the window up in `platform.Windows`
  **first**. Our own window is in there, so the target is resolved as an *in-process* one and the drag
  is delivered straight to `DragDropDevice`: over our own window, **no `XdndEnter`, `XdndPosition` or
  `XdndDrop` is ever sent**. The X11 protocol side of this drag is never accepted by anybody, because
  it is never even offered to anybody.
- `Handler.UpdateCurrentEffects` does call `XChangeActivePointerGrab(..., GetCursor(effects), 0)`, and
  `GetCursor` maps `Move` to `StandardCursorType.DragMove` → the `dnd-move` library cursor. So Avalonia
  *is* asking for the right pointer, from the first moment of the gesture (the constructor sets the
  cursor from the allowed effects before any event is processed).
- The session here is Wayland (`XDG_SESSION_TYPE=wayland`, KDE) and the application is an XWayland
  client. The compositor bridges an X11 drag — it sees `XdndSelection` change hands — and for its
  duration the compositor owns the pointer and paints the drag cursor. An X client's
  `XChangeActivePointerGrab` does not win that argument, and a bridged drag that nothing has accepted
  is drawn as a refusal. Which is exactly what is on screen, for the whole gesture, while the ring, the
  auto-scroll and the drop all work perfectly — because those are the in-process path.
- So the pointer cannot be fixed from inside a platform drag session: not by the effect we report, not
  by the formats we advertise, not by anything else the application is able to say.
- **What the application can do is not open one.** This gesture never leaves the window: a branch row
  is dropped on another branch row of the same list, and the payload is the live row object. A platform
  drag session buys nothing here and costs the cursor. Driven from pointer events with a pointer
  capture, the gesture keeps every behaviour it has — the ring, the auto-scroll, the menu — and the
  cursor becomes an ordinary `Cursor` property, which this application already sets successfully
  elsewhere (the history page's column grip).
- `StandardCursorType.DragMove` resolves to the `dnd-move` cursor, which the machine's Breeze theme
  provides (`/usr/share/icons/breeze_cursors/cursors/dnd-move`). It is the closed hand a desktop draws
  while something is being carried.
- What is lost is a side effect PHASE01 of the previous attempt added: a branch dragged onto a terminal
  or an editor typed its name. Nothing asked for it, and it was a consequence of the workaround rather
  than of the feature. Recorded as a follow-up.
- What is gained, besides the pointer: the gesture becomes **testable**. A platform drag session cannot
  be driven in a headless test; a pointer can — `window.MouseDown` / `MouseMove` / `MouseUp` drive the
  real page through the real input system, so the drag is covered end to end for the first time.
- **Baseline:** clean build, 1803 tests green.

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| Whether to try the platform again | No | Two attempts have changed what the page says, and the decompiled source says the pointer is drawn by the compositor for a drag that was never offered over the protocol. A third variation of the same idea is a third failure | Advertising more formats; asking for `Copy` instead of `Move`; sending Xdnd messages to ourselves (not ours to send) |
| What replaces it | A pointer-driven gesture the page owns: press, threshold, capture, move, release | The drag never leaves the window, so every part of the platform session was overhead — and the one thing it insisted on owning is the thing that is wrong | A drag preview window of our own (a second thing to get wrong, for a gesture that is two rows apart) |
| The cursor while dragging | `StandardCursorType.DragMove` over the list, the list's normal cursor elsewhere | It is the "carrying something" pointer, it exists in the theme, and setting a cursor is a thing this application already does. Over the list means "this gesture is under way", which is the question the pointer answers — where the drop would land is the ring's job | `No` outside the list (re-introducing the refusal we are removing); no cursor change at all (the gesture would look like nothing is happening) |
| Hit testing while the pointer is captured | `BranchList.InputHitTest` at the pointer, then the `ListBoxItem` ancestor | With a capture, the event's source is the capture target, so the source can no longer say which row is under the pointer. The hit test can | Not capturing (moves outside the list stop arriving, and the release is lost) |
| How the gesture ends | A release over a row opens the same menu; a release elsewhere does nothing; Escape and a lost capture cancel it | A platform session had a cancel of its own; an in-house one has to bring its own, and Escape is what every desktop means by "stop" | Committing the drop wherever the pointer is; leaving cancellation out |
| The in-process data format | Removed | `BranchDrop.DragFormat` and `TransferFor` existed to hand a row to the platform and get it back. The source row is now simply a field on the view for the length of the gesture | Keeping them unused |
| What the drop itself does | Unchanged | The menu, the three merges, the policy and the operations service are not what is broken | Revisiting the drop's behaviour while fixing its pointer |

## PHASE01 — A drag the page runs itself

**Branch:** `bugfix/bug-11a4-phase01-in-app-drag`
**Status:** DONE — see `docs/done/BUG-11A4-PHASE01.md`

### Steps

1. `Views/Pages/BranchesPageView.axaml.cs`: the press still records a pending drag and the threshold
   still decides when it becomes one — what changes is what happens next. Instead of
   `DragDrop.DoDragDropAsync`, the view remembers the row being dragged, captures the pointer on the
   branches list, and from then on decides everything from pointer moves: the row under the pointer
   (hit-tested, because a capture replaces the event's source), the drop ring, and the auto-scroll.
2. Same file: the release ends the gesture — capture released, ring and scrolling taken down — and,
   when the pointer is over a row the policy accepts, opens the same menu on that row's container. The
   menu is kept as a field so a test can read what a release offered.
3. Same file: the `DragDrop` handlers (`DragEnter`, `DragOver`, `DragLeave`, `Drop`) and `DragAsync` go,
   along with `BranchDragGesture.EffectFor` and `IsLeavingTheList`'s drag-event caller; the file keeps
   a comment recording why this page does not use the platform's drag machinery.
4. `Views/Pages/BranchesPageView.axaml`: `DragDrop.AllowDrop` is removed from the list — the page no
   longer takes platform drops.
5. `ViewModels/Pages/BranchesPageViewModel.cs`: `BranchDrop.DragFormat` and `BranchDrop.TransferFor` are
   removed; `BranchDrop` keeps the pair, the request and the menu headers.
6. Tests — `tests/.../BranchesPageTests.cs`: the removed helpers' tests go; in their place, the real
   gesture driven through the real input system — a press on one row, a move onto another and a release
   resolve the pair and offer the three merges naming both branches; a move puts the ring on the row
   under the pointer and takes it off the one before; a press-and-release without movement is a click
   that selects and offers nothing; a release away from any row offers nothing; and the page exposes no
   platform drop target any more.

### Acceptance criteria

- Dragging a branch row onto another still opens the menu naming both ends, with the same three items.
- The ring still marks the row a drop would land on, and the list still scrolls while a branch is held
  near an edge.
- A click is still a click: it selects the row and starts nothing.
- No platform drag session is opened and no `DragDrop` event is handled by the page.
- Build clean with zero warnings; the whole suite green.

## PHASE02 — A pointer that says yes, and a way out

**Branch:** `bugfix/bug-11a4-phase02-cursor`
**Status:** DONE — see `docs/done/BUG-11A4-PHASE02.md`

### Steps

1. `Views/Pages/BranchesPageView.axaml.cs`: `BranchDragGesture.CursorFor(bool isOverTheList)` — the
   drag-move cursor over the list, none elsewhere — as a named function beside the other gesture
   decisions, so what the pointer says is a value a test can read.
2. Same file: the branches list wears that cursor for the length of the gesture and gets its own back
   when the gesture ends, however it ends. The cursors are created once, not per pointer move.
3. Same file: Escape cancels a drag in progress — ring down, scrolling stopped, cursor restored,
   capture released, nothing dropped — and so does losing the capture. A cancelled drag opens no menu.
4. Tests — `tests/.../BranchesPageTests.cs`: the cursor function answers `DragMove` over the list and
   nothing elsewhere; the list carries the drag cursor while a drag is under way and its own afterwards;
   Escape during a drag leaves no ring, no menu and the original cursor; and a drag cancelled that way
   can be started again.

### Acceptance criteria

- While a branch is dragged over the list the pointer says the gesture is under way, and never draws
  the refusal.
- The list's cursor is back to normal after a drop, after a cancel and after a lost capture.
- Escape cancels a drag without dropping anything.
- Build clean with zero warnings; the whole suite green.

## Out of scope

- Dragging tags, remotes, or more than one row at a time.
- Dropping a branch onto another application, or accepting a drop from one.
- A drag preview or ghost image following the pointer.
- Changing which pairs may be dropped, or what a drop does.
