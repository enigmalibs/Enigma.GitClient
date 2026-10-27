# FEATURE-9A00-PHASE02 — Squash base, menu and dialog

**Item:** FEATURE-9A00 — Squash a range of commits onto a new branch
**Branch:** `feature/feature-9a00-phase02-squash-menu-dialog`
**Run:** feature/2026-10-27-feature-9a00

## Summary

The squash is reachable from the History page, in two steps like the merge source:

- **Set as squash base** on any commit line flags it (a flag before its subject) and shows a
  `Squash base: <short sha>` chip with ✕ beside the merge source chip. The base line then offers
  **Clear the squash base**; every other line offers to replace it, to clear it, and
  **Squash N commits since <base> into a new branch…** — N counted from the loaded lines by
  `SquashRange.Measure`, the item greyed out (`Squash since <base> into a new branch…`) when the base is
  not on the line's first-parent line. The group sits right after the reset/revert items, listed greyed
  on the uncommitted line and never on a stash line.
- The item opens the **squash dialog** (`SquashDialogViewModel` / `SquashDialogView`): base, tip and
  the branches on it, `N commits (M merges)` and what came in from outside, the new branch name
  (`<branch>-squashed`, else `squash/<tip short sha>`, `-2`, `-3`… while taken), **Switch to the new
  branch** (ticked when HEAD is at the tip), the message (empty title, then the subjects as bullets,
  the caret on the title), the warnings (a merge from outside, an empty commit, no branch at the tip),
  and a line saying nothing that exists changes. Confirm is disabled, with the reason shown, until the
  name is valid and free and the title is written.
- `SquashOperations` refuses — nothing asked of git that writes — during a multi-step operation or with
  unresolved conflicts, reads the preview, shows the dialog, squashes under the repository lock and
  says that the original branch is untouched and still holds the commits. The page then forgets the
  base, reloads, and selects the squash commit (`RevealAsync`).
- The base is forgotten after a squash, by ✕ or the menu, when another repository opens, and when a
  finished reload no longer lists its commit; a refresh, a fetch or a branch switch keeps it.

## Files/modules touched

**Created — Desktop**

- `src/Enigma.GitClient.Desktop/Services/SquashOperations.cs`
- `src/Enigma.GitClient.Desktop/ViewModels/Dialogs/SquashDialogViewModel.cs`
- `src/Enigma.GitClient.Desktop/Views/Dialogs/SquashDialogView.axaml`
- `src/Enigma.GitClient.Desktop/Views/Dialogs/SquashDialogView.axaml.cs`

**Modified — Desktop**

- `src/Enigma.GitClient.Desktop/ViewModels/Pages/CommitRowViewModel.cs`
- `src/Enigma.GitClient.Desktop/ViewModels/Pages/HistoryPageViewModel.cs`
- `src/Enigma.GitClient.Desktop/Views/Pages/HistoryPageView.axaml`
- `src/Enigma.GitClient.Desktop/Themes/Styles.axaml`
- `src/Enigma.GitClient.Desktop/DependencyInjection/ServiceCollectionExtensions.cs`

**Created — tests**

- `tests/Enigma.GitClient.Desktop.UnitTests/HistorySquashTests.cs`
- `tests/Enigma.GitClient.Desktop.UnitTests/SquashDialogTests.cs`

**Modified — docs**

- `README.md` — the feature list mentions the squash

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| "A conflict in progress" without a merge (e.g. a stash pop) | Refused too, from the status (`HasConflicts`), as `CommitService` refuses a commit | The plan's draft named "a merge or conflict in progress"; the head's operation alone misses conflicts left by a stash |
| The base's flag | An icon in the subject's cell, the subject padded clear of it by a `squashbase` class | Keeps the subject the cell's own `TextBlock` in column 2, which `HistoryPageTests.SubjectCell` finds by column |
| What the base keeps | Sha, short sha and subject | The dialog quotes the base's subject, and the base line may no longer be loaded when the squash runs |
| Where the menu's count comes from | `MeasureSquash` over every loaded line with a commit, on each menu build and can-execute | O(loaded lines); a cache would have to follow every reload for nothing measurable |
| The tip's branches | Local branches on the line, the checked-out one first | The default name and the "untouched" sentence name the branch the reader is most likely squashing |
| Dialog form helper | A private `ShowFormAsync` in `SquashOperations` | Each operations class keeps its own (`BranchOperations`, `RepositoriesPageViewModel`); no shared helper to reuse |
| Focus on open | The message box, caret at 0, posted on `Loaded` | The title is the one field the dialog cannot propose; the tag dialog's precedent for focusing |
| Glyphs | `FlagBanner` (base), `ArrowsInLineVertical` (squash), `X` (clear) | Present in Enigma.Icons.Phosphor 1.0.0; `MenuIconTests` requires a glyph on every item |

## Deviations & follow-ups

- The plan's `SquashBase` record carries the subject as well (see above).
- Not verified by running here: the Desktop tests are compiled, not run, on this Windows machine
  (house rule). Run `dotnet test --project tests/Enigma.GitClient.Desktop.UnitTests` on Linux before
  tagging, including the existing menu tests (`HistoryPageTests`, `MenuIconTests`, `HistoryResetTests`,
  `HistoryRevertTests`) that the new group touches.
- The squash dialog is the only confirmation; there is no second question, as planned.
- Pre-existing, unrelated: `TokenStoreTests.AfterTheResetOfAnUnreadableStore_SecretsAreStoredAndReadAgain`
  fails on Windows at the starting commit too (see `FEATURE-9A00-PHASE01.md`).
- Recommendation only: line endings were not examined beyond this diff; nothing in it showed CRLF churn.

## Documentation sweep

- `README.md` — a feature bullet after the revert's: the two steps, the new branch, and that nothing
  is rewritten or moved. `RELEASENOTES.md` is written by the release items, not per dev.

## Build/test evidence

- `dotnet build Enigma.GitClient.slnx`: 0 warnings, 0 errors (the Avalonia compiled bindings of the new
  XAML included).
- `Core.UnitTests`: 1372 total, 1369 passed, 1 failed (the pre-existing token-store test), 2 skipped —
  unchanged from PHASE01; this phase touches no Core code.
- `Core.IntegrationTests`: 410 total, 408 passed, 0 failed, 2 skipped.
- `Desktop.UnitTests`: compiled, **not run** on this Windows machine (house rule). New:
  `HistorySquashTests` (11 tests: the menu in each state and its place, the flag and the chip, the
  base's lifetime, a cancelled squash, a confirmed one — branch, files, HEAD, nothing moved, the
  revealed commit, the report — and a refusal during a merge) and `SquashDialogTests` (16 methods,
  20 cases: the name, the switch, the message, validation, the range line, each warning).
- Fix budget: 0 cycles used.
