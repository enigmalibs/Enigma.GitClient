# BUG-876A-PHASE03 — The list scrolls while dragging

**Item:** BUG-876A — Branch rows: selection and dragging
**Branch:** `bugfix/bug-876a-phase03-drag-autoscroll`
**Run:** feature/2026-09-20-ui-polish-diff-page

## Summary

A branch held near the top or the bottom of the branches list now scrolls it, so a branch below the
fold can be reached and dropped on. Until now a repository with more branches than fit on screen
simply could not be merged by dragging: the list stayed where it was, and the target was never
reachable.

The decision is a function — how far to scroll for a pointer at a given height in a viewport — and
the plumbing is a 30 ms `DispatcherTimer`, because a pointer held still reports nothing and a branch
held over the bottom edge is exactly the gesture that has to keep scrolling.

The speed grows with how far into the band the pointer is, so nudging the edge creeps and pushing
past it runs, and it never falls to nothing inside the band: a list that stops scrolling just short
of its end is a list whose last row cannot be dropped on. The band is a third of the viewport at
most, so a short list still has a middle.

The timer stops on drag-leave, on drop, when the pointer returns to the middle, and when the drag
session ends however it ended — nothing keeps ticking after the gesture.

## Files / modules touched

**Modified — App**

- `Views/Pages/BranchesPageView.axaml.cs` — `BranchDragGesture.ScrollFor` with its band, its step
  and its depth ramp; the timer; `FollowTheEdge`, `ScrollTowardsTheEdge`, `StopScrolling` and the
  cached list scroll

**Modified — tests**

- `tests/Enigma.GitClient.App.UnitTests/BranchesPageTests.cs` — the middle of the list scrolls by
  nothing, each edge scrolls towards itself, a pointer past the edge is capped at one step, deeper
  is faster, the band never falls to nothing inside itself, a viewport of nothing scrolls by nothing
  rather than by `NaN`, and a short list still has a middle

**Modified — docs**

- `README.md`, `RELEASENOTES.md`, `docs/roadmap.md`, `docs/plan/BUG-876A.md`

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| What drives the scrolling | A 30 ms dispatcher timer | A pointer held still raises no events, and holding still over an edge is the whole gesture |
| The band | 32 px, capped at a third of the viewport | Deep enough to aim at, and a cap so a short list is not one long edge with no middle |
| The speed | A quarter of a step to a full step, by depth | Nudging creeps and pushing runs; never zero inside the band, or the last row stays out of reach |
| A pointer past the edge | Capped at one step | A fast pointer leaving the window should not launch the list |
| When it stops | Leave, drop, the middle, and the end of the session | Four ways in, four ways out: a timer left running after a drag is a scroll nobody asked for |
| Where the decision lives | A function beside the other two | The same reason as the threshold and the cursor: this is what a headless test can call |

## Deviations & follow-ups

- **None from the plan.** All three acceptance criteria are covered.
- Only the branches list scrolls. The tags list takes no drops, and nothing else on the page is a
  drop target, so there is nothing there to reach.
- **Line endings (recommendation only):** no CRLF churn in the touched files. No action taken.

## Build/test evidence

```
dotnet build Enigma.GitClient.slnx
  0 Warning(s)
  0 Error(s)

dotnet test --solution Enigma.GitClient.slnx
  Test run summary: Passed!
  total: 1775  failed: 0  succeeded: 1775  skipped: 0
```

Nine tests added (1766 → 1775). No fix cycle: green on the first run.

## Documentation sweep

- `README.md` and `RELEASENOTES.md` — the drag-to-merge bullet now says the list scrolls when a
  dragged branch is held near an edge, which is what makes the gesture usable in a repository with
  more branches than fit on screen.

There is no `CLAUDE.md`, `CHANGELOG.md` or `CONTRIBUTING.md` in the repository.
