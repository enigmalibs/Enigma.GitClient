# FEATURE-5261-PHASE01 — A details panel for commits

**Item:** FEATURE-5261 — History details panel
**Branch:** `feature/feature-5261-phase01-commit-panel`
**Run:** vibe/2026-09-30-history-details-panel

## Summary

The history now has a details panel on its right, as GitKraken does:

- **The lines are its toggles.** A click on a line selects it and opens the panel. A plain click on
  the selected line (press and release, no drag, no modifier) unselects it and closes the panel. A
  click on another line moves the panel onto that line.
- **Double-click is gone.** A double-click never leaves the panel closed, because the second press of
  a double-click is never a toggle. A right-click and a badge drag don't toggle either.
- **What the panel holds.** A one-line header (the line's subject, the commit-details button for
  commits, and a close button that unselects the line), then the same changed-files list the diff
  view used to have on its left.
- **Diffs open from the panel.** A file picked in the panel opens its diff over the graph, and the
  panel stays beside it. The back button and Escape close the diff and keep the line and the panel.
  Closing lets go of the file, so the same file can be picked again. "Show what it changed" in the
  line's menu still selects the line and opens its first file.
- **The Message column gives up the width.** The panel is docked, so the list's viewport shrinks and
  only the message column (the `*` column of `HistoryColumnLayout`) gets narrower. Graph, Refs,
  Author, Date and Commit keep their widths.
- **Resizable.** A 4-wide grip on the panel's left edge resizes it from 280 to 720 (default 380). The
  width is not persisted.
- **Stable through refreshes.** An in-place reload (the automatic refresh, hidden branches) keeps the
  panel open on the same line and doesn't empty its list. The panel closes when its line disappears.
- **Focus.** A diff opened from the panel leaves the focus in the panel, so the arrow keys move from
  file to file. Opened from the menu, the diff takes the focus as before.

For this phase, the uncommitted line's panel shows its files read-only (`DiffTarget.Uncommitted()`),
as the diff view did. PHASE02 puts the working tree there.

## Files / modules touched

**Created**

- `docs/done/FEATURE-5261-PHASE01.md`

**Modified**

- `src/Enigma.GitClient.App/ViewModels/Pages/HistoryPageViewModel.cs`:
  - new: `DetailsRow`, `IsDetailsPanelOpen`, `HasDetailsCommit`, `DetailsPanelWidth` (with its
    default, minimum and maximum), `ResizeDetailsPanel`, `ToggleSelection`, `CloseDetailsPanelCommand`;
  - `_keepingPlace` holds the panel through `ReloadKeepingPlaceAsync`;
  - a file selected in the panel opens the diff (`OnFileSelectionChanged`), and closing the diff
    clears the file selection. `_diffTarget` is set with `_filesRow`, once the files have arrived;
  - removed: the `Activate` command, `OnActivate` and the `WorkingDirectoryRequested` event.
- `src/Enigma.GitClient.App/ViewModels/Pages/CommitRowViewModel.cs`: `HistoryRowCommands` loses
  `Activate`; the `ShowChanges` documentation is updated.
- `src/Enigma.GitClient.App/ViewModels/MainWindowViewModel.cs`: no longer answers
  `WorkingDirectoryRequested`. The Changes page's own way back is kept until PHASE02.
- `src/Enigma.GitClient.App/Views/Pages/HistoryPageView.axaml`:
  - the root becomes a `DockPanel` with `DetailsPanel` docked right (grip, header, file list) and
    `HistoryArea` (the former content: toolbar, header, list, and the diff overlay);
  - the overlay keeps its header and the `DiffViewerView`, without a file list of its own;
  - `DoubleTapped` is removed. The history area's content is indented one level deeper (review with
    `git diff -w`).
- `src/Enigma.GitClient.App/Views/Pages/HistoryPageView.axaml.cs`:
  - the grip's `DragDelta`;
  - the toggle gesture (`_toggle`, `LetGoOf`, `RowUnder`, `RowAt`), posted after the list has handled
    the release;
  - `MoveFocus` leaves the focus in the panel;
  - `OnCommitDoubleTapped` is removed.
- `src/Enigma.GitClient.App/Themes/Styles.axaml`: `Thumb.panelgrip` and its hover.
- Tests:
  - `HistoryPageTests`:
    - rewritten: `DiffView_OpensFromAFileOfThePanel` (was the double-click test),
      `DiffView_CoversTheGraphAndLeavesThePanelBesideIt` (was "takes the whole page"), and
      `DiffView_ShowsWhatTheSelectedCommitChanged`, `Escape_ClosesTheDiffViewFromInsideItsOwnPanes`
      and `DiffView_LetsEachPaneScrollItself`, which now find the file list in the panel;
    - new: `ALine_IsTheDetailsPanelsToggle`, `ADoubleClick_NeverLeavesThePanelClosed`,
      `ARightClickOnTheSelectedLine_LeavesItSelected`, `ThePanelsCloseButton_LetsGoOfTheLine`,
      `ThePanel_TakesItsWidthFromTheMessageColumnAlone`, `ThePanelsGrip_ResizesItWithinItsBounds`,
      `ThePanel_StaysOnItsLineThroughAnInPlaceRefresh`, `ThePanel_GoesWhenItsLineDoes`,
      `DiffView_OpenedFromThePanel_LeavesTheFocusInIt`;
    - `Activate` calls replaced by `ShowChanges`;
  - `ChangedFilesPanelTests`: the details rendering selects the line instead of opening the diff;
  - `TagsAndCheckoutTests`: `History_SelectingARowOrShowingItsChangesChecksNothingOut`;
  - `WholeLineMenuTests`: `TheHistoryPanelsFileLines_OpenTheirMenusAnywhereOnThem` finds the file
    lines in the panel;
  - `ChangesPageTests`: `History_UncommittedRowTakesYouToTheWorkingDirectory` is removed with the
    behaviour.
