# FEATURE-1E0D-PHASE02 — Open, clone and create start there

**Item:** FEATURE-1E0D — A base directory per profile
**Branch:** `feature/feature-1e0d-phase02-buttons-use-base-directory`
**Run:** feature/2026-10-05-revert-tree-basedir-release

## Summary

With the selected profile naming a base directory, the home page's three buttons start there:
**Open** raises its folder picker in it, and **Clone** and **Create** open their dialogs with it in the
*Where* field — so their *Browse* pickers start there too. For a clone it comes first, before the
directory the last clone went to (FEATURE-2087), which still serves a profile that names none; the
home folder comes last. A clone picked from a host's repository list goes to the base directory of the
profile the **integration belongs to**, whichever profile the home page has selected. A base directory
that is not a full path to an existing directory — a drive unplugged, a folder removed, a hand-edited
relative path — counts as none, and every button behaves as before.

The profile is read from the store each time a button is pressed, so a base directory just changed on
the profiles page (or in another window) is the one used.

## Files / modules touched

**Modified — App**

- `ViewModels/Pages/RepositoriesPageViewModel.cs` — `BaseDirectoryOf(profile)`;
  `CloneParentDirectory(IdentityProfile? profile = null)` (the profile's base directory, then the
  remembered one, then home); Open, Clone and Create read the selected profile now
  (`SelectedProfileNowAsync`) and start on its base directory
- `ViewModels/Pages/ProfilesPageViewModel.cs` — the host-browser clone passes the integration's own
  profile

**Modified — docs**

- `README.md` — the start window's line says a profile can name a base directory (documentation sweep)

**Tests**

- `Desktop.UnitTests/RepositoriesPageTests.cs` — Open's picker starts in the base directory, and
  without one is given no start; the clone and create dialogs open on it; it comes before the
  remembered clone directory, which still serves a profile without one; a base directory that is gone
  is like none (Open, Clone, Create); picking another profile moves where Open starts (6 tests)
- `Desktop.UnitTests/ProfileIntegrationsTests.cs` — a host clone goes to its integration's profile's
  base directory, ahead of the remembered one and of the home page's selected profile (1 test)

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| Which profile the buttons use | The one in use, read from the store when the button is pressed | The picker's in-memory copy could predate an edit made on the profiles page or in another window |
| `CloneParentDirectory`'s shape | One method with an optional profile; `null` means no profile, not "the selected one" | The host-browser clone has its own profile, or none for an earlier integration — it must not pick up the home page's |
| A hand-edited relative base directory | Treated as none (`Path.IsPathFullyQualified`) | The dialog refuses one, but a file can still hold one; never resolve it against the process's working directory |

## Deviations & follow-ups

- None from the plan.
- Observed once while running the suite: `ProfileSelectionTests.SelectingTheSameProfileAgainChangesNothing`
  (an existing test this dev does not touch) failed in one full run and passed alone and in the next two
  full runs. It counts the settings' `Changed` events; a reload of the settings file racing the first
  write is the likely cause. Worth a look on its own.
- Recommendation only: line endings were not examined; nothing in this diff showed CRLF churn.

## Documentation sweep

- `README.md`: the start window's line names the base directory and where it applies.

## Build/test evidence

- `dotnet build Enigma.GitClient.slnx`: 0 warnings, 0 errors.
- `dotnet test --solution Enigma.GitClient.slnx`: 2790 passed, 0 failed (7 new) — twice in a row after
  the one flaky run above (2789 passed, 1 failed, unrelated).
- Fix budget: 0 cycles used (no fix was needed: the re-runs changed no code).
