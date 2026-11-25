# CODE-REVIEW-6E56-PHASE19 — [Medium] XAML-02 One edge auto-scroll

## Summary

The branches dialog, the history and the repositories list each carried their own copy of the drag
gesture's edge scrolling: `FollowTheEdge`, `StopScrolling`, `ScrollTowardsTheEdge`, a
`DispatcherTimer` and an `_autoScrollBy` field. A fix to one list left the other two behaving
differently.

What changed:

- `Views/EdgeAutoScroll` (Desktop, new, internal) owns the 30 ms timer, the step
  (`BranchDragGesture.ScrollFor`) and the way to the list's `ScrollViewer`, which it is given as a
  function because two lists find theirs in a template:
  - `Follow(PointerEventArgs)` measures the pointer against that scroll viewer;
  - `Follow(y, height)` takes the numbers itself;
  - `Stop()`;
  - `afterStep`, an optional callback after each step that moved the list (with its layout
    refreshed first);
  - `Step` and `IsScrolling` for the tests, and `ScrollOnce()`, what the timer does on each tick.
- The three views keep what is theirs and hand the edge to it:
  - the branches dialog: `new EdgeAutoScroll(ListScroll)`;
  - the history: `new EdgeAutoScroll(() => _listScroll)`;
  - the repositories list: `new EdgeAutoScroll(() => RepositoryScroll, OpenTheGap)`. It follows with
    `Follow(pointer.Y, RepositoryScroll.Bounds.Height)`, the height it always measured against, and
    after each step its dashed tile follows the pointer, as before.
- 159 lines removed from the three views, 15 added. `FollowTheEdge`, `StopScrolling` and
  `ScrollTowardsTheEdge` exist once, in the helper.

## Files/modules touched

- Created: `src/Enigma.GitClient.Desktop/Views/EdgeAutoScroll.cs`
- Created: `tests/Enigma.GitClient.Desktop.UnitTests/EdgeAutoScrollTests.cs`
- Modified: `src/Enigma.GitClient.Desktop/Views/Pages/BranchesPageView.axaml.cs`
- Modified: `src/Enigma.GitClient.Desktop/Views/Pages/HistoryPageView.axaml.cs`
- Modified: `src/Enigma.GitClient.Desktop/Views/Pages/RepositoriesPageView.axaml.cs`
- Modified: `docs/roadmap.md`, `docs/plan/CODE-REVIEW-6E56.md`
- Created: `docs/done/CODE-REVIEW-6E56-PHASE19.md`

## Deviations & follow-ups

- **The press / move / release / capture-lost state machine stays in each view.** The plan shares it
  "only where it is identical", and it is not. The history also tracks a press that may toggle the
  selection, marks the moves of a drag handled, drags a branch badge into a `ListBox`, and steers
  differently. The branches dialog drags a tree row. The repositories list drags a tile with a ghost
  and a gap. A shared machine would need hooks for each, a refactor whose safety net is the Desktop
  suite that cannot run here. The duplication the finding measured, the edge scrolling, is gone.
- **The headless platform's timers.** The harness found that a `DispatcherTimer` on Avalonia's
  headless platform fires only under `Dispatcher.UIThread.MainLoop`, never under `RunJobs`: a bare
  timer ticked 0 times in 300 ms of `RunJobs` and 9 times in 300 ms of `MainLoop`. The new tests
  therefore drive the helper through `ScrollOnce()`, not through the timer. A future Desktop test
  that waits for a `DispatcherTimer` by pumping `RunJobs` will wait forever (a note for the test
  infrastructure, not a product issue).
- `using Avalonia.Threading` was removed from `RepositoriesPageView.axaml.cs`, which no longer uses
  it.

## Build/test evidence

- Baseline: see `docs/done/CODE-REVIEW-6E56-PHASE01.md`.
- **This dev:**
  - `dotnet build Enigma.GitClient.slnx --no-incremental`: 0 warnings, 0 errors.
  - Core unit: 1534 run, 1531 passed, 0 failed, 3 skipped.
  - Core integration: 449 run, 447 passed, 0 failed, 2 skipped (6 min 13 s).
  - Desktop: compiled, **not run** (this machine's standing rule).
- **Throwaway harness outside the repository** (Avalonia 12.1.4 headless platform, a real
  `ScrollViewer` 300 px tall over 3,000 px of content). Every check passed:
  - near the bottom it scrolls down by 23.25 px a step;
  - one step moves exactly that;
  - the middle of the list stops it;
  - near the top it scrolls up and stops at the top;
  - a step that moved the list tells the view, and at the end the list stays and nothing is said;
  - with no scroll viewer it stops;
  - held still for 300 ms under the dispatcher's main loop, the timer scrolled 162.75 px (7 steps);
  - after `Stop`, nothing moves.
- **New tests (Desktop, compiled, not run)** in `EdgeAutoScrollTests` (5):
  - scrolls down near the bottom, and stops when the pointer leaves the band;
  - scrolls up near the top, no further than the top;
  - after each step that moved the list the view is told, and at the end it is not;
  - `Stop` stops;
  - a list with no scroll viewer yet stops.
- **Fix budget:** 0 cycles.

## Documentation sweep

- Nothing stale: no user doc describes the code-behind.

## Decisions taken at build time

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| How the helper reaches the scroll viewer | A `Func<ScrollViewer?>` | Two lists find theirs in a template, after construction; the third is a named element | Passing a `ScrollViewer` (the two templated lists have none at construction) |
| The repositories list's height | `Follow(y, height)`, called with the `Bounds.Height` it always used | Its behaviour stays exactly as it was | Moving it to `Viewport.Height` (a silent behaviour change) |
| Sharing the drag state machine | Not shared | The three machines differ in what they track and how they steer. Only the identical part is shared, as the plan says | A generic machine with hooks (large, unverifiable here) |
| How the tests drive it | `ScrollOnce()` directly | The headless platform does not fire dispatcher timers under `RunJobs` | Pumping a main loop with a real-time timeout in unit tests |
