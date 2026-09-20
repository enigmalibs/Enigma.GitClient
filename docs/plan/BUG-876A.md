# BUG-876A — Branch rows: selection and dragging

**Status:** TODO
**Type:** BUG
**Branch:** one per phase, see below
**Run:** feature/2026-09-20-ui-polish-diff-page

## Objective

Three defects of the same gesture on the branches page: a deselected row keeps a grey plate, the
pointer says a drop is impossible while it is being dragged, and a long list will not scroll while
something is held over it.

## Context & constraints

- `Views/Pages/BranchesPageView.axaml.cs` starts a drag **from the press itself**: every left
  `PointerPressed` on a branch row calls `DragDrop.DoDragDropAsync`, which opens a platform drag
  session for what is very often an ordinary click. A drag session takes the pointer, so the
  `ListBoxItem` under it never receives the exit that clears its `:pointerover` state — which is the
  grey plate left on a row after the selection has moved elsewhere. It is also why a click that
  selects a row flashes a drag cursor.
- `Avalonia.Input.DragDrop.DoDragDropAsync` takes a `PointerEventArgs`, not specifically the press:
  a move event is a legal trigger, which is what a threshold needs.
- `OnDragOver` sets `DragDropEffects.None` for every position the policy would not accept —
  including the source row itself, which the pointer necessarily crosses on the way out. `None` is
  what the platform draws as the "no" cursor, so the gesture begins by saying it is impossible.
- `BranchesPageViewModel.CanDrop` is the policy — no self-drop, never a remote target — and it must
  keep deciding what happens on drop and which row is marked. Only what the *cursor* says changes.
- The list is a `ListBox` inside its own `ScrollViewer`; nothing scrolls it during a drag, so a
  branch below the fold cannot be reached at all. Avalonia has no built-in drag auto-scroll.
- Pseudo-classes are settable through `((IPseudoClasses)control.Classes).Set(":pointerover", false)`,
  which is the only way to clear a state the platform never delivered the exit for.
- Headless Avalonia has no drag backend, so the gesture itself cannot be driven in a test: what is
  testable is the decision — when a move becomes a drag, what effect a position deserves, and how
  far a pointer near an edge should scroll. Those are pulled out as pure functions and tested;
  the wiring is covered by the page still building and laying out.
- **Baseline:** clean build, 1728 tests green.

## PHASE01 — Deselected rows go back to normal

**Branch:** `bugfix/bug-876a-phase01-drag-threshold`
**Status:** TODO

### Steps

1. `Views/Pages/BranchesPageView.axaml.cs`: the press no longer starts a drag. It remembers the row
   and where the pointer was; a tunnelling `PointerMoved` starts the drag only once the pointer has
   travelled past a threshold while the button is still down, and the pending state is dropped on
   release, on capture loss and when the drag has started.
2. Same file: the threshold as a small, named, testable helper — a point, a current position and
   the distance that counts as a drag.
3. Same file: when the drag session ends, `:pointerover` is cleared from every row container that
   is not under the pointer, so a state the platform never delivered the exit for cannot outlive the
   gesture.
4. Tests — `tests/.../BranchesPageTests.cs`: the threshold helper says no for a press-and-release in
   place and for a one-pixel tremor, and yes past the threshold in any direction; the page still
   builds, lays out and selects rows.

### Acceptance criteria

- Clicking a branch row selects it and starts no drag at all.
- Selecting another row leaves the first one with no background of its own — no grey plate, no
  residue.
- Dragging still starts as soon as the pointer is deliberately moved with the button down, and the
  drop menu still opens.
- Build clean with zero warnings; the App and Core unit suites green.

## PHASE02 — The drag cursor says yes

**Branch:** `bugfix/bug-876a-phase02-drag-cursor`
**Status:** TODO

### Steps

1. `Views/Pages/BranchesPageView.axaml.cs`: while a branch drag is over the branch list, the effect
   reported is `Move` — including over the source row and over a group heading — so the pointer
   never claims the gesture is impossible while it is under way.
