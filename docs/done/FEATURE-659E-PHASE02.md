# FEATURE-659E-PHASE02 — Revert in the line menu

**Item:** FEATURE-659E — Revert a commit from the history
**Branch:** `feature/feature-659e-phase02-revert-menu`
**Run:** feature/2026-10-05-revert-tree-basedir-release

## Summary

Every history line's menu now offers **`Revert this commit…`**, right after the two reset items and
under the same condition (HEAD on a branch). It asks `Revert <short sha>?` — the subject, the branch the
new commit lands on, that the commit stays in the history, and for a merge that what it brought in is
undone and the branch it went into kept — with **Revert** as the default button. Confirmed, it records
the revert through PHASE01's engine under the repository lock (a merge with `--mainline 1`), reloads the
history with the revert on top, and says which commit undoes it. A conflicting revert changes nothing
and the warning names the files; a revert with nothing left to undo says so; work in the way, a
detached or unborn HEAD and a multi-step operation in progress are refused with a sentence. The item is
greyed out on the uncommitted line, and stash lines keep their own three actions.

## Files / modules touched

**Created — App**

- `Services/RevertOperations.cs` — `IRevertOperations` / `RevertOperations`: the refusals (`Refuse`),
  the question, the write, the report per outcome

**Modified — App**

- `Services/ResetOperations.cs` — the operation-in-progress sentences move to a public
  `DescribeOperationInProgress`, which the revert's `Refuse` shares
- `DependencyInjection/ServiceCollectionExtensions.cs` — `IRevertOperations` registered (singleton)
- `ViewModels/Pages/CommitRowViewModel.cs` — `HistoryRowCommands.Revert`; `RevertHeader`; the menu
  lists the item after the reset items when HEAD is on a branch (`ArrowArcLeft` glyph)
- `ViewModels/Pages/HistoryPageViewModel.cs` — takes `IRevertOperations`; `Revert` runs on a row with a
  commit and reloads the history when a commit was recorded

**Created — tests**

- `Desktop.UnitTests/HistoryRevertTests.cs` — the item's place and glyph; none on a detached HEAD;
  greyed out on the uncommitted line only; the question (title, default button, branch, subject) and
  Cancel changing nothing; the confirmed revert's commit and the reloaded history; a merge's revert;
  a conflicting revert changing nothing and naming the file; refusal during a merge; `Refuse`'s
  sentences (14 tests)

**Modified — docs**

- `README.md` — a feature line for the revert (documentation sweep)

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| The operation-in-progress sentences | Shared through `ResetOperations.DescribeOperationInProgress` | One wording for the same refusal, rather than a second copy of the switch |
| The conflicting files in the warning | On one line, "The conflict is in …" / "The conflicts are in a, b, …and n more" (at most `CheckoutOperations.MaximumListedFiles`) | An info bar is a sentence, not a list; a warning stays until dismissed, so the names can be read |
| Nothing left to undo | An information bar, which closes itself | Nothing went wrong; nothing needs the reader's action |
| The glyph | `PhosphorIcon.ArrowArcLeft` | An undo arrow, unused elsewhere: the reset's `ArrowUUpLeft` and the pop's `ArrowCounterClockwise` stay distinct |
| The test repository's identity | Set in the repository (`git config user.name/email`) | The revert is a commit the application makes; the test must not depend on the machine's global identity |

## Deviations & follow-ups

- None from the plan.
- Follow-up (out of scope): resolving a revert's conflicts in the app — the conflict page drives merges
  only, so a conflicting revert is abandoned instead.
- Recommendation only: line endings were not examined; nothing in this diff showed CRLF churn.

## Documentation sweep

- `README.md`: a feature line after the reset's, describing the revert, its question, merges and the
  abandoned conflict.
- No `CLAUDE.md` or `AGENTS.md` exists; `RELEASENOTES.md` is written by the release (FEATURE-4707).

## Build/test evidence

- `dotnet build Enigma.GitClient.slnx`: 0 warnings, 0 errors.
- `dotnet test --solution Enigma.GitClient.slnx`: 2768 passed, 0 failed (14 new). The existing menu
  tests (`MenuIconTests`, `WholeLineMenuTests`, `ContextMenuWidthTests`, `HistoryResetTests`) pass
  with the new item.
- Fix budget: 0 cycles used.
