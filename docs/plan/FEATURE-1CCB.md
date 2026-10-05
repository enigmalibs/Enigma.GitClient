# FEATURE-1CCB — Changed files as a tree by default

**Status:** DONE — see `docs/done/FEATURE-1CCB.md`
**Type:** FEATURE
**Branch:** `feature/feature-1ccb-tree-by-default`
**Run:** feature/2026-10-05-revert-tree-basedir-release

## Objective

The changed-files panels — a commit's files and the uncommitted files in the history's details panel,
and a stash's — open as a **tree of folders** by default instead of a flat list, on a fresh install and
on an install that never chose a shape.

## Context & constraints

- `AppSettings.FilesView` (Core) is the preference ("Settings → Changed files → Show them as"); every
  `ChangedFilesPanelViewModel` follows it live (`ApplySettings`), and starts as `List` before settings
  arrive. FEATURE-7232 PHASE03 made `List` the default in schema version 5, moving a pre-5 file that
  still said `Tree` to `List`.
- `SettingsService.Migrate`'s rule: an older build's default is a value nobody chose, so it moves to
  the new default; anything else was chosen and is kept. Each schema change is one `if` keyed on the
  stored version.
- Every key is written into the file, so a version-5 file always says `filesView`; a missing key reads
  as the record's default.

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| Which panels | Every panel that follows "Show them as": the history's commit files and uncommitted files, and the stash files | "The uncommitted/committed files panel"; one preference drives them all | A separate preference for the working tree |
| Existing installs | Schema version 6: a version-5 file saying `List` (version 5's default) becomes `Tree`; files older than 5 are no longer moved — their `Tree` was never chosen and is the default again, their `List` was chosen and is kept | Without a migration the user's own install would never change; this is the house rule applied to the shape each version shipped | Fresh installs only; moving every `List` (overwrites a pre-5 explicit choice) |
| The constant naming version 5's default | `LegacyFilesView` becomes `FilesView.List`, documented as version 5's default | The `Legacy*` constants name the value a migration replaces | A second constant beside the old one (the old one is no longer used) |
| The panel's own list/tree toggle | Unchanged — it switches that panel for the session | Out of the draft | Writing it back as the preference |

## Steps

1. `AppSettings`: `FilesView` defaults to `FilesView.Tree`; `CurrentVersion` → 6; `LegacyFilesView =
   FilesView.List` (version 5's default); the remarks and doc comments say so; `Normalised()` falls back
   to `Tree` for an undefined value.
2. `SettingsService.Migrate`: the version-5 rule is replaced by the version-6 one —
   `stored.Version == 5 && FilesView == LegacyFilesView` → `Tree`; the remarks describe it.
3. `ChangedFilesPanelViewModel.ViewMode` starts as `Tree`.
4. Tests:
   - Core: the default is `Tree`; a version-5 file saying `List` reads as `Tree`; a version-5 file saying
     `Tree` stays `Tree`; a version-4 file saying `List` stays `List`; a version-4 file saying `Tree`
     stays `Tree`; a version-6 file saying `List` stays `List`; the version-1 file still goes through
     every migration.
   - Desktop: a panel without settings and a panel with default settings open as a tree; tests that
     relied on the list default ask for the list explicitly.

## Acceptance criteria

- On a fresh install, and on an install whose settings say version 5 and `List`, the history's commit
  and uncommitted files and the stash files open as a tree.
- "Changed files → Show them as" still switches every panel between the list and the tree, and a list
  chosen from version 6 on is kept.
- Build clean with zero warnings; the whole suite green.

## Out of scope

- Remembering the panel's own toggle as the preference.
- Changing how far a tree expands on its own (`FilesAutoExpandLimit`).
