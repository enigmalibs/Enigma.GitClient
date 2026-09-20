# BUG-876A-PHASE02 — The drag cursor says yes

**Item:** BUG-876A — Branch rows: selection and dragging
**Branch:** `bugfix/bug-876a-phase02-drag-cursor`
**Run:** feature/2026-09-20-ui-polish-diff-page

## Summary

Dragging a branch no longer tells the reader the gesture is impossible while they are making it.

The drag-over handler answered the wrong question. It reported `DragDropEffects.Move` only where
the policy would accept a drop, and `None` everywhere else — including over the dragged branch's own
row, which the pointer necessarily crosses on the way out, and over every group heading. `None` is
what a platform draws as the "no" pointer, so a drag that was going to work perfectly well began by
saying it could not.

Now the cursor answers "is this gesture under way here": a branch dragged anywhere over the branches
list reports `Move`. Where a drop would actually land is still said by the ring on the row, which is
still the policy's decision — and a drop the policy refuses still opens no menu and does nothing.
Outside the list, or for anything that is not one of this list's branches, the effect stays `None`.

## Files / modules touched

**Modified — App**

- `Views/Pages/BranchesPageView.axaml.cs` — `BranchDragGesture.EffectFor`, the drag-over handler
  that asks it, and `IsOverTheList`

**Modified — tests**

- `tests/Enigma.GitClient.App.UnitTests/BranchesPageTests.cs` — the effect for each of the four
  cases, and a test that the pairs the policy refuses are still refused while the cursor says the
  drag is running

**Modified — docs**

- `docs/roadmap.md`, `docs/plan/BUG-876A.md`

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| What the cursor answers | Whether the gesture is under way here | It is a property of the drag, not of the pair under the pointer; the ring already answers the other question |
| How far "here" reaches | The branches list | Over the toolbar or the empty state there is nothing to drop on, and saying so is honest |
| What still refuses | The ring, and the drop | Unchanged, and tested as unchanged: only the cursor moved |
| Where the decision lives | A function beside the threshold | Same reason: headless Avalonia cannot run a drag session, but it can call this |

## Deviations & follow-ups

- **None from the plan.** All three acceptance criteria are covered.
- A reader who drags a branch onto its own row and lets go gets nothing at all — no menu, no
  message. That is the existing behaviour for every refused pair and is deliberate: the ring never
  appeared, so nothing was promised.
- **Line endings (recommendation only):** no CRLF churn in the touched files. No action taken.

## Build/test evidence

```
dotnet build Enigma.GitClient.slnx
  0 Warning(s)
  0 Error(s)

dotnet test --solution Enigma.GitClient.slnx
  Test run summary: Passed!
  total: 1766  failed: 0  succeeded: 1766  skipped: 0
```

Five tests added (1761 → 1766). No fix cycle: green on the first run.

## Documentation sweep

`README.md` and `RELEASENOTES.md` describe the drop menu and what it offers, not what the pointer
draws on the way, so the diff made nothing in them wrong. No edits. There is no `CLAUDE.md`,
`CHANGELOG.md` or `CONTRIBUTING.md` in the repository.
