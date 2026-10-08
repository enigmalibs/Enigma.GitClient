# FEATURE-B313 — Window title says Enigma Git Client

**Status:** DONE — see `docs/done/FEATURE-B313.md`
**Type:** FEATURE
**Branch:** `feature/feature-b313-window-title`
**Run:** feature/2026-10-08-title-watcher-refs-release

## Objective

The application's title, in the title bar and the taskbar, says **Enigma Git Client** instead of
`Enigma.GitClient`:

- the start window: `Enigma Git Client`;
- the repository window: `<repository> — Enigma Git Client`, or `Enigma Git Client` with nothing open.

## Context & constraints

- `ProductInformation` (Core) has two names:
  - `Name`, `"Enigma.GitClient"`, which is also the product token of the HTTP user agent and cannot
    carry a space;
  - `DisplayName`, `"Enigma Git Client"`, which the splash screen, the About dialog and the Linux
    launcher entry already use.
- The two window titles read `ProductInformation.Name`:
  - `StartWindowViewModel.WindowTitle`;
  - `MainWindowViewModel.WindowTitle`.
- `MainWindowShellTests` asserts the old titles.
- `DisplayNameConsistencyTests` ties the launcher entry and the installer scripts to `DisplayName`;
  nothing there changes.

## Steps

1. `StartWindowViewModel.WindowTitle` and `MainWindowViewModel.WindowTitle` read
   `ProductInformation.DisplayName`.
2. `ProductInformation.DisplayName`'s documentation lists the window titles among the surfaces that
   show it.
3. Tests: the repository window's two titles (`MainWindowShellTests`), and the start window's title
   (new assertion).

## Acceptance criteria

- The start window's title is `Enigma Git Client`.
- The repository window's title is `Enigma Git Client` with nothing open, and
  `<name> — Enigma Git Client` with a repository open.
- The user agent, the settings folder name (`Enigma.GitClient`) and every other use of
  `ProductInformation.Name` are unchanged.
- Build: zero warnings. The whole suite is green.

## Out of scope

- Other sentences that name the product as `Enigma.GitClient` (messages, the Settings page's About
  card, the README title). The request is about the title.
- Renaming the assemblies, the executable or the settings folder.

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| Which titles | Both windows: the start window and the repository window | Both are "the app title" in the taskbar; changing one would leave the other saying the old name | The repository window only |
| How | Read the existing `ProductInformation.DisplayName` | One source of truth, already tied to the launcher entry by a test | A third constant; changing `Name` (it is the user-agent token and cannot carry a space) |
| Other `Enigma.GitClient` sentences | Left as they are | Outside "the app title"; changing user-facing messages is its own decision | A product-wide rename of every sentence |
