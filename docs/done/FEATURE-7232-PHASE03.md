# FEATURE-7232-PHASE03 — Flat file lists by default

**Item:** FEATURE-7232 — Toolbars, one refresh, flat file lists
**Branch:** `feature/feature-7232-phase03-flat-file-list`
**Run:** feature/2026-09-24-toolbar-refresh-theme-menus

## Summary

The changed files now open as a flat list, not a tree — in the history's diffs, on the changes page
(unstaged and staged) and in a stash — on a fresh install and on any install that never chose a shape.
The preference that decides it is the existing **Settings → Changed files → Show them as** ("A flat
list" / "A tree of folders"), which every panel follows live; its description now names the history,
the changes page and the diffs. The toggle on each panel still switches the one panel in front of the
reader.

Settings files move to schema version 5: a file written by an earlier build that still says `Tree` —
the shape every earlier build opened on, so a shape nobody chose — reads as `List`, exactly as
versions 2–4 moved their own old defaults; from version 5 on, `Tree` is a preference like any other
and is kept.

## Files / modules touched

**Modified — Core**

- `Configuration/AppSettings.cs` — `CurrentVersion` 5; `LegacyFilesView = FilesView.Tree`;
  `FilesView` defaults to `List`; the remarks say why
- `Configuration/SettingsService.cs` — `Migrate`: a pre-version-5 `Tree` becomes `List`; the remarks
  describe the fourth migration

**Modified — App**

- `ViewModels/Panels/ChangedFilesPanelViewModel.cs` — `ViewMode` starts as `List`
- `Views/Pages/SettingsPageView.axaml` — the "Changed files" card's description names the history,
  the changes page and the diffs

**Tests**

- `Core.UnitTests/Configuration/SettingsServiceTests.cs` — the default is `List`; a version-4 `Tree`
  becomes `List` and keeps the other keys; a version-4 `List` is kept; a version-5 `Tree` is never
  migrated; the version-1 file goes through all four migrations
- `App.UnitTests/ChangedFilesPanelTests.cs` — `Panel_OpensAsAFlatList` (no preference: a list, no
  directory rows); a `LoadedAsTree` helper for the tests about the tree; the history's details pane
  draws names without a directory row
- `App.UnitTests/SettingsPageTests.cs` — a fresh install's panel is a list, and the preference moves an
  open panel to the tree and back
- `App.UnitTests/ChangesPageTests.cs` — staging a whole directory chooses the tree first (a directory
  row is the tree's)

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| A settings file with no `filesView` key | The new default (`List`), no migration needed | A missing key reads as the record's default, as for every other preference |
| Tests about the tree | Ask for the tree explicitly (`LoadedAsTree`, or the preference) | They test the tree, not the default; the default has a test of its own |

## Deviations & follow-ups

- None from the plan.
- Follow-up (out of scope): the panel's own list/tree toggle still changes only that panel for the
  session; writing it back as the preference would make the last choice stick.
- Recommendation only: line endings were not examined; nothing in this diff showed CRLF churn.

## Documentation sweep

- Nothing to change: README ("shown as a list or a tree (your choice)") and the release notes ("as a
  list or a tree, whichever you prefer") are still true.

## Build/test evidence

- `dotnet build Enigma.GitClient.slnx`: 0 warnings, 0 errors.
- `dotnet test --solution Enigma.GitClient.slnx`: 1979 passed, 0 failed (4 new). First run: 10
  failures, all tests that relied on the tree being the default; green after one fix cycle.
