# FEATURE-13FE-PHASE02 — Changes page & commit UI

**Item:** FEATURE-13FE — Working directory & commits
**Branch:** `feature/feature-13fe-phase02-changes-page`
**Run:** feature/2026-09-15-enigma-git-client

## Summary

The working directory page is real. It shows what is not staged and what is, as the same list-or-tree
panel the history uses, with stage, unstage and discard on every row and on everything at once. The
diff pane beside them is the same viewer the history has, showing whichever half of the change is
selected. Underneath sits the commit box: a multi-line message with the 50/72 guides, amend and
sign-off, Ctrl+Enter to commit, and the resulting short hash reported back.

Discarding one file is confirmed with the files named. Discarding everything asks for the
repository's own name to be typed, because it is the one thing in this client that nothing can undo.

With this phase FEATURE-13FE is complete.

## Files / modules touched

**Created — App**

- `ViewModels/Dialogs/ConfirmTextDialogViewModel.cs` — a confirmation that has to be typed
- `Views/Dialogs/ConfirmTextDialogView.axaml` (+ code-behind)

**Rewritten**

- `ViewModels/Pages/ChangesPageViewModel.cs` — the two panels, the diff pane, the commit box and
  every command behind them
- `Views/Pages/ChangesPageView.axaml` (+ code-behind) — the layout and the Ctrl+Enter handler

**Modified**

- `ViewModels/Panels/ChangedFilesPanelViewModel.cs` — `ChangedFileRowActions`, so a host page can
  put its own verbs on a row, and a summary that stays quiet about counts nobody measured
- `Views/Panels/ChangedFilesPanelView.axaml` — the row's action button and menu items
- `ViewModels/Pages/CommitRowViewModel.cs` — the uncommitted row carries the row commands too
- `ViewModels/Pages/HistoryPageViewModel.cs` — activating the uncommitted row raises a request
- `ViewModels/MainWindowViewModel.cs` — the shell answers that request by navigating
- `Core/Files/ChangedFile.cs`, `Core/Diff/NameStatusParser.cs` — `HasLineCounts`
- `Themes/Graph.axaml`, `Themes/Styles.axaml` — the accent action, the row action and the guides
- `DependencyInjection/ServiceCollectionExtensions.cs`
- `docs/roadmap.md`, `docs/plan/FEATURE-13FE.md`

**Created — tests**

- `tests/Enigma.GitClient.App.UnitTests/ChangesPageTests.cs` — 25 cases against a real repository:
  staging, unstaging, discarding, committing, amending, the message guides, the diff pane, the
  shell hand-off and a rendered snapshot

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| The two file panels | The history's own `ChangedFilesPanelViewModel`, twice | The list/tree toggle, the filter and the row menu then behave identically everywhere, and a fix in one is a fix in both |
| How a page adds its own verbs | A `ChangedFileRowActions` record the host sets | The panel has no opinion about what should happen to a file — in the history it is something to read, here it is something to stage. The host supplies the verbs; the panel draws them |
| Selecting in both panels at once | Not allowed; picking one clears the other | The diff shown has to be unambiguous about which half of the change it is |
| The uncommitted row's destination | An event the shell answers, not a navigation service | The shell's navigation builds the page ViewModels, so a page holding a reference to it would be asking to be constructed by the thing it is constructing |
| Discard-all's guard | Type the repository's directory name | The one genuinely unrecoverable action here. A click is something a hand does by accident; typing a name is not |
| Amend with nothing staged | Allowed | An amend has something to record even so: the message. Disabling it would make "fix the wording of the last commit" impossible |
| Amend's prefill | The last message, only when the box is empty | Overwriting something already typed to help would be the least helpful help available |
| Line counts on a status entry | Not shown at all | `git status` reports what changed, not by how much. "+0 −0" claims the change is empty, which is a different thing from not having been counted — found by looking at the rendered page |
| The accent colour | Defined in the app's own theme file | Borrowing a key from the theme package would let the primary action's colour change out from under it on a package bump |

## Deviations & follow-ups

- **Deviation:** the plan's "stage the selection" is per row and per directory row rather than per
  multi-selection: the panel carries a single selection, which is what the diff pane needs. Staging a
  directory covers the case multi-select was for.
- **Deviation:** the status is re-read on demand and after every write, not when the window regains
  focus. Window activation is an Avalonia lifetime concern rather than a page one, and the plan
  already records the filesystem watcher as a follow-up; both belong together.
- **Follow-up:** hunk- and line-level staging, already recorded as out of scope by the plan.
- **Follow-up:** `IStagingService.RemoveAsync` and the ignore service have no UI yet. "Stop tracking
  this" and "ignore this" belong in the row menu, and are worth adding with the rest of the row's
  file operations rather than alone.
- **Line endings (recommendation only):** no CRLF churn observed. No action taken.

## Build/test evidence

```
dotnet build Enigma.GitClient.slnx --no-incremental
  0 Warning(s)
  0 Error(s)

dotnet test --solution Enigma.GitClient.slnx
  Test run summary: Passed!
  total: 1037  failed: 0  succeeded: 1037  skipped: 0
```

26 tests are new in this dev. Two defects were found by looking at the rendered page rather than by a
failing assertion: every row claimed "+0 −0" for a change nothing had measured, and the subject
counter sat against the sign-off checkbox instead of beside the button it belongs to. A third was
found by a test: the uncommitted pseudo-row was built without the row commands, so activating it
would have thrown in the running application. The page is written to `snapshots/changes-page.png`.
