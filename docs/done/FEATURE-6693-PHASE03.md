# FEATURE-6693-PHASE03 — Submodules follow the pin

**Item:** FEATURE-6693 — Submodule support
**Branch:** `feature/feature-6693-phase03-follow-pins`
**Run:** feature/2026-11-09-feature-6693

## Summary

Submodules now follow what the app does to the repository, and say when they are stale.

- **The follow step** (`ISubmoduleFollowStep`, Desktop) is in the one write path,
  `RepositoryContext.RunExclusiveAsync`: HEAD is noted before the write, and once it is over — before
  the refresh — `ISubmoduleService.FollowAsync(before, after)` (Core) puts on their new commits the
  submodules whose pin changed, plus the ones not downloaded; inside a submodule that moves, the nested
  ones follow what that move changed. A submodule moved on purpose stays. The card shows only when a
  submodule is actually updated, with Cancel; what was left behind is named in one warning. A write
  can opt out (`followSubmodules: false`); without `.gitmodules` nothing runs (the "before" HEAD is then
  the context's last-read one, no git process).
- **Rule 4:** `GitCommandFactory` adds `-c submodule.recurse=false` to `checkout`, `switch`, `pull`,
  `merge`, `reset`, `revert`, `cherry-pick`, `restore` and `read-tree`.
- **Setting:** `AppSettings.UpdateSubmodulesAutomatically` (default on; an old file reads on), with
  its switch in Settings → Git, under *Pull*.
- **Clone:** *Clone submodules as well* starts ticked.
- **Worktree:** a new worktree's submodules are downloaded (behind the card) before its window opens,
  when the setting is on.
- **The band** (`SubmoduleNoticeViewModel`, `MainWindow`): "2 submodules are not on the commit this
  repository expects." for *Not downloaded* / *Behind* ones, with *Update* (rule 1) and *Submodules…*;
  read after every refresh, only for a repository with `.gitmodules`.
- **Watcher:** `RepositoryLayout` classes a change under `<git dir>/modules/**/HEAD` or `…/refs/` as a
  working-tree change, so a commit made inside a submodule refreshes the parent.
- README *Features*: a "Submodules follow" bullet.

## Files/modules touched

- Created:
  - `src/Enigma.GitClient.Desktop/Services/SubmoduleFollowStep.cs`
  - `src/Enigma.GitClient.Desktop/ViewModels/SubmoduleNoticeViewModel.cs`
  - `tests/Enigma.GitClient.Core.IntegrationTests/Submodules/SubmoduleFollowTests.cs`
  - `tests/Enigma.GitClient.Desktop.UnitTests/SubmoduleFollowTests.cs`
  - `docs/done/FEATURE-6693-PHASE03.md`
- Modified:
  - `src/Enigma.GitClient.Core/Submodules/SubmoduleService.cs`, `Git/GitCommandFactory.cs`,
    `Configuration/AppSettings.cs`
  - `src/Enigma.GitClient.Desktop/Services/IRepositoryContext.cs`, `RepositoryContext.cs`,
    `SubmoduleOperations.cs`, `WorktreeOperations.cs`, `RepositoryLayout.cs`
  - `src/Enigma.GitClient.Desktop/ViewModels/MainWindowViewModel.cs`, `Views/MainWindow.axaml`
  - `src/Enigma.GitClient.Desktop/ViewModels/Pages/SettingsPageViewModel.cs`,
    `Views/Pages/SettingsPageView.axaml`
  - `src/Enigma.GitClient.Desktop/ViewModels/Dialogs/CloneRepositoryDialogViewModel.cs`
  - `src/Enigma.GitClient.Desktop/DependencyInjection/ServiceCollectionExtensions.cs`
  - `tests/Enigma.GitClient.Core.UnitTests/Git/GitCommandFactoryTests.cs`,
    `Configuration/SettingsServiceTests.cs`
  - `tests/Enigma.GitClient.Desktop.UnitTests/RepositoryLayoutTests.cs`,
    `Infrastructure/FakeSubmoduleService.cs`
  - `README.md`; `docs/roadmap.md`, `docs/plan/FEATURE-6693.md`: statuses.

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| Where rule 4 lives | `GitCommandFactory`, by verb | One place, like the rebase refusal: a command added later that moves HEAD cannot forget it |
| Which writes follow | Every `RunExclusiveAsync` write; HEAD unmoved → nothing | Covers checkout, pull, merge, finishing a conflicted merge (a commit), reset, revert and squash with no list to keep; commits included, since finishing a merge is one |
| "Before" without `.gitmodules` | The context's last-read HEAD | Rule 5 (no git), yet a checkout that brings a `.gitmodules` still has a "before" to compare |
| Nested submodules in a follow | Only those the parent's move changed, plus not downloaded | Rule 2 applied at every level; `UpdateAsync` keeps "all nested" for an explicit update |
| A follow describes | Only the moved submodules and the empty ones (a folder check) | Keeps a follow in a repository with many submodules to a few git processes |
| The card | Shown on the first progress report, closed at the end, a late report ignored | A write that moved no submodule says nothing; a `Progress<T>` report arriving late must not reopen a card nothing closes |
| The band's read | After every refresh, only with `.gitmodules`, one read at a time | "Whatever the cause" means after a terminal pull too; serialised so refreshes in a burst read once more, not N times |
| The switch's text | Inline in the XAML, like *Watch the repository* | `SettingsDescriptions` holds the dropdowns' item names; the toggles' sentences are in the view |

## Deviations & follow-ups

- **"One InfoBar … with *Submodules…*":** the library's `InfoBar` has no action button, so the warning
  ends with "Update them from Submodules in the toolbar" instead. The band covers the *Behind* /
  *Not downloaded* cases with real buttons.
- `WorktreeOperations` and `RepositoryContext` take their new dependency as an optional last
  constructor parameter, so the tests that build them directly compile unchanged.
- "Setting off → nothing moves" is a Desktop test (the setting is Desktop's), not an integration test;
  the Core side is covered by the four follow integration tests.
- Pre-existing, unchanged: the Windows-only `TokenStoreTests` failure (see the PHASE01 record).

## Build/test evidence

- **Baseline:** see `docs/done/FEATURE-6693-PHASE01.md`.
- **This dev:**
  - `dotnet build Enigma.GitClient.slnx`: 0 warnings, 0 errors.
  - Core unit: 1473 run, 1470 passed, 1 failed (the baseline's), 2 skipped.
  - Core integration: 436 run, 434 passed, 0 failed, 2 skipped.
  - Desktop: **compiled, not run** (this machine's standing rule): `SubmoduleFollowTests` (11), the new
    `RepositoryLayoutTests` cases (3 methods).
- **New tests:** Core integration — after a pull that moves a pin the submodule is on the new commit
  and one moved on purpose stays; checking out a branch that adds a submodule downloads it; with
  `submodule.recurse=true` the checkout leaves the submodule and the follow moves it; a moved submodule
  takes the nested pin its move changed; `ReadHeadAsync`. Core unit — `submodule.recurse=false` on the
  nine verbs and not on others; the setting's default, round trip and an old file. Desktop — the step
  follows from before to after, not when HEAD did not move, not with the setting off, not when a write
  opts out, runs nothing without `.gitmodules`, warns about what it left; the band's rules, *Update*
  and the singular; the clone box ticked; the settings switch.
- **Fix budget:** 0 cycles.

## Documentation sweep

- README *Features*: the new bullet (plan: "later phases extend it").
