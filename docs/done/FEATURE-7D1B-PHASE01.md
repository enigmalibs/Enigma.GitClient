# FEATURE-7D1B-PHASE01 — Changed files list/tree panel

**Item:** FEATURE-7D1B — Commit details & diff viewer
**Branch:** `feature/feature-7d1b-phase01-changed-files`
**Run:** feature/2026-09-15-enigma-git-client

## Summary

Selecting a row in the history now opens a details pane beneath the graph: the commit's subject,
author, full timestamp and hash, and the files it touched — as a **flat list or a tree, the user's
choice**, which is what the specification asks for by name. Each row carries git's own status letter
in a colour-coded chip, the file name, the rename source when the file moved, and a `+n −m` count
(or `binary`). A filter narrows both shapes identically, the selection survives a shape switch, and a
per-row menu copies the path or hands the file to the desktop.

Underneath it is `IDiffService`, which reads a comparison's changed files from git's own
`--name-status` and `--numstat` and will read the patches PHASE02 renders. The diff viewer's own half
of the pane is a placeholder empty state until then.

## Files / modules touched

**Created — Core**

- `Diff/DiffTarget.cs` — what to compare: a commit against a chosen parent, a range, the index, the
  work tree, or everything uncommitted
- `Diff/NameStatusParser.cs` — `--name-status -z` and `--numstat -z` into `ChangedFile`, including
  the three-record rename form
- `Diff/DiffService.cs` — `IDiffService`, `DiffOptions`, and the argument vector behind both calls

**Created — App**

- `Services/SystemInterop.cs` — `ISystemInterop`: the clipboard, and handing a path to the desktop
- `ViewModels/Panels/ChangedFilesPanelViewModel.cs` — `ChangedFileNodeViewModel`,
  `ChangedFilesViewMode`, and the panel itself
- `Views/Panels/ChangedFilesPanelView.axaml` (+ code-behind) — one row template shared by the
  `ListBox` and the `TreeView`, the segmented shape toggle, the filter box and the row menu

**Modified**

- `src/Enigma.GitClient.Core/DependencyInjection/ServiceCollectionExtensions.cs` — registers `IDiffService`
- `src/Enigma.GitClient.App/DependencyInjection/ServiceCollectionExtensions.cs` — registers `ISystemInterop`
- `src/Enigma.GitClient.App/ViewModels/Pages/HistoryPageViewModel.cs` — the selected commit's header,
  `Files`, and the background read that fills it
- `src/Enigma.GitClient.App/Views/Pages/HistoryPageView.axaml` — a splitter and a details pane under
  the graph, holding the panel and the diff viewer's placeholder
- `src/Enigma.GitClient.App/Themes/Graph.axaml` — the five diff status colours, per variant
- `src/Enigma.GitClient.App/Themes/Styles.axaml` — the status chip and the segmented toggle
- `tests/.../Infrastructure/TestServices.cs`, `Infrastructure/UiServiceDoubles.cs` — a recording
  desktop interop, so a test run never touches the clipboard or opens a file manager
- `docs/roadmap.md`, `docs/plan/FEATURE-7D1B.md`

**Created — tests**

- `tests/Enigma.GitClient.Core.UnitTests/Diff/NameStatusParserTests.cs` — the two parsers and the
  argument vector, including the pathspec pair a rename needs
- `tests/Enigma.GitClient.Core.IntegrationTests/Diff/DiffServiceTests.cs` — 18 cases against a real
  repository holding an add, an edit, a delete, a rename with edits, a binary file, a mode change,
  awkward paths, a merge and a staged/unstaged split
- `tests/Enigma.GitClient.App.UnitTests/ChangedFilesPanelTests.cs` — 32 cases covering both shapes,
  the filter, the selection, the row menu, a 10 000-file change, and three off-screen renders

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| Which git command reads a commit's files | `git show --format= -m --first-parent` for the first parent, `git diff <sha>^n <sha>` for any other | `show` diffs a root commit against the empty tree, so the first commit in a repository lists its files instead of failing on a parent that does not exist. A second parent has no such problem and reads better as a plain `diff` |
| `-z` on the machine-readable listings | Always | Without it a rename is written `dir/{old => new}`, a shorthand that cannot be taken apart reliably, and any path with a space or a quote is escaped. `--numstat` has the same trap and the same fix |
| Reading one file's patch | A `ChangedFile` overload beside the `string` one | git pairs a deletion with an addition to spot a rename, and it does that **after** the pathspec has filtered the diff — so asking for the new path alone reports a plain addition. The entry carries both sides, so both survive the filter. A test pins each behaviour |
| Where the panel's row menu gets its commands | The row passes the panel's through | A `ContextMenu` opens in its own popup tree and cannot reach the panel through a visual ancestor; a `$parent[UserControl]` binding would resolve to nothing at runtime while compiling cleanly |
| The clipboard and "open file" | An `ISystemInterop` service | Both reach outside the process. As a service a test can assert "the panel asked to open this path" without a file manager appearing on the developer's screen |
| Tree auto-expansion | Only under 500 files | An expanded row is a realised row. A commit touching thousands of files would otherwise pay for every one of them the moment it is selected; past the limit the tree opens closed and the user expands what they care about |
| The rename label | The old **name** when the directory is unchanged, the whole old path when the file moved | Found by looking at the render: at panel width the full old path pushed the file's own name into an ellipsis. The full path stays as the label's tooltip |
| The directory beside a name | List mode only | The tree already says it in the parent row; repeating it there costs the width the name needs |
| Row layout | One `Grid` with the name in the single star column | A horizontal `StackPanel` measures every child with infinite width, so `TextTrimming` never fires and a deeply indented tree row runs past the panel's edge |

## Deviations & follow-ups

- **Deviation:** the row menu ships with copy-path, copy-name, open-file and show-in-file-manager.
  "View the file at this revision" and "open the file's own history" are left out: the first needs
  the file viewer that PHASE02 builds, the second needs a path-filtered log query. Both recorded as
  follow-ups on FEATURE-7D1B.
- **Deviation:** the merge-parent switch described in the item's context is modelled in `DiffTarget`
  and tested at the service (`parentIndex`), but the panel does not yet expose a control for it. It
  belongs beside the diff viewer's own toolbar in PHASE02, where the choice is visible next to what
  it changes.
- **Deviation:** the view-mode toggle is not persisted across restarts — there is no settings store
  yet (FEATURE-5D77 owns it). The toggle keeps its state for the session.
- **Follow-up:** `ChangedFile.Staging` is filled for the working-tree targets but nothing renders it
  yet; the working-directory item (FEATURE-13FE) is what shows staged against unstaged.
- **Line endings (recommendation only):** no CRLF churn observed. No action taken.

## Build/test evidence

```
dotnet build Enigma.GitClient.slnx --no-incremental
  0 Warning(s)
  0 Error(s)

dotnet test --solution Enigma.GitClient.slnx
  Test run summary: Passed!
  total: 738  failed: 0  succeeded: 738  skipped: 0
```

86 tests are new in this dev. Two integration tests failed on their first run and both were real
findings rather than bad assertions: `git add --all` restages a file from the work tree and so undid
a mode staged straight into the index, and a single-path pathspec hides a rename from git's own
detection. Three further defects were found only by rendering the panel and looking at the PNGs —
the directory repeated on every tree row, rows running past the panel's right edge, and the rename
label crowding out the file name. The three frames are written to `snapshots/changed-files-tree.png`,
`changed-files-list.png` and `changed-files-empty.png`, and the whole page to
`snapshots/history-page-details.png`.
