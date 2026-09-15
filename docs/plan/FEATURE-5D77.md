# FEATURE-5D77 — Settings, preferences & docs

**Status:** TODO
**Type:** FEATURE
**Branch:** `feature/feature-5d77-settings-and-docs`
**Run:** feature/2026-09-15-enigma-git-client

## Objective

Give the app a real settings surface — the preferences the rest of the run has been persisting — and
finish the user-facing documentation.

## Context & constraints

- Single-phase: the settings store already exists (introduced with recent repositories in
  FEATURE-52FB); this item gives it a UI, completes the option set, and writes the docs.
- Built from `SettingsCard` / `SettingsCardExpander` so the page matches the house look.

## Steps

1. `AppSettings` (versioned record) consolidating everything earlier devs persisted: theme variant
   (Dark / Light / Follow system), changed-files view mode (list / tree) and directory collapsing,
   diff view mode, context lines, whitespace handling, tab width, word-wrap, the history page size,
   `--first-parent` default, date format (relative / absolute), graph row height and lane width,
   the pull strategy (`merge` / `ff-only`), confirmation toggles for destructive actions, and the
   git executable path override.
2. `ISettingsService` — load, save (debounced), `Changed` event, and a migration hook keyed on the
   schema version; a corrupt file is backed up and replaced with defaults rather than crashing.
3. Settings page: grouped `SettingsCard` rows (Appearance, History & graph, Diff, Git, Integrations,
   Advanced), each bound to the store with an immediate effect (theme switches live, graph metrics
   re-render live).
4. An About card: app version, the Avalonia and git versions in use, the licence, and the explicit
   scope statement ("no rebase, no issues, no pull requests").
5. A "Reset to defaults" action behind a confirmation.
6. Documentation sweep: `README.md` (features, screenshots placeholder, platform requirements, build
   and run, the token scopes per provider, the non-goals) and `RELEASENOTES.md` seeded with the
   initial entry.

## Acceptance criteria

- Unit tests: settings round-trip; an unknown future version falls back to defaults without data
  loss on the keys it does understand; a corrupt file is backed up and defaults load; saving is
  debounced (a burst of changes writes once); every property has a defined default.
- Changing the theme, the view mode, the diff mode and the graph metrics from the page takes effect
  without a restart (asserted through the ViewModel + a headless render pass).
- `dotnet build` clean with zero warnings, including zero `AVLN` warnings;
  `dotnet test --solution` fully green.
- README documents every shipped feature and both non-goals.

## Out of scope

- Localisation of the UI strings (follow-up; the app ships English).
- Installer/packaging (`dotnet-release` owns that at release time).

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| Settings storage | Versioned JSON under the user config directory | No dependency, human-readable, migratable, identical on both platforms | registry (Windows-only); `appsettings.json` in the install directory (not writable) |
| Corrupt settings | Back up and fall back to defaults | Never block startup on a file the user can delete, but never destroy it silently either | crash; overwrite without a backup |
| Theme default | Follow the OS | The least surprising default on both platforms, and the control library supports it | forcing Dark |
