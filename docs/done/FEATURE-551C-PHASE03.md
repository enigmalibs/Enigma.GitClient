# FEATURE-551C-PHASE03 — Double-click a badge to check out

**Item:** FEATURE-551C — Branches: grouping, reset, double-click
**Phase:** PHASE03 — Double-click a badge to check out
**Branch:** `feature/feature-551c-phase03-double-click-checkout`
**Run:** feature/2026-10-02-tags-branches-release

## Summary

A double-click on a branch badge in the history checks that branch out, as GitKraken does. It runs
the badge's own *Check out* command, so it behaves exactly as the menu item does:

- a local branch is checked out;
- a remote branch whose local branch is elsewhere asks PHASE02's *Reset local to here* question; with
  no local branch, the tracking branch is created;
- a grouped badge (PHASE01) checks its local branch out;
- the checked-out branch does nothing (the command cannot execute), with no question and no notice.

These are unchanged:

- a single click (no checkout);
- a double-click on the line beside the badges (it leaves the line selected and checks nothing out);
- drags.

The checkout is posted to the dispatcher, so the list finishes handling the press before the question
or the reload arrives.

With this phase, **FEATURE-551C is done**.

## Files / modules touched

**Modified**

- `src/Enigma.GitClient.Desktop/Views/Pages/HistoryPageView.axaml.cs`: `OnPointerPressed` recognises
  a left double-click (`ClickCount == 2`) on a branch badge; `CheckOut` posts the badge's command.
- `docs/roadmap.md`: PHASE03 and FEATURE-551C `DONE` (the table re-pads back to its widths without
  `IN PROGRESS`).
- `docs/plan/FEATURE-551C.md`: statuses.

**Created**

- `tests/Enigma.GitClient.Desktop.UnitTests/HistoryDoubleClickTests.cs` (5 tests)
- `docs/done/FEATURE-551C-PHASE03.md`

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| Where the gesture is recognised | The page's tunnelling `OnPointerPressed`, on the second press | It is where the page already reads presses on badges (drags, toggles), before the list handles them |
| Press or `DoubleTapped` | The press's `ClickCount` | Same handler, same hit-testing (`BadgeAt`) as the drag. No second routed-event subscription to keep in step |
| Running the command | `Dispatcher.UIThread.Post`, then `CanExecute`/`Execute` | As `LetGoOf` does for a line: the gesture ends first. `CanExecute` is what makes the checked-out branch a no-op |
| How the tests wait | `RunJobs`, then the command's `ExecutionTask` | The checkout runs on the posted job, and its task is what tells the test it has finished |

## Deviations & follow-ups

- None from the plan.
- Line endings: no CRLF churn; the touched files are LF.

## Documentation sweep

Nothing became wrong. README's *Checkout of anything in the graph: a branch from its badge's menu…*
is still true. The double-click is described in FEATURE-4D5A's release notes. No `CLAUDE.md` or
`AGENTS.md` exists.

## Build/test evidence

- `dotnet build Enigma.GitClient.slnx`: 0 warnings, 0 errors.
- `dotnet test --solution Enigma.GitClient.slnx`: **2687 passed**, 0 failed, 0 skipped (5 new).
- New tests (headless window, real input, real git):
  - a double-click on a local badge checks it out;
  - a single click does not;
  - a double-click on the checked-out branch does nothing;
  - on a remote whose local is elsewhere it asks *Reset "topic" to "origin/topic"?*;
  - a double-click on the line checks nothing out and leaves the line selected.
- Fix budget: 0 cycles used.
