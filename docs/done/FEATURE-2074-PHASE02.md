# FEATURE-2074-PHASE02 — Stash operations that know about conflicts

**Item:** FEATURE-2074 — Stashes like GitKraken
**Branch:** `feature/feature-2074-phase02-stash-operations`
**Run:** bugfix/2026-09-28-history-stash-menus-release

## Summary

The stash's four operations now live in one App service, `IStashOperations`, which the Changes page
already uses and the history uses from PHASE03. They behave as GitKraken's do:

- **Stash**: a dialog asks for an optional message. Everything uncommitted is stashed, untracked files
  included. With no message, git names the entry. "Nothing to stash" is a warning; success says the work
  is on the stash as `stash@{0}`.
- **Pop**: without conflicts, the changes are uncommitted changes again and the stash is gone. With
  conflicts, git keeps the stash. The reader gets a warning that the stash was kept and that the
  conflicted files are on the Changes page.
- **Apply**: the changes come back, and the stash is always kept. Conflicts are reported the same way.
- **A refusal** (uncommitted work the entry would overwrite) changes nothing and is reported as an error
  with git's first line.
- **Delete**: a confirmation names the stash and says its changes cannot be recovered. The harmless
  button is the default, and cancelling keeps the stash.

In Core, `IStashService.ApplyAsync` and `PopAsync` now return a `StashApplyResult` (`Applied`,
`Conflicted`) instead of throwing on conflicts. A conflict is exit code 1 plus git's own `CONFLICT (`
report (`StashService.IsConflict`; git runs under `LC_ALL=C` here). Every other failure still throws.

## Files / modules touched

**Created**

- `src/Enigma.GitClient.App/Services/StashOperations.cs` — `IStashOperations`, `StashOperations`.
- `src/Enigma.GitClient.App/ViewModels/Dialogs/StashDialogViewModel.cs`,
  `src/Enigma.GitClient.App/Views/Dialogs/StashDialogView.axaml(.cs)` — the stash dialog.
- `tests/Enigma.GitClient.App.UnitTests/StashOperationsTests.cs` — 9 tests over real git with scripted
  dialogs.

**Modified**

- `src/Enigma.GitClient.Core/Stashes/StashService.cs` — `StashApplyResult`, `IsConflict`, and
  `BringBackAsync` behind `ApplyAsync`/`PopAsync`.
- `src/Enigma.GitClient.App/DependencyInjection/ServiceCollectionExtensions.cs` — registration.
- `src/Enigma.GitClient.App/ViewModels/Pages/ChangesPageViewModel.cs` — Stash all, Apply, Pop and Drop go
  through `IStashOperations` and re-read the page after a change.
- `src/Enigma.GitClient.App/Views/Pages/ChangesPageView.axaml` — the history's wording:
  - "Apply stash", "Pop stash", "Delete stash…";
  - tooltips and accessible names to match.
- `tests/Enigma.GitClient.Core.IntegrationTests/Stashes/StashServiceTests.cs` — results asserted; a
  conflicted apply; a refused pop.
- `tests/Enigma.GitClient.Core.UnitTests/Stashes/StashParsingTests.cs` — `IsConflict`.
- `tests/Enigma.GitClient.App.UnitTests/StashPanelTests.cs` — stash-all now answers the dialog.
- `docs/roadmap.md`, `docs/plan/FEATURE-2074.md` — statuses.

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| Return value of the operations | `bool`: "the work tree changed" (a conflicted apply included) | That is what a page needs to know to re-read itself |
| Conflict wording | Point to the Changes page | That is where conflicted files are listed (out of scope: the conflicts page serves merges) |
| Changes page labels | "Apply stash" / "Pop stash" / "Delete stash…" | One vocabulary with the history. "Delete" is the word the request uses. |
| The old inline confirmation on the Changes page | Replaced by the service's | Same meaning ("cannot be recovered", safe default); the existing test's assertions still hold |

## Deviations & follow-ups

- The plan said "Stash all … go through `IStashOperations`". That also means the Changes page's one-click
  Stash all now asks for a message first. The test was adapted to answer the dialog.
- Line endings: no CRLF churn.

## Documentation sweep

The README's "Remotes: fetch, pull …, and stash" is still accurate. No edit.

## Build/test evidence

- `dotnet build Enigma.GitClient.slnx`: 0 warnings, 0 errors.
- `dotnet test --solution Enigma.GitClient.slnx`: **2395 passed**, 0 failed (13 new: 9 App, 2 Core
  integration, 2 Core unit), with no fix cycle.
