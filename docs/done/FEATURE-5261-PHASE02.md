# FEATURE-5261-PHASE02 — The working tree in the panel

**Item:** FEATURE-5261 — History details panel
**Branch:** `feature/feature-5261-phase02-working-tree-panel`
**Run:** vibe/2026-09-30-history-details-panel

## Summary

Selecting the history's uncommitted line now opens the working tree in the details panel. The panel
holds *Not staged* (with Discard all, Stash all and Stage all), *Staged* (with Unstage all) and the
commit box (message, subject counter, long-lines warning, Commit, Ctrl+Enter). The Changes page is
gone.

- A file picked in either half opens its diff over the graph: `DiffTarget.WorkingTree()` for what is
  not staged, `DiffTarget.Staged()` for what is. It uses the history's own diff viewer, as a commit's
  files do. Back and Escape close the diff and let go of the file.
- A file that is staged or unstaged while it is picked stays picked, in the half it moved to. Its
  diff follows it.
- After every operation in the panel (stage, unstage, discard, stash, commit), the history refreshes
  in place:
  - a commit appears at once, and the uncommitted line stays selected while there is still work;
  - a tree left clean takes the line away, and the panel with it;
  - a diff whose file left both lists (committed, discarded, stashed) is put away first.
- The working tree reads `git status` only while the history shows it (the uncommitted line is
  selected), and again on each repository refresh while it does. A new repository clears the message.
- "Show what it changed" on the uncommitted line selects it and opens the diff on the first file:
  the first that is not staged, otherwise the first staged one.
- The first commit of a new (unborn) repository is made from the panel. The empty history now says
  so, instead of naming the Changes page.
- The Changes page is removed: its rail item (the rail is History alone, plus the footer),
  `ShellPage.Changes`, its DI registrations, and `MainWindowViewModel`'s wiring. The page's own
  extras went with it: the back button and Escape to the history, the branch/upstream header (the
  window strip shows both), and the stash list (the graph draws every stash as a line with apply,
  pop and delete).

## Files / modules touched

**Created**

- `src/Enigma.GitClient.App/ViewModels/Panels/WorkingTreePanelViewModel.cs`: the working tree, taken
  from `ChangesPageViewModel`. New: `WorkingTreeChange`, `WorkingTreeChangedEventArgs`, `IsActive`,
  `SelectedChange`, `SelectionChanged`, `Changed`, `ClearSelection`, `SelectFirstFile`.
- `src/Enigma.GitClient.App/Views/Panels/WorkingTreePanelView.axaml`: the Changes page's left column,
  without the stash list.
- `tests/Enigma.GitClient.App.UnitTests/WorkingTreePanelTests.cs`.
- `docs/done/FEATURE-5261-PHASE02.md`.

**Moved**

- `Views/Pages/ChangesPageView.axaml.cs` → `Views/Panels/WorkingTreePanelView.axaml.cs`: Ctrl+Enter
  only.

**Deleted**

- `src/Enigma.GitClient.App/ViewModels/Pages/ChangesPageViewModel.cs`,
  `src/Enigma.GitClient.App/Views/Pages/ChangesPageView.axaml`.
