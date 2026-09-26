# FEATURE-70C1-PHASE03 — Show and hide from the branches

**Item:** FEATURE-70C1 — Hide branches from the history
**Branch:** `feature/feature-70c1-phase03-branch-visibility`
**Run:** feature/2026-09-26-release-1-0-0

## Summary

Every row of the branches dialog — local and remote-tracking alike — now has an **eye** first among its
actions, and a matching *Hide from the history* / *Show in the history* item in its menu:

- clicking it hides the branch from the History page, or shows it again, and the choice is remembered
  for the repository (PHASE02's store);
- a hidden branch stays in the list, with its name and icon set aside (dimmed) and the eye struck
  through;
- the checked-out branch's eye is disabled, and its tooltip says why: the history walks HEAD whatever it
  is told;
- the row is updated where it stands rather than the list being rebuilt, so the list does not jump back
  to the top under the pointer.

The History page:

- reads each page with the hidden branches that still exist, less the checked-out one, as
  `ExcludedRefs` (PHASE01) — git leaves out the commits only those branches reach, and a commit another
  branch still reaches stays;
- leaves the badges of those branches off their rows, so hiding a merged branch still has a visible
  effect;
- redraws itself, keeping the selected line, the moment a branch is hidden or shown — waiting, as the
  automatic refresh does, while the diffs have the page;
- shows a chip on its toolbar while branches are hidden — "1 branch hidden", "3 branches hidden" — whose
  clear button shows them all again.

## Files / modules touched

**Modified — App**

- `ViewModels/Pages/BranchesPageViewModel.cs` — `BranchRowViewModel.IsHiddenInHistory`,
  `CanChangeVisibility`, `VisibilityTip`, `VisibilityHeader`, `ToggleVisibilityCommand`,
  `NotifyVisibilityChanged`; the page's `ToggleVisibilityCommand`, `IsHiddenInHistory(row)` and the
  in-place notification on `IHiddenBranches.Changed`
- `Views/Pages/BranchesPageView.axaml` — the eye button, the menu item, the `hiddenbranch` style
- `ViewModels/Pages/HistoryPageViewModel.cs` — `ExcludedRefs()` into each page's query, `WithoutHidden`
  for the badges, `HiddenBranchCount`, `HasHiddenBranches`, `HiddenBranchesSummary`,
  `ShowHiddenBranchesCommand`, the redraw on `Changed`; the unused `QueueReload` (orphaned by
  FEATURE-92A3) removed
- `Views/Pages/HistoryPageView.axaml` — the *hidden branches* chip, the toolbar grid renumbered
- `README.md`, `RELEASENOTES.md` — the feature (planned step 6)

**Tests**

- `App.UnitTests/BranchVisibilityTests.cs` (new, 10) — a row hides and shows its branch in place and
  says so; the checked-out branch cannot be hidden and says why; the realised list has an eye per row,
  disabled on the checked-out branch, and sets a hidden row aside; a hidden branch leaves the graph
  with its badge and comes back; a hidden merged branch keeps its commits and loses its badge; the
  checked-out branch is drawn whatever the store says; the toolbar counts and shows them all again; the
  selected line survives the redraw; the redraw waits while the diffs have the page; the chip is on the
  toolbar only while branches are hidden

## Deviations & follow-ups

- **Rows are notified, not rebuilt.** The plan said "the page rebuilds its rows when `Changed` fires";
  a rebuild clears the list, which scrolls it back to the top — under the pointer of someone hiding a
  branch far down it. The rows ask the page for their state and are told to re-read it instead.
- **Known limit, documented rather than worked around:** a tag on a hidden branch's own commits keeps
  them in the graph, because `--all` walks tags too. Hiding tags is out of scope for this item.
- The branches-page snapshot (`snapshots/branches-page.png`, from the existing render test) was looked
  at: an eye on every row, the checked-out branch's dimmer.

## Build/test evidence

- `dotnet build Enigma.GitClient.slnx` — 0 warnings, 0 errors.
- `dotnet test --solution Enigma.GitClient.slnx` — 2205 passed, 0 failed (10 new).

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| The icons | Phosphor `Eye` while shown, `EyeSlash` while hidden | The state the history is in now, the convention of layer and visibility toggles |
| What "set aside" looks like | The name column and the branch icon at 55 % opacity | The row stays readable and actionable; the eye is not the only signal |
| The count's source | The hidden branches that still exist, less the checked-out one | The chip counts what the graph actually leaves out, not stale entries |
| Where the chip sits | Right after the merge-source chip, same pill style | Both are choices that outlive the dialog they were made in, and read the same way |