- `README.md`: the two feature lines the panel made wrong (see *Documentation sweep*).
- `docs/roadmap.md`, `docs/plan/FEATURE-5261.md`: statuses.

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| How the panel survives an in-place reload | `DetailsRow`, set from `SelectedRow` except while `_keepingPlace` drops it | The list pushes a null selection while its rows are replaced. Following it closed and emptied the panel at every automatic refresh |
| When the toggle lets go | On the release, posted, and only when the press was a plain `ClickCount == 1` left press on the selected line and the release lands on the same line with no drag | Letting go on the press would close the panel under a badge drag. Posting keeps the list from handing the selection straight back |
| How the panel is resized | A `Thumb` driving `DetailsPanelWidth` in the ViewModel | A `GridSplitter` turns an `Auto` column into a fixed one, which then stays reserved after the panel closes |
| Which row's files may open a diff | Only those of `_filesRow`, the row whose files have arrived | Until they arrive, the panel still lists the previous line's files, and a click on one used to be shown against the new commit |
| The panel's header | Subject, details (commits only), close | Keyboard- and screen-reader-reachable way out, and the details one click away |

## Deviations & follow-ups

- None from the plan's steps.
- Follow-up: on a window too narrow for every column plus the panel, the message stops at its
  120 minimum and the right-hand columns clip. The panel can be made narrower.
- Follow-up: the suite does not pass on a stock Windows machine, for reasons outside this dev. See
  *Build/test evidence*. BUG-6EAA (abandoned) covered the first two, and the git configuration and
  the font enumeration are new findings.
- Line endings: no CRLF churn in the diff.

## Documentation sweep

- `README.md`, *Features*: the changed-files line now describes the panel (click a line to open it,
  click again to close it, a file opens its diff over the graph), and the commit-details line names
  the panel's header as a way in.

## Build/test evidence

- `dotnet build Enigma.GitClient.slnx --no-incremental`: **0 warnings, 0 errors**.
- `dotnet test --solution Enigma.GitClient.slnx` on this Windows machine: **2535 tests, 2514 passed,
  18 failed, 3 skipped** (the skips are the Windows file-mode tests). It was run with:
  - the known hanging test excluded (`--filter-not-method *AReaderRacingTheWriter_AlwaysReadsAWholeDocument`,
    BUG-6EAA: it spins forever on Windows);
  - a **local, uncommitted** Windows-safe teardown in `TestServices.Dispose` (clear git's read-only
    objects, then delete; BUG-6EAA). Without it, 400 App tests fail in their teardown on the base
    `develop` too. The committed harness is unchanged;
  - `core.longpaths=true` given to git through `GIT_CONFIG_*` environment variables, for the
    ref-column tests' 230-character branch names.
- **The 18 failures are all environmental and pre-existing.** Rerun on an untouched copy of the run
  branch's base (`git archive bcc61a3`), 17 fail the same way there. The 18th is order-dependent
  (below).
  - 14 compare file contents after a checkout, a discard, a reset, a merge abort or a stash apply,
    and get CRLF because this machine's global `core.autocrlf` is `true`. All 16 non-typography
    failures pass on this branch with `core.autocrlf=false` in the environment.
  - 4 enumerate the system fonts and hit an installed font whose name Avalonia's `Typeface` parser
    rejects (`FormatException`). The two `DiffTypographyTests` fail on the base too. Which of them
    trips first depends on xUnit's order, which differs between the two assemblies' paths. Alone,
    `Typography_OffersOnlyMonospaceFacesToChooseFrom` passes here. The two `SettingsPageTests`
    passed in the rerun.
- Every test this dev added or changed passes: `HistoryPageTests` (99), `ChangedFilesPanelTests`,
  `TagsAndCheckoutTests`, `WholeLineMenuTests`, `ChangesPageTests`.
- Fix budget: no fix cycle after the first full run. Before it, while implementing, four
  of the new or moved tests were corrected:
  - the filter and file-list lookups now search the panel;
  - the vanishing-line test uses a tracked file and `git checkout --`, because untracked-only work
    lists no files in `DiffTarget.Uncommitted()`;
  - the focus test clicks the file line;
  - the namespace is qualified with `global::Avalonia.Automation`.
