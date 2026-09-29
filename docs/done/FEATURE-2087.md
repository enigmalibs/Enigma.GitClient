# FEATURE-2087 — Clone remembers its directory

**Item:** FEATURE-2087 — Clone remembers its directory
**Branch:** `feature/feature-2087-remember-clone-directory`
**Run:** feature/2026-09-29-rail-clone-history

## Summary

When a clone succeeds, its parent directory is saved in the settings file as a full path. The next
clone is suggested there instead of the home folder. That covers the *Clone a repository* dialog's
*Where* field and the clone started from a host's repository list, which has no dialog. If the saved
directory no longer exists, the home folder is suggested. A clone that fails or is cancelled saves
nothing. *Create a repository* keeps its home-folder default.

## Files / modules touched

**Created**

- `docs/done/FEATURE-2087.md`

**Modified**

- `src/Enigma.GitClient.Core/Configuration/AppSettings.cs` — `CloneParentDirectory` (empty by
  default) in a new *repositories* section; `Normalised()` trims it. No schema bump: an older file
  reads it as empty.
- `src/Enigma.GitClient.App/ViewModels/Pages/RepositoriesPageViewModel.cs`:
  - takes `ISettingsService`;
  - `CloneParentDirectory()` (public, instance) returns the saved directory while it exists, else
    the home default;
  - `RunCloneAsync` saves the directory right after `CloneAsync` returns;
  - the clone dialog opens on `CloneParentDirectory()`;
  - `DefaultParentDirectory()` is now private, since its only other caller moved to the new method.
- `src/Enigma.GitClient.App/ViewModels/Pages/ProfilesPageViewModel.cs` — the host-browser clone goes
  to `_repositories.CloneParentDirectory()`.
- `tests/Enigma.GitClient.Core.UnitTests/Configuration/SettingsServiceTests.cs` —
  `TheCloneDirectory_IsRememberedTrimmed_AndAnOlderFileHasNone`.
- `tests/Enigma.GitClient.App.UnitTests/RepositoriesPageTests.cs`:
  - `ACloneThatWorked_IsWhereTheNextCloneIsSuggested`: a real clone, then the next dialog's field;
  - `ACloneThatFailed_LeavesTheSuggestionWhereItWas`;
  - `ARememberedDirectoryThatIsGone_IsNotSuggested`;
  - a `SourceRepositoryAsync` helper.
- `tests/Enigma.GitClient.App.UnitTests/ProfileIntegrationsTests.cs` —
  `ARepositoryPickedInTheDialog_ClonesWhereTheLastCloneWent`.
- `docs/roadmap.md`, `docs/plan/FEATURE-2087.md` — statuses.

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| The exact moment it is saved | Right after `CloneAsync` returns, before the repository is opened | The clone has worked by then. If opening it fails later, that says nothing against the directory |
| The form it is saved in | `Path.GetFullPath` of the request's directory | A relative path typed in the dialog would otherwise mean something else the next time the application starts |
| `DefaultParentDirectory` | Made private | Its only outside caller, the profiles page, now uses `CloneParentDirectory()`. A public static left behind would invite the next clone path to skip the saved directory |
| Cancellation test | Not separately tested | A cancelled clone throws out of `CloneAsync` before the save, exactly like a failed one, which is tested |

## Deviations & follow-ups

- None from the plan.
- Follow-up, not new here: `SettingsService` writes 400 ms after the last change, and nothing flushes
  it at shutdown. A change in the last 400 ms before exit is lost, this one included, as with every
  other setting. After a clone the window changes, so in practice that doesn't happen here.
- Line endings: no CRLF churn.

## Documentation sweep

The README's "Create, clone and open repositories from a start window…" stays true. No edit.

## Build/test evidence

- `dotnet build Enigma.GitClient.slnx --no-incremental`: 0 warnings, 0 errors.
- `dotnet test --solution Enigma.GitClient.slnx`: **2524 passed**, 0 failed, 0 skipped (5 new).
- Fix budget: no fix cycle.
