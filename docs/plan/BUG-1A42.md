# BUG-1A42 — The drag cursor still says no

**Status:** TODO
**Type:** BUG
**Branch:** one per phase, see below
**Run:** feature/2026-09-21-refs-tags-and-dragging

## Objective

Dragging one branch row onto another stops drawing the "impossible" pointer for the whole gesture.

## Context & constraints

This is the **second** attempt. `BUG-876A-PHASE02` already made `BranchDragGesture.EffectFor` report
`DragDropEffects.Move` everywhere over the list, and the symptom survived — so the cause is not what
the page answers. What this run found, by reading `Avalonia.X11` 12.1.1 and by driving the real
`DragDropDevice` in a headless probe:

- `X11DragSource.GetCursor` picks the pointer from the effect it last saw: `Copy`/`Move`/`Link` load
  the `dnd-copy` / `dnd-move` / `dnd-link` library cursors, and anything else loads
  `DragNoDropCursorHandle`, which is `dnd-no-drop`. Rendered from the machine's Breeze cursor theme,
  `dnd-no-drop` is a closed hand beside "a white circle with a red border and a red bar crossing
  diagonally" — the reported pointer exactly. So something is reporting no effect at all.
- It is not this page. A headless probe built the real branches page in a window and pushed real
  `RawDragEvent`s through the real `DragDropDevice`: `DragEnter` and `DragOver` both came back
  `Move` at every point tested over the list, and `DragDrop.GetAllowDrop` was `True` on the element
  the hit test returned. The page answers correctly.
- `Avalonia.X11.Selections.DataFormatHelper.ToAtoms(IReadOnlyList<DataFormat>, X11Atoms)` **skips
  every format whose `Kind` is the in-process one**. `BranchDrop.DragFormat` is created with
  `DataFormat.CreateInProcessFormat<BranchRowViewModel>(…)` and is the drag's only format, so
  `X11DragSource` publishes an **empty `XdndTypeList`** on the source window while still taking
  ownership of `XdndSelection`.
- This machine runs a Wayland session (`XDG_SESSION_TYPE=wayland`) with the application under
  XWayland. The compositor bridges an X drag into a Wayland one from `XdndSelection` and
  `XdndTypeList`: a drag offering no type at all is a drag nothing can accept, and the compositor
  owns the pointer for the duration and paints the refusal. Avalonia meanwhile delivers the drag
  **in process** — `TryGetXdndTargetInfo` finds the window in `platform.Windows` and calls
  `DragDropDevice` directly, never sending XdndEnter — which is precisely why the ring, the
  auto-scroll and the drop all work while the pointer says they cannot.
- A second, smaller defect of our own, reproducible headlessly: `DragDropDevice.DragOver` raises
  **`DragEnter`**, not `DragOver`, whenever the *deepest element* under the pointer changes — which,
  crossing a list of rows, is most pointer moves — and raises `DragLeave` on the element being left.
  `BranchesPageView` handles `DragOverEvent`, `DragLeaveEvent` and `DropEvent` only, so on those
  moves the page never states its effect, and its `DragLeave` handler tears down the drop ring and
  stops the auto-scroll although the drag has not left the list at all.
- `DataTransfer` takes several items; adding one costs a string per drag and nothing per move.
  `DataFormat.Text` maps to the X11 text atoms, which is a type any target can be offered.
- Headless Avalonia has no drag backend, so the platform session itself cannot be driven in a test.
  What is testable is everything up to it: which formats a drag carries, and what the page answers
  for each drag event — both are pulled out as functions the tests call.
