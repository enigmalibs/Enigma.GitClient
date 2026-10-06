# FEATURE-630E — Folder and terminal on the home list

**Status:** TODO
**Type:** FEATURE
**Branch:** `feature/feature-630e-home-folder-terminal`
**Run:** vibe/2026-10-06-diff-search-refs-release

## Objective

Each repository of the home window's list can be opened in the file manager and in a terminal from its
own row, as the repository window's toolbar does for the open repository (FEATURE-3B4E).

## Context & constraints

- The toolbar: `MainWindowViewModel.OpenFolderCommand` / `OpenTerminalCommand` →
  `ISystemInterop.OpenPathAsync` / `OpenTerminalAsync`, with the warnings "The folder did not open" /
  "No terminal started" when nothing launched; icons `FolderOpen` / `TerminalWindow`.
- The home list: `RepositoriesPageView.axaml`, one row per `ListedRepository` (`Name`, `Path`,
  `Exists`), with icon buttons (forget `X`, new window `ArrowSquareOut`) and *Open*;
  `RepositoriesPageViewModel` already reports a missing repository as "That repository has moved".
- `ISystemInterop` is registered in DI; the tests use `RecordingSystemInterop`
  (`OpensPaths` / `OpensTerminals`).

## Steps

1. `RepositoriesPageViewModel`: takes `ISystemInterop`; `OpenFolderCommand` and `OpenTerminalCommand`
   (`AsyncRelayCommand<ListedRepository>`): a missing repository gets the existing "has moved"
   warning; a launch that fails gets the toolbar's own warnings.
2. `RepositoriesPageView.axaml`: two icon buttons per row — `FolderOpen` and `TerminalWindow`, after
   the forget button and before the new-window one — with the toolbar's tooltips and accessible names
   ("…the repository's folder…").
3. Tests: both commands hand the row's path to the desktop; a missing repository warns and launches
   nothing; a failed launch warns; the realised row carries both buttons with their icons, names and
   commands; dragging a row by those buttons does not start a reorder (buttons are excluded like the
   others).

## Acceptance criteria

- Every row of the home list has a folder button and a terminal button that open that repository's
  folder in the file manager and a terminal in it.
- A repository that no longer exists says so instead of launching anything.
- Release build clean with zero warnings; the whole suite green.

## Out of scope

- Context menus on the rows.
- The Windows false failure (BUG-6EA3, its own item).

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| Placement | Icon buttons in the row, after forget, before new window and *Open* | The row's other actions are icon buttons there; *Open* stays last | A row context menu; a header toolbar acting on a selection the list does not have |
| Messages | The toolbar's own titles and texts | One wording for one action | New wording |
| Missing repository | The existing "That repository has moved" warning | What *Open* and *new window* already say | Disabling the buttons |
| Shared code | The two commands on the page's ViewModel, calling `ISystemInterop` | Two short handlers; a shared service would be more code than it saves | A new folder-operations service |
