# FEATURE-1406-PHASE01 — Identity becomes Profiles

**Item:** FEATURE-1406 — Profiles that own their integrations
**Phase:** PHASE01 — Identity becomes Profiles
**Branch:** `feature/feature-1406-phase01-profiles-page`
**Run:** feature/2026-09-27-diff-profiles-release

## Summary

The Identity page is now the **Profiles** page, in the view and in the code:

- the rail item on both windows reads *Profiles*, and so does the page's header (it said *Git identity*);
- the page's types, files and navigation values are renamed.

The page does exactly what it did before. The integrations move onto it in PHASE04.

| Before | After |
|---|---|
| `IdentityPageViewModel` (`ViewModels/Pages/IdentityPageViewModel.cs`) | `ProfilesPageViewModel` (`ProfilesPageViewModel.cs`) |
| `IdentityProfileRowViewModel` | `ProfileRowViewModel` |
| `IdentityPageView` (`Views/Pages/IdentityPageView.axaml(.cs)`) | `ProfilesPageView` (`ProfilesPageView.axaml(.cs)`) |
| `ShellPage.Identity`, `StartPage.Identity` | `ShellPage.Profiles`, `StartPage.Profiles` |
| rail header `Identity`, title `Git identity` | `Profiles`, `Profiles` |
| `IdentityPageTests` | `ProfilesPageTests` |

## Files / modules touched

**Renamed** (with `git mv`, so history follows)

- `src/Enigma.GitClient.App/ViewModels/Pages/IdentityPageViewModel.cs` → `ProfilesPageViewModel.cs`
- `src/Enigma.GitClient.App/Views/Pages/IdentityPageView.axaml` → `ProfilesPageView.axaml`
- `src/Enigma.GitClient.App/Views/Pages/IdentityPageView.axaml.cs` → `ProfilesPageView.axaml.cs`
- `tests/Enigma.GitClient.App.UnitTests/IdentityPageTests.cs` → `ProfilesPageTests.cs`

**Modified**

- `src/Enigma.GitClient.App/Navigation/ShellNavigation.cs`, `StartNavigation.cs` — the enum member, its
  summary, and the rail header
- `src/Enigma.GitClient.App/DependencyInjection/ServiceCollectionExtensions.cs` — the registrations
- `src/Enigma.GitClient.App/ViewModels/StartWindowViewModel.cs`, `Views/StartWindow.axaml(.cs)` —
  comments naming the page
- `tests/…/AppWindowsTests.cs`, `MainWindowShellTests.cs`, `CompositionRootTests.cs`,
  `ShellRenderTests.cs` — the new types and headers
- `README.md` — the feature bullet and *Where your things are kept* name the Profiles page (docs sweep)
- `docs/roadmap.md`, `docs/plan/FEATURE-1406.md` — statuses

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| The profile dialog (`IdentityProfileDialogViewModel/View`) | Kept its name | It edits an `IdentityProfile`, the Core type, which keeps its name (plan) |
| Test and message wording about the *git identity* | Kept | They describe git's identity, not the page |
| Old release notes that say *Identity page* | Not edited | They describe what 1.0.0 shipped |

## Deviations & follow-ups

- None from the plan.
- Documentation sweep: `README.md`, two places (listed above).

## Build / test evidence

- `dotnet build Enigma.GitClient.slnx`: 0 warnings, 0 errors.
- `dotnet test --solution Enigma.GitClient.slnx`: **2239 passed**, 0 failed. The count is unchanged:
  this is a rename, and every renamed test still runs.
