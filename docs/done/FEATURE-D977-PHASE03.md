# FEATURE-D977-PHASE03 — The History bar takes the commands

**Item:** FEATURE-D977 — Repository strip and command bar, PHASE03 — The History bar takes the commands
**Branch:** `feature/feature-d977-phase03-history-command-bar`
**Run:** feature/2026-10-23-strip-and-command-bar (quarantined there; finished by `/build`)

## Summary

The repository strip's buttons moved to the History page's bar, which is now the window's command bar.

- **Where the bar is.** It is at the top of the History page, over its whole width, above the details
  panel and above a diff: neither narrows it nor covers it. It is laid out by `CommandBarPanel`
  (PHASE02).
- **Its three parts:**
  - **start:** the History title and the merge-source and hidden-branches chips, as before;
  - **centre:** twelve named commands, each a 24 px icon over an 11 px name, in four groups:
    Fetch · Pull · Push · Stash | Folder · Terminal | Branches · Worktrees · Tags · Remotes |
    Refresh · Close;
  - **end:** the search block as it was, then the theme switch and About at a toolbar's size.
- **Pull and push counts.** They are drawn as a small accent pill on the icon's corner
  (`Border.commandcount`), so a count appearing never moves the centred row.
- **Whose commands they are.** The window's commands (fetch, pull, push, folder, terminal, refresh,
  close, theme, About) stay on `MainWindowViewModel`. The bar reaches them through the window the page
  is shown in (`$parent[Window].((shell:MainWindowViewModel)DataContext)`), and the counts fall back to
  hidden outside it. Every button keeps its name, tooltip and accessible name.
- **The repository strip** keeps the repository and HEAD, and has no buttons left.
- **Compact mode.** This was added to the scope at build time, on the user's decision. A bar too narrow
  for the named commands beside the start and the search block's minimum is `CommandBarPanel.IsCompact`
  (`:compact`). The commands then drop their names and the 56 px minimum, and keep their icons and tips.
  The decision rests on the full width the centre last had, so it cannot undo itself.
  - Named commands need a window of about 1350 px or more (1920 px at 125 %).
  - At 1280 px (1920 at 150 %) the bar is compact and nothing leaves it.
- **Styles** (`Themes/Styles.axaml`):
  - the `command` icon role (24);
  - `Button.toolbar.command` (padding 8,5, minimum width 56);
  - `TextBlock.commandlabel`;
  - `Border.commandcount`;
  - the two `:compact` rules.

## Files / modules touched

**Created**

- `tests/Enigma.GitClient.Desktop.UnitTests/HistoryCommandBarTests.cs`: 9 cases.
  - The order and the groups.
  - Each command is the window's or the history's, with a tip and a name.
  - The commands are at the bar's true centre, with search, theme and About on their right at toolbar
    size.
  - Each icon is 24, with its name under it.
  - On a narrow (1280) window the names go and nothing leaves the bar.
  - Widened again, the names come back at the centre.
  - The strip has no button.
  - The bar spans the page over the details panel and a diff.
  - The pull and push counts show only when there is something to do, without widening the button.
- `docs/done/FEATURE-D977-PHASE03.md`

**Modified**

- `src/Enigma.GitClient.Desktop/Views/Pages/HistoryPageView.axaml`:
  - the command bar, docked first in `PageRoot`;
  - the old toolbar removed from `HistoryArea`;
  - the `shell` namespace;
  - the page's and the details panel's comments.
- `src/Enigma.GitClient.Desktop/Views/MainWindow.axaml`: the strip (`RepositoryStrip`) without its
  buttons and their grid.
- `src/Enigma.GitClient.Desktop/Controls/CommandBarPanel.cs`: `IsCompact`, `:compact`, and the decision
  in `MeasureOverride`.
- `src/Enigma.GitClient.Desktop/Themes/Styles.axaml`: the styles above, and the `controls` namespace.
- `src/Enigma.GitClient.Desktop/ViewModels/MainWindowViewModel.cs`: the comments that put these commands
  on "the toolbar" now say "the command bar".
- `tests/Enigma.GitClient.Desktop.UnitTests/CommandBarPanelTests.cs`: compact, and back once wide enough.
- `tests/Enigma.GitClient.Desktop.UnitTests/ShellRenderTests.cs`:
  - the scale gains `command` = 24;
  - `NoPage_HasARefreshButtonOfItsOwn` no longer lists the History page;
  - `TheRepositoryStrip_HasTheOneRefreshButton` became `TheWindow_HasTheOneRefreshButton_OnTheHistorysCommandBar`.
