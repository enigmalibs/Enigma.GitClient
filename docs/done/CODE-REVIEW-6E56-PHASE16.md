# CODE-REVIEW-6E56-PHASE16 — [Medium] PERF-02 Folded conflict context

## Summary

The conflict page built every agreed line of a file as a line ViewModel and drew it in an
unvirtualised `ItemsControl`. The outer `ListBox` virtualised the regions, but one agreed region is
everything between two conflicts. A 10,000-line file with one conflict in the middle was two regions
of 5,000 lines each, built in full and realised as 5,000 controls the moment it scrolled into view.
Lock and generated files are exactly the files that conflict.

What changed:

- `ConflictRegionViewModel` folds agreed text longer than twice the context. It shows the context
  lines next to each conflict, then an "N unchanged lines" row, then the context lines before the next
  one.
  - The part of the file before the first conflict keeps only its last lines, and the part after the
    last conflict keeps only its first lines.
  - Only the lines shown are built. `ExpandCommand` builds the rest and shows them all, numbered as
    before.
  - New members: `StableTailLines`, `FoldedLineCount`, `IsFolded`, `FoldedText`, `ExpandCommand`.
    `StableLines` keeps its name: the lines above the fold, or every agreed line when nothing is
    folded.
  - The constructor takes `context` (negative means never fold, the default, so other callers are
    unchanged) and whether a conflict comes before or after the region.
- `ConflictResolutionPageViewModel` passes the diff's context-lines setting (`DiffContextLines`, 3 by
  default) and each region's neighbours. It takes `ISettingsService` as a new optional constructor
  parameter, like its other optional services.
- The view stacks the head lines, the expand row and the tail lines. The expand row is a full-width
  button in the diff viewer's hunk-band colours (`Button.conflictfold`), with a tooltip and the
  automation name "Show N unchanged lines".
- Writing the file back is unchanged. It renders the document's regions, not the line ViewModels, as
  the save and the live preview always did.

## Files/modules touched

- Created: `tests/Enigma.GitClient.Desktop.UnitTests/ConflictFoldTests.cs`
- Modified: `src/Enigma.GitClient.Desktop/ViewModels/Pages/ConflictResolutionPageViewModel.cs`
- Modified: `src/Enigma.GitClient.Desktop/Views/Pages/ConflictResolutionPageView.axaml`
- Modified: `src/Enigma.GitClient.Desktop/Themes/Styles.axaml`
- Modified: `tests/Enigma.GitClient.Desktop.UnitTests/ConflictResolutionPageTests.cs`
- Modified: `docs/roadmap.md`, `docs/plan/CODE-REVIEW-6E56.md`
- Created: `docs/done/CODE-REVIEW-6E56-PHASE16.md`

## Deviations & follow-ups

- **Only agreed text folds.** The sides of a conflict (`OurLines`, `BaseLines`, `TheirLines`) are
  still built in full. They are what the reader must decide on, and the plan folds agreed regions
  only. A conflict of thousands of lines on one side is still drawn whole (follow-up, if it shows up
  in practice).
- **A fold expands once and stays expanded** until the file is loaded again. There is no "fold again"
  row, which the plan did not ask for.
- **The context is read when the file loads.** Changing the setting while a file is on screen takes
  effect at the next load.
- The edges of the file fold with the same threshold as the middle: more than twice the context. A
  start of the file 7 lines long, with a context of 3, shows its last 3 lines and folds 4.

## Build/test evidence

- Baseline: see `docs/done/CODE-REVIEW-6E56-PHASE01.md`.
- **This dev:**
  - `dotnet build Enigma.GitClient.slnx --no-incremental`: 0 warnings, 0 errors.
  - Core unit: 1534 run, 1531 passed, 0 failed, 3 skipped.
  - Core integration: 449 run, 447 passed, 0 failed, 2 skipped (6 min 08 s).
  - Desktop: compiled, **not run** (this machine's standing rule).
- **Throwaway harness outside the repository**, on the Avalonia 12.1.4 headless platform with the
  real container (`App.ConfigureServices`, a temporary configuration directory and a silent info
  bar), the real conflict page and a real git merge. Every check passed:
  - 5,000 agreed lines between conflicts: 6 line ViewModels built, 4,994 folded, numbers kept across
    the fold;
  - expanding shows all 5,000, numbered 11 to 5010;
  - 6 lines with a context of 3 are not folded;
  - a real 81-line file with one conflicting line in the middle loads as three regions:
    - the start: no head, 37 folded, the last 3 lines;
    - the conflict;
    - the end: the first 3 lines, 37 folded, no tail;
  - taking ours and saving writes the whole file, folded lines included;
  - a context of 1 shows one line on each side.
- **New tests (Desktop, compiled, not run):**
  - `ConflictFoldTests` (7 cases):
    - a 5,000-line stretch builds only its context lines;
    - expanding shows every line, numbered as before;
    - 6 lines are not folded and 7 fold one;
    - the edges keep only the side next to a conflict;
    - without a context nothing folds;
    - a conflicted region has nothing to fold.
  - `ConflictResolutionPageTests.ALongFile_ShowsOnlyTheContextAroundItsConflict_AndIsWrittenBackWhole`.
- `Page_BuildsTheRegionsOfTheSelectedFile` still holds: its one-line agreed regions are not folded.
- **Fix budget:** 0 cycles.

## Documentation sweep

- Nothing stale. The README's "three-way view … live preview" still describes the page, and its
  "context" setting now also sets the conflict page's.

## Decisions taken at build time

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| How the context reaches the region | The page passes `DiffContextLines` from `ISettingsService`, an optional constructor parameter | The diff's own setting, as the plan says. The optional parameter matches the page's other optional services | A constant 3; a new setting of its own |
| Keeping `StableLines` | Kept, as the lines above the fold | A test and the view bind it. Unfolded, it is still every agreed line | Renaming it `StableHeadLines` (breaks the test for no gain) |
| Where the fold threshold applies | More than twice the context, for every agreed region | One rule, and short regions stay whole as the plan asks | A smaller threshold at the file's edges |
| The expand row's look | A full-width button in the diff viewer's hunk-band brushes | The plan asks for the diff viewer's style, and those are its theme resources | A link-style text button; a new colour pair |