- **Baseline:** clean build, 1775 tests green.

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| Why the previous fix did not work | It changed the answer, and the answer was never the problem | The probe shows the page reporting `Move`; the refusal is drawn by the platform for a drag that offers nothing | Assuming the effect logic was still wrong (it is not, and the tests from PHASE02 still pass) |
| The fix | Advertise `DataFormat.Text` — the branch's full name — beside the in-process row | It makes the drag a real drag to the platform, whose bridge then has a type to offer; the in-process item the drop reads is untouched | Dropping the in-process format for a serialised one (the drop would lose "is it remote" and "is it checked out", which the payload exists to carry); a custom byte format (a made-up MIME type, offered to no one, for no benefit) |
| Whether the text is a side effect worth having | Yes: dragging a branch into a terminal or an editor now types its name | It is the natural meaning of the format, and it costs one string | Advertising an empty text item (a drag that offers a type and then nothing is worse than either) |
| The `DragEnter` gap | Handle `DragEnter` with the same handler as `DragOver` | The two are the same question — where is the pointer and what does it carry — and Avalonia picks between them by whether the deepest element changed | Handling only `DragEnter` (the opposite gap); asking Avalonia not to split them (not ours to change) |
| The spurious `DragLeave` | Act on it only when the drag has really left the list | A leave between two children of the same list is not a leave; today it flickers the ring and stops the auto-scroll every few pixels | Ignoring `DragLeave` entirely (the ring would survive the drag leaving the page) |
| What is verifiable here | The formats a drag carries, and the answer for each event kind; the platform session is not | Honest about a headless harness with no drag backend, and it is what the probe could actually establish | Claiming the cursor itself is covered by a test |

## PHASE01 — A drag that offers something

**Branch:** `bugfix/bug-1a42-phase01-drag-payload`
**Status:** TODO

### Steps

1. `ViewModels/Pages/BranchesPageViewModel.cs`: `BranchDrop` gains a factory that builds the
   `DataTransfer` for a dragged row — the in-process row under `DragFormat`, and the row's full name
   under `DataFormat.Text` — with a comment recording why the second item exists: a drag whose only
   format is in-process is published to the platform with no type at all, and a platform that cannot
   offer a type draws the refusal for the whole gesture.
2. `Views/Pages/BranchesPageView.axaml.cs`: the drag starts from that factory instead of building
   the `DataTransfer` inline.
3. Tests — `tests/.../BranchesPageTests.cs`: the built transfer carries the row itself under the
   in-process format **and** the row's full name as text; its format list is not empty and contains
   a format that is not in-process; a remote row carries its full `origin/…` name; and
   `DropFor`-style reading of the in-process item is unchanged, so the drop still resolves the same
   pair.

### Acceptance criteria

- A branch drag advertises at least one format the platform can offer, as well as the in-process row.
- The dropped pair, the menu it opens and the merge it performs are unchanged.
- Dropping a branch on a text field or another application pastes the branch's full name.
- Build clean with zero warnings; the whole suite green.

## PHASE02 — Every drag event gets an answer

**Branch:** `bugfix/bug-1a42-phase02-drag-enter`
**Status:** TODO

### Steps

1. `Views/Pages/BranchesPageView.axaml.cs`: `DragDrop.DragEnterEvent` is handled by the same handler
   as `DragDrop.DragOverEvent`, so the effect, the drop ring and the auto-scroll are decided on every
   drag event rather than only on those where the deepest element under the pointer happened not to
   change.
2. Same file: the `DragLeave` handler clears the ring and stops the scrolling only when the drag has
   actually left the branches list — a leave raised on one child while the pointer moves to another
   child of the same list is not a leave.
3. Same file: the decision is a named function beside the other gesture helpers, taking whether the
   event's source is still over the list, so it can be tested without a drag session.
4. Tests — `tests/.../BranchesPageTests.cs`: the new function keeps the ring for a leave inside the
   list and drops it for one outside; the view registers a handler for `DragEnter` as well as
   `DragOver`; and the page still builds, lays out and drags as the existing tests require.

## Acceptance criteria

- The page states what a drag may do on every drag event it receives, not only on some of them.
- The drop ring stays on the row under the pointer while the drag crosses that row's children.
- The auto-scroll keeps running while a dragged branch is held near an edge.
- Build clean with zero warnings; the whole suite green.

## Out of scope

- Changing which pairs may be dropped, or what a drop does.
- A drag preview or ghost image.
- Dragging tags, remotes, or more than one row.
- Working around the platform's cursor by drawing one in the application.
