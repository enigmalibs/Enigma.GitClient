# FEATURE-6693-PHASE02 — The Submodules dialog

**Item:** FEATURE-6693 — Submodule support
**Branch:** `feature/feature-6693-phase02-submodules-dialog`
**Run:** feature/2026-11-09-feature-6693

## Summary

A *Submodules* dialog, opened from a toolbar button beside *Worktrees* that shows only when the
repository has a `.gitmodules`.

- **The list:** one line per submodule, nested ones indented under theirs (path from the parent), the
  expected commit abbreviated, the state in words (*Up to date*, *Behind*, *New commits*,
  *Elsewhere, on a1b2c3d*, *Not downloaded*, *Conflicted*) with a tooltip saying what it means and a
  warning glyph for *Behind* / *Not downloaded* / *Conflicted*, and an *uncommitted changes* badge. The
  line's tooltip is the submodule's name and its (redacted) address.
- **The menu,** a glyph on every item: *Open in a new window* (greyed, with why, when not downloaded) ·
  *Update* (greyed for a conflicted one) · *Open folder* · *Open a terminal* · *Copy path*. A
  double-click opens it. The header has *Update all*.
- **`ISubmoduleOperations`** (Desktop): the update runs as a write (`RunExclusiveAsync`, refresh after)
  behind the progress card with Cancel; skipped and failed submodules are named in one warning, with the
  reason. The other actions reuse the launcher and the system interop, with the worktree actions'
  failure wording.
- **`IRepositoryContext.HasSubmodules`:** a `.gitmodules` file check at each refresh, no git process.
- README *Features*: a submodules bullet.

## Files/modules touched

- Created:
  - `src/Enigma.GitClient.Desktop/Services/SubmoduleOperations.cs`
  - `src/Enigma.GitClient.Desktop/ViewModels/Pages/SubmodulesPageViewModel.cs`
  - `src/Enigma.GitClient.Desktop/Views/Pages/SubmodulesPageView.axaml`, `.axaml.cs`
  - `tests/Enigma.GitClient.Desktop.UnitTests/SubmodulesDialogTests.cs`
  - `tests/Enigma.GitClient.Desktop.UnitTests/Infrastructure/FakeSubmoduleService.cs`
  - `docs/done/FEATURE-6693-PHASE02.md`
- Modified:
  - `src/Enigma.GitClient.Desktop/Services/IRepositoryContext.cs`, `RepositoryContext.cs`,
    `ToolDialogService.cs`
  - `src/Enigma.GitClient.Desktop/ViewModels/Pages/HistoryPageViewModel.cs`,
    `Views/Pages/HistoryPageView.axaml`
  - `src/Enigma.GitClient.Desktop/DependencyInjection/ServiceCollectionExtensions.cs`
  - `tests/Enigma.GitClient.Desktop.UnitTests/HistoryCommandBarTests.cs`, `ToolDialogTests.cs`,
    `CompositionRootTests.cs`
  - `README.md`
  - `docs/roadmap.md`, `docs/plan/FEATURE-6693.md`: statuses.

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| When the list is read | On appearing, and after each refresh while the dialog is shown | Reading states starts git in every submodule; the worktrees' "read with every refresh" would cost that on every refresh of every window |
| Where the actions live | A Desktop `ISubmoduleOperations`, like `IWorktreeOperations` | PHASE03's band reuses *Update* with the same progress card and report |
| The glyphs | `Package` for the dialog and its lines, `ArrowsClockwise` for *Update* / *Update all*, the worktree menu's glyphs for the rest | One glyph per kind of action across the app |
| *Update* on an up-to-date submodule | Enabled | It still puts its nested submodules on their expected commits |
| A list git refuses | Empty state with git's (redacted) message | A broken repository should say why rather than look as if it had no submodules |

## Deviations & follow-ups

- Three existing tests counted every toolbar button and expected each label visible; the hidden
  *Submodules* button would have broken them. `HistoryCommandBarTests` now counts the visible buttons
  (still 13 for a repository without submodules) and asserts the new one is hidden there; its
  name/tooltip/command check includes it. `ToolDialogTests` and `CompositionRootTests` list the new page.
- Pre-existing, unchanged: the Windows-only `TokenStoreTests` failure (see the PHASE01 record).

## Build/test evidence

- **Baseline:** see `docs/done/FEATURE-6693-PHASE01.md`.
- **This dev:**
  - `dotnet build Enigma.GitClient.slnx --no-incremental`: 0 warnings, 0 errors.
  - Core unit: 1458 run, 1455 passed, 1 failed (the baseline's), 2 skipped.
  - Core integration: 431 run, 429 passed, 0 failed, 2 skipped (no Core change in this dev).
  - Desktop: **compiled, not run** (this machine's standing rule). New and changed Desktop tests to run
    on Linux before tagging: `SubmodulesDialogTests` (9), the three edited files above.
- **New tests** (`SubmodulesDialogTests`): the button hidden without `.gitmodules`, shown once one
  appears, hidden again; rows, nesting, indent, states and tooltips from a fake service; the empty and
  the refused list; the list following a refresh only while shown; menu items enabled per state with
  why; every menu item has a glyph; *Update* asks for the line's path and *Update all* for every one,
  with the success report; the warning naming skipped and failed submodules; open, folder, terminal and
  copy reaching the submodule's folder.
- **Fix budget:** 0 cycles.

## Documentation sweep

- README *Features*: the submodules bullet (this dev's plan step). Nothing else describes the toolbar's
  buttons one by one.
