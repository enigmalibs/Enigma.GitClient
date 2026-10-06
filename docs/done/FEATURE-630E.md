# FEATURE-630E — Folder and terminal on the home list

**Item:** FEATURE-630E — Folder and terminal on the home list
**Branch:** `feature/feature-630e-home-folder-terminal`
**Run:** vibe/2026-10-06-diff-search-refs-release

## Summary

Every row of the home window's repository list now has the two buttons the repository window's
toolbar has (FEATURE-3B4E): **open the folder in the file manager** and **open a terminal in it**.

- **`RepositoriesPageViewModel`** takes `ISystemInterop` and has two new commands,
  `OpenFolderCommand` and `OpenTerminalCommand` (`AsyncRelayCommand<ListedRepository>`):
  - a repository that no longer exists gets the page's existing "That repository has moved" warning,
    and nothing is launched;
  - a launch that fails gets the toolbar's own warnings, "The folder did not open" and "No terminal
    started", with the path.
- **The "has moved" check** was already written out twice in this ViewModel, for *Open* and for *Open
  in a new window*. It is now a single `IsStillThere(entry)` shared by the four row actions that need
  the folder. Behaviour is unchanged.
- **`RepositoriesPageView.axaml`:** the two icon buttons sit in each row after *Forget*, before *Open in
  a new window* and *Open*. They use `FolderOpen` and `TerminalWindow`, with the toolbar's tooltips
  and accessible names. The row's drag ignores presses on any button, so they never start a reorder.

## Files / modules touched

**Modified**

- `src/Enigma.GitClient.Desktop/ViewModels/Pages/RepositoriesPageViewModel.cs`
- `src/Enigma.GitClient.Desktop/Views/Pages/RepositoriesPageView.axaml`
- `tests/Enigma.GitClient.Desktop.UnitTests/RepositoryFolderTests.cs`: a home-list section:
  - both commands hand the row's path to the desktop;
  - a repository that has moved warns and launches nothing;
  - a failed launch warns with the toolbar's words;
  - the realised row carries both buttons with their commands, parameters, icons, tooltips and
    names.
- `tests/Enigma.GitClient.Desktop.UnitTests/RepositoryReorderTests.cs`: `APressOnARowsButton_StartsNoDrag`
  is now a theory over *Forget* and the two new buttons.
- `docs/roadmap.md`, `docs/plan/FEATURE-630E.md`: statuses.

**Created**

- `docs/done/FEATURE-630E.md`

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| The duplicated "has moved" check | One `IsStillThere` helper for all four row actions | The new commands would have made it four copies of the same five lines. It is in the same file, about the same rows, and behaves identically |
| Where the messages live | Repeated in this ViewModel, with the toolbar's exact words | Two `Notify` calls. A shared service for them would be more code than it saves |

## Deviations & follow-ups

- **Not run:** the new and the changed tests are compiled but were not run, because you asked to skip
  the Desktop tests this session.
- On Windows, opening a folder reports a false failure until BUG-6EA3, the next item of this run. That
  fix is in `SystemInterop`, so it covers these buttons too.
- Line endings: the touched files are LF; no CRLF churn.

## Documentation sweep

Nothing made factually wrong. The README's start-window bullet never listed the row's actions. The
release notes (FEATURE-1795) describe the buttons.

## Build/test evidence

- `dotnet build Enigma.GitClient.slnx -c Debug --no-incremental`: 0 warnings, 0 errors. This
  compiles the tests.
- `Enigma.GitClient.Core.UnitTests`: 1159 total, 1158 passed, 1 skipped, 0 failed. The racing
  `AtomicFileTests` test is excluded (BUG-6EAA, it hangs on Windows).
- `Enigma.GitClient.Core.IntegrationTests`: 351 total, 349 passed, 2 skipped, 0 failed.
- `Enigma.GitClient.Desktop.UnitTests`: **not run**, at your request.
- Fix budget: 0 cycles used.
