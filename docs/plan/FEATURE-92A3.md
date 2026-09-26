# FEATURE-92A3 — History: no scope, no first parent

**Status:** DONE
**Type:** FEATURE
**Branch:** one per phase, see below
**Run:** feature/2026-09-26-release-1-0-0

## Objective

Take two controls off the History page's toolbar, with everything that exists only to serve them:

1. **The "All branches / Current branch" combobox** — the history always shows every branch.
2. **The "First parent only" checkbox** — the history always follows every parent.

## Context & constraints

- `HistoryPageViewModel` holds `HistoryScopeOption` (a record in the same file), `ScopeOptions`,
  `SelectedScope` (which rewrites `_query.Scope` and reloads) and `FirstParentOnly` (which rewrites
  `_query.FirstParentOnly` and reloads). `ApplySettings` copies `AppSettings.FirstParentOnly` into it.
- The preference behind the checkbox is `AppSettings.FirstParentOnly`, edited by a "Follow first
  parents only" toggle on the Settings page (`SettingsPageViewModel.FirstParentOnly`), and asserted by
  `SettingsServiceTests` and `SettingsPageTests`.
- The engine keeps both notions: `CommitLogQuery.Scope` / `CommitLogScope` (used by Core and its
  integration tests with `Head` and `Revision`) and `CommitLogQuery.FirstParentOnly` (with an
  integration test). The query's default scope is already `AllRefs`.
- `settings.json` is read with `System.Text.Json`, which ignores members it does not know — a file
  written by an earlier build, carrying `firstParentOnly`, must still load.
- `RELEASENOTES.md` lists "first-parent history" among the preferences.

## PHASE01 — No branch-scope selector

**Status:** DONE — see `docs/done/FEATURE-92A3-PHASE01.md`
**Branch:** `feature/feature-92a3-phase01-no-scope-selector`

### Steps

1. `Views/Pages/HistoryPageView.axaml` — remove the combobox; renumber the toolbar grid.
2. `ViewModels/Pages/HistoryPageViewModel.cs` — remove `HistoryScopeOption`, `ScopeOptions`,
   `SelectedScope` and the constructor line selecting the first option; the query keeps its default
   scope, every ref.
3. Tests — drop the uses of `SelectedScope`; assert the history reads with `CommitLogScope.AllRefs`
   and that the toolbar has no combobox.

### Acceptance criteria

- The History toolbar has no scope selector; the history shows every branch, as "All branches" did.
- Build clean with zero warnings; the whole suite green.

## PHASE02 — No first-parent history

**Status:** DONE — see `docs/done/FEATURE-92A3-PHASE02.md`
**Branch:** `feature/feature-92a3-phase02-no-first-parent`

### Steps

1. `Views/Pages/HistoryPageView.axaml` — remove the checkbox; renumber the toolbar grid.
2. `ViewModels/Pages/HistoryPageViewModel.cs` — remove `FirstParentOnly` and its handling in
   `ApplySettings`.
3. `Core/Configuration/AppSettings.cs` — remove `FirstParentOnly`.
4. `ViewModels/Pages/SettingsPageViewModel.cs` and `Views/Pages/SettingsPageView.axaml` — remove the
   "Follow first parents only" toggle and its property (and any description or change notification
   naming it).
5. Tests — remove the first-parent assertions from the history, settings-page and settings-service
   tests; add one that a `settings.json` carrying `"firstParentOnly": true` still loads, keeping the
   other preferences, and is written back without it.
6. `RELEASENOTES.md` — drop "first-parent history" from the preferences (sweep).

### Acceptance criteria

- Neither the History page nor the Settings page offers first-parent history, and nothing in the app
  sets it.
- An existing settings file carrying the old preference loads without error.
- Build clean with zero warnings; the whole suite green.

## Out of scope

- The engine's `CommitLogScope` and `CommitLogQuery.FirstParentOnly`, which stay as engine options.
- Anything else on the History toolbar.

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| What "with its usages" covers for the checkbox | The checkbox, the page's property, the Settings toggle and `AppSettings.FirstParentOnly` | The preference is the other half of the checkbox: kept alone, it would still strip the merged-in branches from the graph with nothing on the page saying so | Removing the checkbox only (a hidden switch that changes the graph) |
| The engine's `CommitLogQuery.FirstParentOnly` and `CommitLogScope` | Kept | Engine options with their own tests, alongside the other filters the UI does not use (author, message, paths, dates); removing them changes no behaviour | Deleting them from Core (API churn with nothing gained) |
| Old settings files | Loaded as they are; the key disappears at the next save | `System.Text.Json` ignores unknown members; a test pins it | A settings-version migration (nothing to migrate) |
| One phase or two | Two | Two independent removals, each a small reviewable commit; the second reaches into the settings | One commit for both |