2. Same file: the drop mark is unchanged: only a row the policy accepts is ringed, so the reader is
   still told where a drop would land.
3. Same file: the drop itself is unchanged — a position the policy refuses opens no menu, which is
   already the behaviour and is what keeps the ring honest.
4. Same file: a drag that is not a branch's, or that is outside the list, keeps `None`.
5. Tests — `tests/.../BranchesPageTests.cs`: the effect decision as a pure function — a branch
   payload over the list is `Move` whether or not the pair is valid, anything else is `None` — and
   `CanDrop` still refuses the pairs it refused, so the ring and the menu are unaffected.

### Acceptance criteria

- Dragging a branch shows the move cursor from the first pixel, over its own row and over every
  other row of the list.
- The ring still appears only on a row the drop would act on.
- Dropping on a row the policy refuses still does nothing and opens no menu.
- Build clean with zero warnings; the App and Core unit suites green.

## PHASE03 — The list scrolls while dragging

**Branch:** `bugfix/bug-876a-phase03-drag-autoscroll`
**Status:** TODO

### Steps

1. `Views/Pages/BranchesPageView.axaml.cs`: while a drag is over the list, a pointer inside a band
   at the top or the bottom scrolls the list towards that edge, on a dispatcher timer, at a speed
   that grows with how far into the band the pointer is.
2. Same file: the band, the speed and the step as a small named helper taking the pointer's
   position and the viewport height and returning how far to scroll — nothing about timers or
   controls, so it can be tested.
3. Same file: the timer starts on the first drag-over that asks for it and stops on drag-leave, on
   drop and when the pointer leaves the band, so nothing keeps ticking after the gesture.
4. Tests — `tests/.../BranchesPageTests.cs`: the helper returns zero in the middle of the list,
   negative near the top, positive near the bottom, and more the deeper into the band the pointer
   is; the page still builds and lays out.

### Acceptance criteria

- Holding a dragged branch near the top or the bottom of the branches list scrolls it, so a branch
  further down the list can be reached and dropped on.
- The scrolling stops as soon as the pointer leaves the band, the drag leaves the list, or the
  branch is dropped.
- Scrolling during a drag leaves the selection and the drop mark alone.
- Build clean with zero warnings; the App and Core unit suites green.

## Out of scope

- Auto-scrolling any other list, or dragging anything else.
- Dragging more than one row, or dragging a tag or a remote.
- Changing what a drop does, or which pairs are allowed.
- A drag preview or ghost image.

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| Why a deselected row keeps a plate | A drag session opened by every press eats the pointer exit | It explains all three symptoms at once — the grey plate, the cursor on a click, and only on this page | Restyling the selected state (paints over the symptom and leaves the cursor bug) |
| When a drag starts | Past a small movement threshold, from the move event | It is what every platform means by a drag, and `DoDragDropAsync` accepts a move as its trigger | Keeping the press (the bug); a time-based hold (slower than a click, and unfamiliar) |
| The stuck hover state | Cleared explicitly when the session ends | The exit the platform never delivered has to come from somewhere | Trusting the next pointer move (the row keeps its plate until the pointer happens to cross it) |
| What the cursor says over an invalid row | `Move`, while the pointer is over the list | The gesture is possible — the reader is mid-drag — and the ring already says where it would land | Keeping `None` (the reported bug); `Link`/`Copy` (says something the drop does not do) |
| What still refuses | The ring and the drop | The policy is unchanged; only the cursor stops shouting | Allowing a self-drop or a remote target to open a menu |
| Auto-scroll shape | Edge bands on a dispatcher timer, speed by depth | It is what a file manager does, and a timer is the only way to keep scrolling while the pointer is still | Scrolling one step per drag-over event (stops the moment the pointer stops moving) |
| Where the logic lives | Pure helpers in the view's code-behind | Headless Avalonia has no drag backend, so the decisions are what can be tested — and they are decisions about a pointer, not about branches | Moving them to the ViewModel (the ViewModel would learn about pixels and viewports) |
