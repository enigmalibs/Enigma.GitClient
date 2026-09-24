# FEATURE-7232 — Toolbars, one refresh, flat file lists

**Status:** IN PROGRESS
**Type:** FEATURE
**Branch:** one per phase, see below
**Run:** feature/2026-09-24-toolbar-refresh-theme-menus

## Objective

Three changes to how the application's strips read and behave:

1. **Toolbar buttons that look enabled.** Every icon (and text) button on a toolbar strip — the
   repository strip, the history toolbar, the branches/tags/remotes dialogs, the changes page, the
   diff viewer, the changed-files panel — is drawn today in the secondary grey with no plate, which
   reads as disabled. An enabled toolbar button gets the full foreground and a frame around it; a
   disabled one looks the way an enabled one looks today (grey, no frame), rather than the current
   35 % opacity that is too dark.
2. **One refresh button.** The main (repository) toolbar keeps its refresh button; every other one
   goes. That button refreshes everything, the way the periodic automatic refresh does.
3. **Flat file lists by default.** The changed-files panels (history diffs, changes page, stashes)
   open as a flat list instead of a tree, and the existing preference says so.

## Context & constraints

- `Themes/Styles.axaml` owns `Button.toolbar` (transparent, no border, `EnigmaForegroundSecondaryBrush`,
  hover/pressed plates, `:disabled` → opacity 0.35) and `ToggleButton.segment` (the list/tree switch,
  the diff viewer's toggles; grey unless checked).
- A **row action** is a `Button.toolbar` holding an `ei:Icon.row`, inside a `ListBoxItem` or
  `TreeViewItem`; its icon is already forced to the full foreground by
  `Button.toolbar ei|Icon.row`. Row actions are not toolbars and must not gain a frame on every row.
- Refresh buttons today: `MainWindow` (`RepositoryContext.RefreshAsync`), `HistoryPageView`
  (`ReloadAsync`), `ChangesPageView`, `BranchesPageView`, `TagsPageView`, `RemotesPageView`,
  `IntegrationsPageView` (re-reads the selected account's hosted repositories).
- `IAutoRefreshService.RefreshNowAsync` runs a quiet fetch (`ISyncOperations.FetchQuietlyAsync`, which
  re-reads the reference state after it), falls back to a local re-read when the fetch fails, and
  raises `Refreshed(AutoRefreshResult)`; `MainWindowViewModel` answers with
  `HistoryPageViewModel.RefreshInPlaceAsync(result.Changed)`. The changes, remotes, branches and tags
  pages follow `IRepositoryContext` on their own.
- `AppSettings.FilesView` exists (default `Tree`), shown on the settings page under "Changed files →
  Show them as"; `ChangedFilesPanelViewModel` applies it live. `SettingsService.Migrate` moves a legacy
  default that nobody chose to the new default (versions 2–4 did so for row height, diff view, lane
  width).

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| The enabled look | Full foreground (`EnigmaForegroundBrush`), a 1 px `EnigmaBorderBrush` frame, corner radius 4; the hover and pressed plates stay | The frame is the "rectangle around them" asked for, and the full foreground is what an enabled control looks like everywhere else in the theme | A filled plate in every state (heavy on a strip of eight buttons); an accent colour (the accent is reserved for the page's one primary action) |
| The disabled look | Today's enabled look: secondary grey, no frame, transparent, full opacity | Exactly what was asked: "the current look should be the disabled look" | Keeping the 0.35 opacity (too dark, the complaint) |
| Which buttons | Every `Button.toolbar` and `ToggleButton.segment`, text buttons included (Stage all, New branch, the merge banner's buttons) | They are all toolbar buttons and all read as disabled today | Icon-only buttons only (the text ones sit on the same strips and have the same problem) |
| Row actions | Stay frameless (`ListBoxItem`/`TreeViewItem` scope resets the frame); disabled, their icon greys instead of fading | A frame on every row of a list is noise, and the row actions were never the complaint; the disabled grey keeps the new rule consistent | A new class on every row action (a class nobody would remember to write) |
| Segment toggles | Unchecked: full foreground + frame; checked: the selection plate + frame; disabled: grey, no frame | An unchecked segment is an enabled button and read as disabled too | Leaving segments grey |
| What the one refresh does | The automatic refresh's pipeline, run now and marked as asked for: a quiet fetch, the re-read of HEAD, references and status, the history redrawn keeping its place (always, not only when something moved), and the Integrations page's repository list re-read | "A main refresh of everything … like the automatic refresh that happens every 15s does" | A local re-read only (what the button did before; not what was asked); a full history reload from the top (loses the reader's place) |
| A fetch that fails on a manual refresh | Quiet, like the automatic refresh; the local state is still re-read | The same pipeline; the Fetch button beside it is the one that reports a failing remote | An info bar per failed refresh |
| Pressed while an automatic refresh is running | Nothing more happens; the running refresh publishes its result | The gate the service already has; two fetches at once would only race | Queueing a second refresh |
| The page-level `RefreshCommand`s | Removed from the History, Changes, Branches, Tags, Remotes and Integrations ViewModels once no view binds them; the `RefreshAsync` methods stay | Dead commands mislead the next reader; the methods are what the context refresh and the tests call | Leaving the commands in place |
| Flat list by default | `AppSettings.FilesView` defaults to `List`, and the panel's own initial mode to `List` | What was asked | — |
| Existing settings files | Schema version 5: a file written before it that still says `Tree` (the old default, which nobody chose) moves to `List`; from version 5 on `Tree` is a preference like any other | The house migration rule, applied exactly as versions 2–4 applied it | Leaving existing installs on the tree (the change would reach nobody who has ever opened the settings) |
| The setting | The existing "Changed files → Show them as" setting, its description widened to name the history, the changes page and the diffs | The setting exists; its description said "of a commit" only | A second setting per page |

## PHASE01 — Toolbar buttons that look enabled

**Branch:** `feature/feature-7232-phase01-toolbar-buttons`
**Status:** DONE — see `docs/done/FEATURE-7232-PHASE01.md`

### Steps

1. `Themes/Styles.axaml`, `Button.toolbar`: `Foreground` → `EnigmaForegroundBrush`, `BorderBrush` →
   `EnigmaBorderBrush`, `BorderThickness` 1 (on the button and on its `PART_ContentPresenter`), padding
   adjusted by the border so the button keeps its size.
2. `Button.toolbar:disabled`: `Foreground` → `EnigmaForegroundSecondaryBrush`, border transparent, no
   opacity change (the 0.35 rule goes). `Button.toolbar:disabled ei|Icon.row` greys the row-action
   icon.
3. `ListBoxItem Button.toolbar, TreeViewItem Button.toolbar`: no frame (row actions keep their look).
4. `ToggleButton.segment`: the same frame and full foreground; checked keeps the selection plate;
   disabled greys and drops the frame.
5. Update the style comments to say what the enabled and the disabled looks are and why.
6. Tests (`App.UnitTests`, headless): an enabled toolbar button on the realised main window has the
   full foreground and a visible frame; a disabled one has the secondary foreground, no frame and an
   opacity of 1; a row action inside a list row has no frame; an unchecked segment has the full
   foreground and a frame.

### Acceptance criteria

- On every toolbar strip, an enabled button is drawn in the full foreground inside a frame.
- A disabled toolbar button is drawn in the secondary grey, with no frame, at full opacity.
- Row actions in list and tree rows are unchanged when enabled, and grey when disabled.
- Build clean with zero warnings; the whole suite green.

## PHASE02 — One refresh for everything

**Branch:** `feature/feature-7232-phase02-single-refresh`
**Status:** DONE — see `docs/done/FEATURE-7232-PHASE02.md`

### Steps

1. `AutoRefreshResult` gains `Requested` (the reader asked for this refresh). `IAutoRefreshService`
   gains `RequestRefreshAsync(CancellationToken)`, which runs the same pipeline as `RefreshNowAsync`
   and publishes a result with `Requested = true`.
2. `MainWindowViewModel.RefreshCommand` runs `RequestRefreshAsync` (busy while it runs); the
   `Refreshed` handler redraws the history in place when the references moved **or** the refresh was
   requested.
3. `IntegrationsPageViewModel` follows `Refreshed`: a requested refresh re-reads the selected account's
   repositories.
4. Remove the refresh buttons from `HistoryPageView`, `ChangesPageView`, `BranchesPageView`,
   `TagsPageView`, `RemotesPageView` and `IntegrationsPageView`, and the `RefreshCommand` properties
   their ViewModels no longer need; the main toolbar's tooltip says it refreshes everything.
5. Tests: the requested refresh fetches, re-reads, publishes `Requested`, and redraws the history even
   when nothing moved; a press while a refresh is running does nothing more; no page view carries a
   refresh button any more, and the main window carries exactly one; the integrations page re-reads
   its repositories on a requested refresh and not on a periodic one. Existing tests that used the
   removed commands move to the methods.

### Acceptance criteria

- The main toolbar is the only place with a refresh button.
- Pressing it fetches quietly, re-reads the repository state, redraws the history keeping its place,
  and re-reads the integrations page's repositories.
- The periodic automatic refresh behaves as before.
- Build clean with zero warnings; the whole suite green.

## PHASE03 — Flat file lists by default

**Branch:** `feature/feature-7232-phase03-flat-file-list`
**Status:** TODO

### Steps

1. `AppSettings.FilesView` defaults to `FilesView.List`; `CurrentVersion` → 5; `LegacyFilesView =
   FilesView.Tree`; the remarks and doc comments say so.
2. `SettingsService.Migrate`: a file with `Version < 5` whose `FilesView` is `Tree` moves to `List`.
3. `ChangedFilesPanelViewModel.ViewMode` starts as `List`.
4. The settings page's "Changed files" description names the history, the changes page and the diffs.
5. Tests: the defaults say `List`; a version-4 file saying `Tree` reads as `List`; a version-5 file
   saying `Tree` stays `Tree`; a version-4 file saying `List` stays `List`; a panel without settings and
   a panel with default settings both open as a list; existing tests that assumed the tree default are
   updated.

### Acceptance criteria

- The history diffs, the changes page and the stash files open as a flat list on a fresh install and on
  an install that never chose a shape.
- "Changed files → Show them as" still switches every panel between the list and the tree.
- Build clean with zero warnings; the whole suite green.

## Out of scope

- Remembering the panel's own list/tree toggle as the preference.
- Restyling buttons that are not toolbar buttons (`Button.accent`, `Button.choice`, dialog buttons).
- A refresh keyboard shortcut.
