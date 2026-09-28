# FEATURE-49E4 — About in the start window

**Status:** DONE — see `docs/done/FEATURE-49E4.md`
**Type:** FEATURE
**Branch:** `feature/feature-49e4-about-on-start`
**Run:** bugfix/2026-09-28-changes-commit-details-release

## Objective

The About dialog opens from the start window's home, the Repositories page, as it does from the
repository window's toolbar — not only from the Settings page's About card.

## Context & constraints

- `IAboutDialogService.ShowAsync()` (`Services/AboutDialogService.cs`) shows `AboutView` on the
  window's `IContentDialogService` host; both windows register one (`HostDialog`).
- The repository window opens it from an Info `toolbar` button at the end of its strip
  (`MainWindowViewModel.OpenAboutCommand`, tooltip "About", automation name "About Enigma git
  client"). The start window has no strip: its rail holds Repositories, Profiles and Settings, and the
  rails navigate to pages, not dialogs (FEATURE-75F4's decision).
- The Repositories page's header already carries the start window's actions on its right: Open,
  Clone, Create.

## Steps

1. `RepositoriesPageViewModel`: inject `IAboutDialogService`; `OpenAboutCommand` (`AsyncRelayCommand`)
   shows it.
2. `RepositoriesPageView.axaml`: at the end of the header's button row, after Create and a thin
   divider (as the repository strip separates its groups), an Info `toolbar` button bound to
   `OpenAboutCommand`, with the repository toolbar's tooltip and automation name.
3. Tests: the command shows the About view with its single Close button (scripted dialog service); the
   page's header carries the button bound to the command.

## Acceptance criteria

- The Repositories page shows an Info button at the right end of its header; clicking it opens the
  same About dialog the repository window shows.
- The Settings page's About card still opens it.
- Build clean with zero warnings; the whole suite green.

## Out of scope

- A strip or toolbar for the whole start window; an About page in the rail.

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| Where in the start window | An Info button at the end of the Repositories page header | The home page's header is the start window's toolbar, and its right end is where the repository window puts About | A rail item (rails navigate to pages); a new window-wide strip (new chrome on every page); an About page |
| Which dialog | The existing `IAboutDialogService` | One About, the same everywhere | A page-hosted copy of `AboutView` |
