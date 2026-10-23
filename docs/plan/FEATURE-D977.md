# FEATURE-D977 — Repository strip and command bar

**Status:** IN PROGRESS
**Type:** FEATURE
**Branch:** one per phase, see below
**Run:** feature/2026-10-23-strip-and-command-bar

## Objective

The repository window's two top bars, reorganised:

1. **The repository strip** (the window's top bar): the repository's name and its icon bigger, and the
   branch badge on their right drawn exactly as the history draws the checked-out branch's badge — the
   same size, the same light blue, the same almost-white text.
2. **The History bar** (the bar just below it) takes the strip's buttons. In its centre, bigger buttons
   with bigger icons and a title under each icon, in this order:
   fetch · pull · push · stash | folder · terminal | branches · worktrees · tags · remotes |
   refresh everything · close the repository.
   On its right, at today's size and style: the search box with its counter and its buttons, the theme
   switch, About. The bar may be taller for it.

## Context & constraints

- **Two view models.** The strip is `MainWindow.axaml`, bound to `MainWindowViewModel`, which owns fetch,
  pull, push (`RunSyncAsync` redraws the history after each), the folder, the terminal, refresh, close,
  the theme switch and About, and the ahead/behind counters. The History bar is `HistoryPageView.axaml`,
  bound to `HistoryPageViewModel`, which owns stash, the four tool dialogs and the search. The window's
  view model already depends on the history's (`_history`), so the history's cannot depend back on it.
- **The views already bind across a scope with a cast:** `$parent[ListBox].((vm:HistoryPageViewModel)
  DataContext).…` (`HistoryPageView.axaml`), `$parent[ItemsControl].((vm:RepositoriesPageViewModel)
  DataContext).…` (`RepositoriesPageView.axaml`).
- **Where the History bar sits today:** inside `HistoryArea`, the panel the details panel (docked right,
  380 px by default, 280–720) narrows and the diff page (`DiffPage`) covers. The window opens maximised
  every time; the rail is 72 px.
- **Width budget.** Twelve labelled buttons and three separators come to about 720 px. The search block
  needs about 174 px of fixed parts (counter 72 + margins, three 30 px buttons) besides the box, and
  theme + About about 70 px. Effective widths of 1280–1536 px (1920 px at 150 % / 125 %) are common.
- **The selected branch's badge** is `RefBadge` with the `head` class (`Themes/Controls.axaml`):
  `RefBadgeHeadBrush` (#3574F0 in both themes), foreground `#FFFFFF`, 13 px text and icons, padding
  9,3, a check before the branch icon. `RefBadgeMetrics` mirrors those numbers for the Refs column.
- **The icon scale** (`Themes/Styles.axaml`): header 20, toolbar 18, row 16, pill 12, menu 16 — one
  role per use, tested by `ShellRenderTests`.
- **Tests pinned to today's placement:** `ShellRenderTests.NoPage_HasARefreshButtonOfItsOwn` (includes
  the History page), `ShellRenderTests.TheRepositoryStrip_HasTheOneRefreshButton`,
  `ToolbarButtonLookTests.TheRepositoryStrip_FramesWhatCanBePressedAndGreysWhatCannot`,
  `RepositoryFolderTests.TheRepositoryStrip_CarriesBothButtons_WithTheirIconsAndNames` (asserts the
  button's content *is* the icon).
- **Baseline** on `develop` @ `44f2aac`: build 0 warnings; 3430 tests, 3427 passed, 3 skipped.

## PHASE01 — Bigger strip and head badge

**Status:** DONE — see `docs/done/FEATURE-D977-PHASE01.md`
**Branch:** `feature/feature-d977-phase01-repository-strip`

### Steps

1. `Styles.axaml`: a new icon role **`title` = 24** — the icon beside the open repository's name in the
   window's strip — documented in the scale's comment.
2. `MainWindow.axaml`, the strip's left part:
   - the folder icon and the linked-worktree tree take the `title` role;
   - the repository's name is drawn at 18, SemiBold;
   - the HEAD pill becomes the graph's own **`RefBadge`** (`Kind="LocalBranch"`, `IsCurrent="True"`,
     `Text="{Binding HeadDisplayName}"`), shown while HEAD is on a branch;
   - a detached HEAD shows no branch badge: the existing warning pill says where HEAD is
     (`detached at abc1234`) instead of `detached HEAD`.
3. `MainWindowViewModel.IsOnBranch` (a repository is open and HEAD is not detached), notified with the
   repository and HEAD.
4. Tests:
   - the scale has `title` at 24, bigger than `header`;
   - in the window: the name is 18, the folder icon 24; the head badge is a `RefBadge` carrying `head`,
     with the same background and foreground brushes, text size and height as a badge the history draws
     for its checked-out branch;
   - detached: no badge, the warning pill reads `detached at …`; `IsOnBranch` follows HEAD.

### Acceptance criteria

- The strip's name, icon and badge are bigger; the badge is indistinguishable from the history's
  checked-out-branch badge, in both themes.
- Zero warnings; the whole suite is green.

## PHASE02 — Centring command bar panel

**Status:** DONE — see `docs/done/FEATURE-D977-PHASE02.md`
**Branch:** `feature/feature-d977-phase02-command-bar-panel`

### Steps

1. `Controls/CommandBarPanel.cs`: a `Panel` with three slots, by position — start, centre, end — and a
   `Spacing` (default 10):
   - the start hugs the left edge at its desired width; the end hugs the right edge;
   - the centre sits at the bar's **true centre** whenever both sides fit beside it;
   - otherwise it moves as little as it must, never over the start, and keeping the end at least its
     `MinWidth` while there is room for that;
   - the end is given the width left right of the centre, up to its `MaxWidth`, so a flexible search box
     is as wide as before on a wide screen and gives way first on a narrow one;
   - all children take the bar's height (their own vertical alignment applies);
   - a fourth child is not laid out (documented).
2. Tests (headless): true centre with room; the end capped at its `MaxWidth` and right-aligned; a wide
   start or a wide end moves the centre without overlap; the end shrinks to its `MinWidth` before the
   centre moves; overflow puts the centre right after the start; desired height is the tallest child.

### Acceptance criteria

- The layout rules above hold, proven by tests; the panel is not used by any view yet.
- Zero warnings; the whole suite is green.

## PHASE03 — The History bar takes the commands

**Status:** IN PROGRESS — quarantined by feature/2026-10-23-strip-and-command-bar: the compact command bar does not widen back within one layout pass (1 test red after 3 cycles)
**Branch:** `feature/feature-d977-phase03-history-command-bar`

### Quarantine

- **Failing (1 of 3452):** `CommandBarPanelTests.ABarTooNarrowForTheFullCentre_IsCompact_UntilItIsWideEnoughAgain`.
  - The bar is first laid out at 760 (compact: the label is hidden and the centre is 200), then
    widened to 900.
  - `IsCompact` turns false and the label is visible again, but after one `UpdateLayout` the centre is
    still arranged at its compact width (`Expected: 300`, `Actual: 200`).
  - Build: 0 warnings. Every other test is green (3448 passed, 3 skipped).
- **What the branch holds, complete and green apart from the above:**
  - the command bar at the top of the History page, full width;
  - the twelve labelled commands centred, with search, the theme and About on the right;
  - the strip with no buttons;
  - the `command` icon role, the `Button.toolbar.command`, `TextBlock.commandlabel` and
    `Border.commandcount` styles;
  - `HistoryCommandBarTests` (8 cases) and the pinned tests updated.
- **How it got here:**
  - **First run:** 5 red. My centre test was 1 px out, and as it failed it left a `MainWindow` open,
    which broke three click tests run after it. `DiffView_CoversTheGraph…` asserted the old design
    (the diff over the whole page).
  - **Cycle 1:** a 1 px tolerance, windows closed in `finally`, the diff test moved to the new design.
    Green, 3450.
  - The rendered 1280 px frame then showed About pushed off the bar and the search box squeezed:
    1280 px is too narrow for the labelled commands beside the search block's minimum. This was
    out of scope in this plan; the evidence made it necessary.
  - **Cycle 2:** a compact mode. `CommandBarPanel` sets `:compact` when the full centre, the start and
    the end's minimum do not fit; styles hide the labels and drop the 56 px minimum; the centre is
    measured again. The new panel test went red (the item above). `RepositoriesPageTests.OpeningA…`
    failed once, passed alone and passed in the next full run: a flake.
  - **Cycle 3:** after the toggle, every descendant of the centre is invalidated before it is measured
    again. Still red, the same value.
- **Leads for `/build FEATURE-D977`:**
  - Check whether a second layout pass converges: the test does one `UpdateLayout`.
  - Apply the pseudo-class outside the measure pass: decide in `MeasureOverride`, set it through
    `Dispatcher.UIThread.Post`, then `InvalidateMeasure`.
  - Or drive the labels' visibility from a bound `IsCompact` direct property rather than a style.
  - Then look at the 1280 px snapshot again.

### Steps

1. `Styles.axaml`:
   - a new icon role **`command` = 24** — a labelled command button's icon;
   - `Button.toolbar.command`: the toolbar look and states, padding 8,5, minimum width 56, content centred;
   - `TextBlock.commandlabel`: 11 px, centred, under the icon;
   - `Border.commandcount`: the pull/push counter, a small accent pill on the icon's top-right corner, so
     the centred cluster does not move when a count appears.
2. `HistoryPageView.axaml`:
   - the bar moves to the top of the page, **full width, above the details panel and the diff**;
   - its content is a `CommandBarPanel`:
     - start: the History title and the merge-source and hidden-branches chips, unchanged;
     - centre: the twelve labelled buttons and three separators, in the order of the objective;
     - end: the search block unchanged, then the theme switch and About as toolbar buttons;
   - the window's commands bind through `$parent[Window].((shell:MainWindowViewModel)DataContext)`, with
     `FallbackValue` where a failed binding would otherwise show something;
   - every button keeps its name, tooltip and accessible name (`OpenFolder`, `OpenTerminal`,
     `StashChanges`, `OpenBranches`, `OpenWorktrees`, `OpenTags`, `OpenRemotes`, `OpenAbout`, "Refresh
     everything", …).
3. `MainWindow.axaml`: the strip keeps only its left part; the buttons and their separators go.
4. Comments and XML docs that place these commands on "the repository strip" / "the toolbar" follow them
   (`MainWindowViewModel`, `ShellRenderTests`).
5. Tests:
   - the bar's centre carries the twelve commands in order with their labels and the separators between
     the groups;
   - the window's commands are the window view model's, the history's the history's;
   - search, theme and About are right of the centre, at toolbar size;
   - the strip has no button;
   - the bar is as wide as the page with the details panel open, and stays visible with a diff open;
   - a command's label is under its icon, and its icon is 24;
   - the four pinned tests above follow the buttons to the History bar.

### Acceptance criteria

- The History bar shows the commands in the asked order, with labels, centred, and search, theme and
  About on its right at today's size; the strip has no buttons.
- The bar is never narrowed by the details panel nor covered by a diff.
- Zero warnings; the whole suite is green.

## Out of scope

- A window-level command bar on the Profiles, Settings and Conflicts pages (see the decisions).
- Collapsing the labels on narrow windows; keyboard shortcuts for the commands; new commands.
- Release notes: they are written by the release item.

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| Where the merged bar lives | The History page's bar, binding the window's commands through the hosting window | Literal to the request; least churn; the cast binding is an idiom the views already use | A window-level bar on every page (search, stash and the dialogs would sit on pages they mean nothing on, and every History-bar test would move); a new shared command service (a dependency cycle with the history) |
| The pages without the commands | Profiles, Settings and Conflicts no longer show fetch, pull, push, folder, terminal, refresh, close, theme, About | Direct consequence of the above; History is one click away on the rail; Settings keeps About and the theme preference; the operation banner keeps the merge's actions | Keeping a second copy in the strip on those pages (two refresh buttons, contrary to "move") |
| The details panel and the diff | The bar moves above both, full width | The window's commands must never be covered or squeezed; with labels, the centre alone is ~720 px | Leaving it in `HistoryArea` (overflows at 1536 px with the details panel open; hidden under a diff) |
| Centring | A small `CommandBarPanel`: true centre when it fits, else the least shift without overlap | A Grid either drifts ~200 px off centre (`Auto,*,Auto`) or starves the search box (`*,Auto,*`) | Either Grid; a hard-coded offset |
| Big button look | Toolbar family + `command` class, icon role 24, 11 px label, min width 56 | Keeps the enabled/disabled language; one scale entry sizes them all | A new unframed button look; 22 px icons (too close to 18) |
| Pull/push counters | An accent count pill on the icon's corner | A count appearing does not move the centred cluster | A counter beside the icon or the label (widens the button) |
| Strip sizes | Name 18 SemiBold, icon role `title` 24 | A clear step above the pages' titles (15 / 20) | Reusing `header` 20 for the strip |
| "Same badge as the lines" | The graph's own `RefBadge`, `IsCurrent` | Identical by construction, follows the theme and `RefBadgeMetrics` | A copied style (drifts) |
| Detached HEAD | No blue badge; the warning pill says `detached at abc1234` | Blue means "the checked-out branch" in the graph | A blue badge with a branch icon for a detached HEAD |
| Phases | Strip; panel; bar | One reviewable commit each | One commit for everything |
