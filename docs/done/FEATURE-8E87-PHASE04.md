# FEATURE-8E87-PHASE04 — Bulk delete for branches

**Item:** FEATURE-8E87 — Compact ref rows with multi-select
**Phase:** PHASE04 — Bulk delete for branches
**Branch:** `feature/feature-8e87-phase04-bulk-delete-branches`
**Run:** feature/2026-10-08-title-watcher-refs-release

## Summary

Several branches, local and remote, can be selected in the tree and deleted after one question.

- **Selection:**
  - The tree takes `SelectionMode="Multiple"`, its `SelectedItems` bound to the page's `SelectedNodes`.
  - The page acts only on `SelectedBranches`: the selected branch lines, in tree order. A top-level
    node or a folder is never counted or deleted.
  - Ctrl+click and Shift+click are the tree's own.
  - Ctrl+A selects the branches the open nodes show and nothing a closed folder hides. The view
    handles it on the way down, before the tree's own, which would take the folders and every line
    of a closed folder too.
- **Folders never stay in the selection.** The arrows, a Shift range, or a right-click can put one
  there.
  - Once the tree is done, the view takes it out again.
  - It keeps the branches the gesture left selected; when only a folder is left, it restores the
    branches selected before.
  - The focus stays where the arrow left it.
- **The menu by selection.** With several branches selected, a line's menu shows only *Delete…*,
  acting on the whole selection. With one, every item is back.
- **The header:** a red *Delete (n)* button.
  - It is disabled until a branch other than the checked-out one is selected.
  - The Delete key on the tree does the same.
- **`IBranchOperations.DeleteAsync(IReadOnlyList<BranchToDelete>)` asks one question:**
  - each unmerged local branch with what it would lose — the first ten commit subjects, then
    "…and n more";
  - the checked-out branch marked *skipped: it is checked out*, and never attempted;
  - when a remote branch is included, *Deleting a branch from a remote changes the remote for
    everyone who uses it.*
- **Running the batch:**
  - one exclusive section (`DeletionBatch`), with `-d` per local branch, `-D` only for one the
    question named as unmerged, and `push --delete` per remote branch;
  - a failure is recorded and the batch goes on;
  - one refresh at the end;
  - one summary, for example *Deleted 4 of 5 branches — Not deleted: main — it is checked out*.
- **A single delete is a batch of one,** in its own words: *Delete the branch "x"?*, the unmerged
  warning, or the remote question. Success is silent, as before. The page's line and the history's
  badge menu both go through it.
- **`IBranchService.CountUnmergedCommitsAsync`** (`git rev-list --count`) gives the true number
  behind "…and n more".
- **The selection survives a rebuild as a whole,** by key. The line it started from stays first.

## Files / modules touched

**Created**

- `docs/done/FEATURE-8E87-PHASE04.md`

**Modified**

- `src/Enigma.GitClient.Core/Branches/BranchService.cs`: `CountUnmergedCommitsAsync`.
- `src/Enigma.GitClient.Desktop/Services/BranchOperations.cs`:
  - `BranchToDelete` and the batch `DeleteAsync`;
  - the single delete on top of it;
  - the old single path removed (the private remote delete and the plain confirmation).
- `src/Enigma.GitClient.Desktop/ViewModels/Pages/BranchesPageViewModel.cs`:
  - `SelectedNodes`, `SelectedBranches`, `Selection`, `DeleteSelectionLabel`, `DeleteSelectionCommand`;
  - `SelectBranches`, `SelectVisibleBranches`;
  - a line's delete by selection;
  - the whole selection restored across rebuilds;
  - `BranchRowViewModel.Selection`.
- `src/Enigma.GitClient.Desktop/Views/Pages/BranchesPageView.axaml`: multiple selection, the Delete
  key, the header button, the menu by selection.
- `src/Enigma.GitClient.Desktop/Views/Pages/BranchesPageView.axaml.cs`: folders taken out of the
  selection, and Ctrl+A.
