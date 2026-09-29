# FEATURE-2087 — Clone remembers its directory

**Status:** DONE — see `docs/done/FEATURE-2087.md`
**Type:** FEATURE
**Branch:** `feature/feature-2087-remember-clone-directory`
**Run:** feature/2026-09-29-rail-clone-history

## Objective

When a repository is cloned, remember the directory it was cloned into, and suggest that directory
for the next clone instead of the home folder.

## Context & constraints

- `ViewModels/Pages/RepositoriesPageViewModel.cs`:
  - `OnCloneAsync` builds `CloneRepositoryDialogViewModel(_folderDialogs, _repositories,
    DefaultParentDirectory())`. The dialog's *Where* field starts on that directory.
  - `RunCloneAsync(CloneRequest)` runs every clone: the dialog's, and the one the profiles page starts
    from a host's repository list.
  - `public static DefaultParentDirectory()` returns the user profile, or the current directory. It is
    also used by `OnCreateAsync` (*Create a repository*) and by `ProfilesPageViewModel
    .OnBrowseRepositoriesAsync`, which clones with no dialog.
- Remembered choices already live in `AppSettings` and are written through `ISettingsService.Update`.
  The branch and tag sort choices are the precedent. A key an older file does not have reads as its
  default, so no schema migration is needed.
- `SettingsService.ResetAsync` puts every value back to its default.
- The clone creates its parent directory (`RepositoryService.CloneAsync` →
  `Directory.CreateDirectory`), so after a clone succeeds, its parent directory exists.

## Steps

1. Core, `AppSettings`:
   - `CloneParentDirectory` (`string`, default empty, meaning the home folder), documented like its
     neighbours;
   - `Normalised()` trims it (null becomes empty).
2. App, `RepositoriesPageViewModel`:
   - takes `ISettingsService`;
   - an instance `CloneParentDirectory()` returns the remembered directory when it is set and still
     exists, else the existing default;
   - `OnCloneAsync` opens the dialog on `CloneParentDirectory()`;
   - `RunCloneAsync`, on success only, records `request.ParentDirectory` (full path) through
     `_settings.Update`.
   - `DefaultParentDirectory()` stays as it is for *Create a repository*.
3. `ProfilesPageViewModel.OnBrowseRepositoriesAsync` clones into `_repositories.CloneParentDirectory()`.
4. Tests:
   - Core: the new key defaults to empty, round-trips through the settings file, and is trimmed;
   - App:
     - a successful clone stores its parent directory, and the next clone dialog opens on it;
     - a failed or cancelled clone stores nothing;
     - a remembered directory that no longer exists falls back to the home default;
     - the host-browser clone uses the remembered directory.

## Acceptance criteria

- After a successful clone into `<dir>/<name>`, the next *Clone a repository* dialog suggests `<dir>`.
  So does the next clone from a host's repository list. This holds across restarts.
- A clone that fails or is cancelled leaves the remembered directory as it was.
- A remembered directory that has since disappeared is not offered; the home folder is.
- *Create a repository* keeps its home-folder default.
- Build clean with zero warnings; the whole suite green.

## Out of scope

- Showing or editing the directory on the Settings page.
- Remembering the directory for *Create a repository*.
- A list of recent clone directories.

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| Where it is kept | `AppSettings.CloneParentDirectory`, no schema bump | The sort choices are the precedent. An older file reads the default | The recent-repositories file; a file of its own |
| When it is saved | After a clone succeeds | A failed clone (wrong URL, no access) shouldn't move the next default | When the dialog is confirmed |
| Which clones use it | The dialog's and the host-browser's; both go through `RunCloneAsync` | "The next clone" is any clone | The dialog only |
| *Create a repository* | Keeps the home default | The draft asks about clones only | Sharing the remembered directory |
| A directory that is gone | Fall back to the home default | Doesn't offer a path on a drive that is no longer there | Offering it anyway |
| Shown in Settings | No | A remembered choice, like the sort order, not a preference. *Reset* forgets it, which is harmless | A Settings card |
