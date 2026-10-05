# FEATURE-1CCB — Changed files as a tree by default

**Item:** FEATURE-1CCB — Changed files as a tree by default
**Branch:** `feature/feature-1ccb-tree-by-default`
**Run:** feature/2026-10-05-revert-tree-basedir-release

## Summary

The changed files open as a **tree of folders** again — a commit's files and the uncommitted files in
the history's details panel, and a stash's — on a fresh install and on any install that never chose a
shape. **Settings → Changed files → Show them as** still switches every panel, live, and the toggle on
each panel still switches the one in front of the reader.

Settings files move to schema version 6. A version-5 file that says `List` — version 5's own default,
so a shape nobody chose — reads as `Tree`. A file written before version 5 is no longer moved at all:
its `Tree` was never chosen and is the default again, and its `List` was chosen against the tree's
default, so both are kept. From version 6 on, `List` is a preference like any other.

## Files / modules touched

**Modified — Core**

- `Configuration/AppSettings.cs` — `CurrentVersion` 6; `FilesView` defaults to `Tree`;
  `LegacyFilesView = FilesView.List` (version 5's default); `Normalised()` falls back to the default
  for an undefined value; the remarks say why
- `Configuration/SettingsService.cs` — `Migrate`: the version-5 rule (pre-5 `Tree` → `List`) becomes
  the version-6 one (a version-5 `List` → `Tree`); the remarks describe it

**Modified — App**

- `ViewModels/Panels/ChangedFilesPanelViewModel.cs` — `ViewMode` starts as `Tree`

**Tests**

- `Core.UnitTests/Configuration/SettingsServiceTests.cs` — the default is `Tree`; a version-5 `List`
  becomes `Tree` and keeps the other keys; a version-5 `Tree` is kept; a version-4 `Tree` and a
  version-4 `List` are both kept; a version-6 `List` is never migrated (the version-1 file still goes
  through every migration)
- `Desktop.UnitTests/ChangedFilesPanelTests.cs` — `Panel_OpensAsATree`; a `LoadedAsList` helper for the
  list's own tests; the history opens the diffs on the tree's first file, selects another commit's own
  first file, and draws the details panel with its directory row
- `Desktop.UnitTests/SettingsPageTests.cs` — a fresh install's panel is a tree; the preference moves an
  open panel to the list and back
- `Desktop.UnitTests/FileListToggleTests.cs` — the toggle test asks for the list it is about
- `Desktop.UnitTests/HistoryPageTests.cs` — the details panel's single file is read through the tree

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| `Normalised()` for an undefined value | `Defaults.FilesView` rather than a literal | One place says what the default is, as `DiffView` already does |
| Tests about the list | Ask for the list explicitly (`LoadedAsList`, `ViewMode = List`) | They test the list, not the default; the default has a test of its own |
| History tests about "the first file" | Expect the tree's first file | They are about opening on the first file; the tree is what a reader now sees |

## Deviations & follow-ups

- None from the plan.
- Follow-up (out of scope, as for 5.x): the panel's own list/tree toggle still changes only that panel
  for the session.
- Recommendation only: line endings were not examined; nothing in this diff showed CRLF churn.

## Documentation sweep

- Nothing to change: README ("shown as a **list or a tree** (your choice)") and the settings card's
  description are still true. The release notes for 5.4.0 (FEATURE-4707) carry the upgrade note.

## Build/test evidence

- `dotnet build Enigma.GitClient.slnx`: 0 warnings, 0 errors.
- `dotnet test --solution Enigma.GitClient.slnx`: 2770 passed, 0 failed (2 more than before: five
  migration tests replace three).
- First run: 11 failures, all tests that relied on the list being the default (3 Core, 8 Desktop);
  green after **1 fix cycle**.