- `tests/Enigma.GitClient.Desktop.UnitTests/BranchesPageTests.cs`: 12 tests.
  - A single delete: its own words, in red, and silent.
  - Several deleted after one question, checked exactly: unmerged commits and "…and 2 more", the
    checked-out branch skipped, the remote warning. One refresh and one summary.
  - A partial failure: a branch deleted in a terminal meanwhile.
  - The button's label and state.
  - A selection holding a folder deletes only its branch.
  - A line's menu acting on the whole selection.
  - The selection surviving a rebuild.
  - The menu with several selected.
  - The Delete key, Ctrl+A, the arrows reaching a folder, and a Shift range across a top-level node.
- `tests/Enigma.GitClient.Core.IntegrationTests/Branches/BranchServiceTests.cs`: the count against
  the list's limit, a merged branch, and a branch that is not there.
- `docs/roadmap.md`, `docs/plan/FEATURE-8E87.md`: PHASE04 and FEATURE-8E87 `DONE`. PHASE03's
  status now points to its done doc, as the first two phases' do.

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| What the tree's `SelectedItems` binds to | `ObservableCollection<BranchTreeNode>`, with `SelectedBranches` read from it | The tree writes whatever it selects into the bound list, folders included; a list of branch rows would throw on one |
| Ctrl+A | Handled by the view on the way down; the page selects the visible branches | The tree's own selects every item of every realized node, closed folders' lines included, and the folders |
| When the folders are taken out | After the tree's change, posted; the gesture's branches kept, else the ones before | The tree ignores changes made while it is selecting (as found in PHASE03) |
| Refilling a multi-selection from the page | Empty it, add the first branch, let the tree make it its selected item, then add the rest | Setting the tree's selected item selects that item alone |
| Counting what an unmerged branch would lose | `IBranchService.CountUnmergedCommitsAsync` | "…and n more" needs the true number; the subjects stop at ten |
| A batch that skips the checked-out branch | Its summary is a warning: *Deleted 4 of 5 branches*, naming it | The summary accounts for everything selected |
| The header button | Enabled when a selected branch other than the checked-out one is | Otherwise nothing could go |
| The checked-out branch's line, inside a selection with another branch | Its *Delete…* is enabled and acts on the selection | A right-click inside the selection keeps it, so the menu speaks for all of it (PHASE02) |
| The question's colour | Red for every branch delete, a single one included | As the tags since PHASE02: one path |
| The remote warning | One sentence below the list | Each remote branch is listed with its remote already |
| Notifications while the page refills the selection | One, at the end; the selection kept as a set | Ctrl+A over hundreds of lines would otherwise make every line's delete rescan the selection on every change |

## Deviations & follow-ups

- **A single delete counts true.** A branch with more than ten unmerged commits used to read
  "holds 10 commits"; it now gives the real count and ends its list with "…and n more". The wording
  is otherwise unchanged.
- **A single delete asks in red**, as the tags have since PHASE02.
- **The checked-out branch alone** (no menu offers it) gets a warning instead of a question.
- **Moved:** a stray doc comment in `BranchesPageViewModel`, from `ApplySort` to `Rebuild`.
- **Unchanged:** the remote branch delete asks no push guard; see the PHASE02 follow-up.
- I looked at the rendered snapshot: the header shows *Delete* disabled with nothing selected, as on
  the tags page.
- Line endings: the touched files are LF; no CRLF churn.

## Documentation sweep

Nothing to change. The README's "Branch management — create, rename, delete, set upstream,
checkout" is still true. Deleting several branches is a 6.0.0 release-note item (FEATURE-AE9F).

## Build/test evidence

- `dotnet build Enigma.GitClient.slnx -c Debug --no-incremental`: 0 warnings, 0 errors.
- `dotnet test --solution Enigma.GitClient.slnx -c Debug`: 3028 total, 3027 passed, 1 skipped,
  0 failed.
- Fix budget: 0 cycles.