- `tests/Enigma.GitClient.App.UnitTests/ChangesPageTests.cs` (moved to `WorkingTreePanelTests`) and
  `StashPanelTests.cs` (the page's stash list).

**Modified**

- `ViewModels/Pages/HistoryPageViewModel.cs`:
  - takes the `WorkingTreePanelViewModel` and exposes it as `WorkingTree`, with `IsWorkingTreeShown`;
  - shows and activates the working tree for the uncommitted line (`ShowWorkingTreeAsync`), and
    follows its selection (`OnWorkingTreeSelectionChanged`) and its operations (`OnWorkingTreeChanged`);
  - loads no files of its own for that line;
  - selecting the first file and closing the diff apply to whichever list the panel shows;
  - the new empty-history message.
- `Views/Pages/HistoryPageView.axaml`: the panel shows `ChangedFilesPanelView` or
  `WorkingTreePanelView`.
- `Navigation/ShellNavigation.cs`, `DependencyInjection/ServiceCollectionExtensions.cs`,
  `ViewModels/MainWindowViewModel.cs`: the page is gone, and the panel is registered as a singleton.
- User-facing text:
  - `Services/StashOperations.cs`: the conflict message says to resolve them from the history's
    uncommitted line;
  - `Views/Pages/SettingsPageView.axaml`: the file-list description.
- Comments that described the page: `ChangedFilesPanelViewModel`, `AppSettings.FilesView`,
  `WorkingTreeStatus.NotStaged`, `PorcelainV2ParserTests`.
- Tests:
  - `HistoryPageTests.ThePanel_GoesWhenItsLineDoes` waits on the working tree;
  - `ChangedFilesPanelTests.HistoryPage_ShowsTheUncommittedChangesLikeAnyOtherRow` becomes
    `…AsTheWorkingTree`: the uncommitted line's files are in the working tree, and none are in the
    list a commit's files go in;
  - `MenuIconTests` and `WholeLineMenuTests` use `WorkingTreePanelView`;
  - `ShellRenderTests`, `CompositionRootTests` (plus `TheWorkingTreePanel_IsTheOneTheHistoryShows`)
    and `MainWindowShellTests` (the rail is `["History"]`).
- `README.md`: the working-directory feature line.
- `docs/roadmap.md`, `docs/plan/FEATURE-5261.md`: statuses.

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| How the panel hands a file to the history | `SelectedChange` (target and file) and a `SelectionChanged` event; the history's `Diff` shows it | One diff viewer for the page, whichever list the file came from. The panel keeps no viewer of its own |
| A refresh replacing both halves' files | One `SelectionChanged` after the refresh, not one per list | Each list drops its selection and takes it back while it is rebuilt. Reporting that blanked the diff at every refresh |
| A picked file that is staged or unstaged | Followed into the other half | Otherwise staging the file on screen emptied the selection, and the diff over the graph closed under the reader |
| When the diff is put away after an operation | When its file left both lists | The diff then describes nothing, and the graph under it shows the result |
| `git status` while the panel is hidden | Not run (`IsActive`) | The panel is on screen only for the uncommitted line. The page used to re-read on every refresh once visited |
| Ctrl+Enter | Handled on the tunnel of the message box | A `TextBox` that accepts newlines takes Ctrl+Enter for a newline, and the bubbling `KeyDown` handler the page used never saw it. The new test found this |
| The empty history's message | "Change a file, then commit it from the uncommitted line." | The uncommitted line is where the first commit is made now |
| The constructor parameter's name | `workingTreePanel` | `workingTree` is already the dirty-tree probe's |

## Deviations & follow-ups

- **Deviation, a latent bug fixed:** Ctrl+Enter never committed on the Changes page either. The
  bubbling handler didn't run, and nothing tested it. The plan's step 2 says Ctrl+Enter still commits,
  so it is fixed here, with `CtrlEnter_CommitsFromTheMessageBox`.
- **Deviation, a small addition:** the picked file follows a stage or unstage into the other half.
  Without it, the step-3 diff over the graph closed under the reader at every stage or unstage.
- The stash list of the Changes page is gone by plan. A stash's files are still one click away: select
  its line, and the panel lists them.
- The `docs/plan` and `docs/done` records of earlier items still name the Changes page. They are
  history and were left as they are.
- Line endings: no CRLF churn in the diff.

## Documentation sweep

- `README.md`, *Features*: the working-directory line now describes the panel (select the uncommitted
  line; stage, unstage, discard and commit there, Ctrl+Enter included; diffs over the graph) instead
  of a page with a back button.
- `RELEASENOTES.md` is left alone: it describes released versions, and no release was asked for.

## Build/test evidence

- `dotnet build Enigma.GitClient.slnx --no-incremental`: **0 warnings, 0 errors**.
- `dotnet test --solution Enigma.GitClient.slnx` on this Windows machine, after fix cycle 1: **2525
  tests, 2518 passed, 4 failed, 3 skipped** (the skips are the Windows file-mode tests). Same
  verification setup as PHASE01, and none of it is committed:
  - the BUG-6EAA hanging test excluded;
  - a local Windows-safe teardown in `TestServices.Dispose`;
  - git given `core.longpaths=true` and `core.autocrlf=false` through `GIT_CONFIG_*` environment
    variables. With the neutral git configuration, the 14 CRLF failures PHASE01 classified don't
    occur.
- **The 4 failures are the font-enumeration ones, environmental and pre-existing.**
  `DiffTypographyTests.Typography_ListsTheFacesInNameOrder`,
  `SettingsPageTests.AStoredFontThisMachineLacksIsStillOffered` and
  `SettingsPageTests.TheFontListLeadsWithTheApplicationsOwnStack` fail the same way on an untouched
  copy of the run branch's base. `Typography_OffersOnlyMonospaceFacesToChooseFrom` is the
  order-dependent one (explained in `FEATURE-5261-PHASE01.md`). All four come from an installed font
  whose name Avalonia's `Typeface` parser rejects, and none touches this dev's code.
- Every test this dev added or moved passes: `WorkingTreePanelTests` (34), `HistoryPageTests`,
  `ChangedFilesPanelTests`, `MenuIconTests`, `WholeLineMenuTests`, `ShellRenderTests`,
  `CompositionRootTests`, `MainWindowShellTests`.
- Fix budget: **1 cycle of 3.** The first full run failed
  `ChangedFilesPanelTests.HistoryPage_ShowsTheUncommittedChangesLikeAnyOtherRow`, which pinned the
  old behaviour (the uncommitted line's files in the commit list). It now asserts the working tree
  instead, and the second full run is green apart from the four font tests. Before the first full
  run, while implementing:
  - Ctrl+Enter, whose new test found the swallowed key;
  - a test that waited for the rows to be emptied rather than for the reload to finish.