- `tests/Enigma.GitClient.Desktop.UnitTests/ToolbarButtonLookTests.cs`: the strip test is now
  `TheCommandBar_FramesWhatCanBePressedAndGreysWhatCannot`.
- `tests/Enigma.GitClient.Desktop.UnitTests/RepositoryFolderTests.cs`: `TheCommandBar_CarriesBothButtons_…`.
  It finds the icon inside the button and checks the name.
- `tests/Enigma.GitClient.Desktop.UnitTests/HistoryPageTests.cs`: `DiffView_CoversTheGraphAndLeavesThePanelBesideIt`.
  The diff and the panel are now as tall as the page less the bar.
- `docs/roadmap.md`, `docs/plan/FEATURE-D977.md`: statuses. PHASE03 and the item are `DONE`. The plan's
  scope gains the compact mode (step 5 and a criterion) and its out-of-scope list loses it. The
  quarantine notes are condensed into a resolved note.

## Deviations & follow-ups

- **Scope amended (the user's decision at `/build`): the compact mode.** The plan had it out of scope.
  The rendered 1280 px frame showed About pushed off the bar and half the theme switch cut, so it is
  in, with its own step and acceptance criterion in the plan.
- **The quarantine's diagnosis was wrong, and so was its third cycle.**
  - The one red test (`CommandBarPanelTests.ABarTooNarrowForTheFullCentre_IsCompact_UntilItIsWideEnoughAgain`)
    widened a shown headless window and asserted after one `UpdateLayout`.
  - A probe of the layout after each pass showed what happens: a headless window measures at its new
    `Width` at once, but is arranged at it only once the platform's resize has been through the
    dispatcher. The panel had already decided "not compact", with the centre measured at 300; it was
    still arranged at 760.
  - The test now runs the dispatcher's jobs before asserting. The quarantine's third cycle (invalidating
    every descendant of the centre before measuring it again) made no difference to the probe, so it is
    removed: the panel measures the centre once more after the switch, as in the second cycle.
  - `HistoryCommandBarTests.WidenedAgain_…` proves the same on the real, nested buttons.
- **Merging into the run branch:**
  - The run branch's `docs/roadmap.md` carries the quarantine footnote for `FEATURE-D977-PHASE03` and
    the row at `IN PROGRESS`. This branch re-pads the whole table (no row is `IN PROGRESS` any more).
  - On the merge, keep this branch's table and delete that footnote.
- **Follow-ups:**
  - On a compact bar the search box is at its minimum, about 100 px. `CommandBarEnd`'s `MinWidth`
    (360) is the setting to change if a wider box should win over the names sooner.
  - As decided in the plan, the Profiles, Settings and Conflicts pages no longer carry the window's
    commands.

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| Finish on which branch | The quarantined branch, continued | The user's choice; it holds the whole phase |
| Keep the compact mode | Yes, fixed | The user's choice; without it a 1280 px window loses About |
| Labels hidden how | A `:compact` pseudo-class on the panel, answered by two styles | The panel knows when; the styles say what — names off, minimum width off — next to the command button's own style |
| Compact decided on what | The centre's full width when last measured named | Deciding on the compact width would switch back and forth |
| The counts | An accent pill over the icon's corner | A count appearing does not move the centred row |
| Window commands reached how | `$parent[Window].((shell:MainWindowViewModel)DataContext)`, counts with `FallbackValue=False` | The cast binding the views already use; no view-model dependency cycle |

## Build/test evidence

- `dotnet build Enigma.GitClient.slnx`: 0 warnings, 0 errors.
- `dotnet test --solution Enigma.GitClient.slnx`: 3453 total, 3450 passed, 3 skipped, 0 failed.
  - That is +10 over PHASE02's 3443: the 9 `HistoryCommandBarTests`, the compact panel case and the
    scale's `command` row, less the History case of `NoPage_HasARefreshButtonOfItsOwn`.
- Looked at in the rendered snapshots:
  - at 1280 px the bar is compact: icons only, centred, with search, theme and About on the bar;
  - with the details panel open, the bar spans the page above it.
