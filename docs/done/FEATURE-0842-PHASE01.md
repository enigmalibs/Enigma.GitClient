# FEATURE-0842-PHASE01 — The checked-out line is washed

**Item:** FEATURE-0842 — History: checked-out line, tag focus
**Branch:** `feature/feature-0842-phase01-checked-out-line`
**Run:** feature/2026-09-29-rail-clone-history

## Summary

The checked-out line in the history (the one with the current branch's blue badge and the ringed
node) is now tinted in a light blue across its whole width. The tint is the HEAD badge's `#3574F0`
laid over the background:

| Theme | Line      | Hovered   |
|-------|-----------|-----------|
| Dark  | `#21293B` | `#23304B` |
| Light | `#E7EDF9` | `#E0E8F9` |

A selected HEAD line looks like any selected line. A HEAD line that the search found shows the
search's wash. Primary text keeps a contrast of 7:1 or better on the tint, which is more than the
search wash leaves it.

## Files / modules touched

**Created**

- `docs/done/FEATURE-0842-PHASE01.md`

**Modified**

- `src/Enigma.GitClient.App/Themes/Graph.axaml` — `HeadRowColor` and `HeadRowHoverColor` in both
  variants, their brushes, and why they are what they are.
- `src/Enigma.GitClient.App/Themes/Styles.axaml` — `Grid.commitrow.head`, its `:pointerover` and
  `:selected` forms (the last one uses `EnigmaSelectionBrush`), all declared before the search's
  wash.
- `src/Enigma.GitClient.App/Views/Pages/HistoryPageView.axaml` — `Classes.head="{Binding IsHead}"` on
  the row.
- `tests/Enigma.GitClient.App.UnitTests/HistoryPageTests.cs`:
  - new: `TheCheckedOutLine_IsWashed_AndNoOtherLine`;
  - new: `TheCheckedOutLine_AnswersForHoverAndSelection_AndGivesWayToTheSearch`;
  - new: `TheCheckedOutLine_IsASubtleWash_InBothThemes`, which checks the tint is visible, stays
    nearer the background than the selection even when hovered, and is blue;
  - `Search_WashesTheRowsItFound` and `Search_KeepsAFoundRowHoverableAndSelectable` now expect
    "transparent" of the lines that are not HEAD.
- `docs/roadmap.md`, `docs/plan/FEATURE-0842.md` — statuses.

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| Light-variant strength | 8%, and 12% hovered (the plan said ≈ 10% / 16%) | The light selection (`#C4D8F8`) is far paler than the dark one. At 16% the hovered line was more than halfway to it, so it read as selected |
| A selected and hovered HEAD line | The selection | The `:selected` style is declared after `:pointerover`, so selection wins, as it does for a found line |
| What "subtle" means in the test | At least 20 (RGB, summed) from the background; hovered further; the hover less than half the selection's distance from the background | Puts "subtle but visible" as three inequalities that hold in both themes, without pinning exact colours |

## Deviations & follow-ups

- The light-variant proportions differ from the plan (see above); the dark ones are as planned.
- Line endings: no CRLF churn.

## Documentation sweep

The README's graph and history lines don't describe row colours. No edit.

## Build/test evidence

- `dotnet build Enigma.GitClient.slnx --no-incremental`: 0 warnings, 0 errors.
- `dotnet test --solution Enigma.GitClient.slnx`: **2527 passed**, 0 failed, 0 skipped (3 new, 2
  adjusted).
- Fix budget: 1 cycle. The first run failed `TheCheckedOutLine_IsASubtleWash_InBothThemes` with
  "Light: the wash rivals the selection". The light colours were lowered to 8% / 12%, and the next
  run was green.
