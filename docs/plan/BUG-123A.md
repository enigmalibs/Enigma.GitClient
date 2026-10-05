# BUG-123A — A repository opens on its history

**Status:** DONE — see `docs/done/BUG-123A.md`
**Type:** BUG
**Branch:** `bugfix/bug-123a-repository-opens-on-history`
**Run:** vibe/2026-10-05-profiles-graph-fetch-release

## Objective

Opening a repository in the repository window always shows its **History** page — not the Profiles or
Settings page that was selected when the previous repository was closed.

## Context & constraints

- `ViewModels/MainWindowViewModel.cs` is a singleton; `InitialiseAsync` (on each repository window's
  `Opened`) calls `Shell.Start()` only the first time (`_initialised`). The rail's selection lives in the
  singleton `INavigationService`, so a page selected before *Close this repository* is still selected
  when the next repository's window opens.
- A clone started from the repository window's Profiles page opens the clone in the same window
  (`AppWindows.ShowRepository` keeps it), still on Profiles.
- `Navigation/ShellNavigation.GoTo(ShellPage.History)` selects the page.
- Tests: `AppWindowsTests`, `MainWindowShellTests`, `NavigationRailTests`.

## Steps

1. `MainWindowViewModel`: when a repository is opened (the context's `Repository` becomes a repository),
   the rail goes to History.
2. Tests: Profiles selected → close → open another repository → History; Settings likewise; a repository
   opened while the window is on Profiles (the clone case) → History.

## Acceptance criteria

- Whatever page was selected before, opening a repository shows its History page.
- Build clean, whole suite green, the new tests among them.

## Out of scope

- Remembering a page per repository.

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| The trigger | The repository being opened | Covers reopening from the start window and a clone opened in the same window | Only on the window's `Opened` (misses the clone case); on close (the clone case again) |
| The conflicts page during a merge | History too | The banner leads to it; the history is the repository's front page | Opening on Conflicts |
