# FEATURE-711F-PHASE02 — A repository list per profile

**Item:** FEATURE-711F — Repository lists per profile
**Branch:** `feature/feature-711f-phase02-profile-lists`
**Run:** feature/2026-09-30-profile-lists-readme-diffs

## Summary

The start window's list of repositories now belongs to a profile. There is always a profile to own
one.

- **A Default profile when there is none.** `IIdentityProfileStore.EnsureAnyAsync` saves
  `IdentityProfile.CreateDefault()` (id `default`, label "Default", no name or email, which PHASE01
  made legal) when the file has no profile, and returns the list. The fixed id means two instances
  starting together write the same profile, not two.
- **Which list is shown.** `IProfileSelection` (`Services/ProfileSelection.cs`) reads the profiles
  through `EnsureAnyAsync` and picks the one `AppSettings.SelectedProfileId` names, else the first.
  `Select(id)` records the choice. If the profiles file cannot be read or written, the Default
  profile stands in under its own id and the start window still works. It never touches git's
  identity.
- **The lists.** `RepositoryListStore` replaces `RecentRepositoryStore`: `ListedRepository(Path,
  Name)`, one file `repository-lists.json` (`{ version: 1, profiles: { "<id>": [ … ] } }`), and
  `GetAsync` / `AddAsync` / `RemoveAsync` / `RemoveProfileAsync`, keyed by profile. The order is the
  user's: a new repository is appended, a listed one keeps its place (and takes the new name), and
  nothing is capped. The gate, re-read before every write, atomic write, corrupt-file backup and
  newer-version tolerance are the same as before. `recent-repositories.json` is not read and is left
  where it is.
- **Pin is gone.** The button, `TogglePinCommand`, `SetPinnedAsync`, `IsPinned`, the pinned-first
  ordering and their tests.
- **Its users.** `RepositoryOpener` (the page's Open and the command line) adds to the selected
  profile's list, and so do create and clone. Forget removes from it. Deleting a profile on the
  Profiles page removes its list too, and the confirmation says so.

## Files / modules touched

**Created — App**

- `Services/ProfileSelection.cs` — `ProfileChoice`, `IProfileSelection`, `ProfileSelection`

**Renamed and rewritten — App**

- `Services/RecentRepositoryStore.cs` → `Services/RepositoryListStore.cs`

**Modified — Core**

- `Identity/IdentityProfile.cs` — `DefaultId`, `DefaultLabel`, `CreateDefault()`
- `Identity/IdentityProfileStore.cs` — `EnsureAnyAsync`
- `Configuration/AppSettings.cs` — `SelectedProfileId`, trimmed by `Normalised`

**Modified — App**

- `Services/RepositoryOpener.cs` — the list store and the selection instead of the recent store
- `ViewModels/Pages/RepositoriesPageViewModel.cs` — `Repositories` / `HasRepositories` /
  `ReloadListAsync` / `OpenListedCommand` / `ForgetCommand` (were `Recent` / `HasRecent` /
  `ReloadRecentAsync` / `OpenRecentCommand` / `ForgetRecentCommand`); `AddToListAsync`,
  `SelectedProfileIdAsync`; no pin
- `Views/Pages/RepositoriesPageView.axaml(.cs)` — the renamed bindings; no pin button
- `ViewModels/StartWindowViewModel.cs` — `ReloadListAsync`
- `ViewModels/Pages/ProfilesPageViewModel.cs` — takes `IRepositoryListStore`; deleting a profile drops
  its list; the confirmation's wording
- `ViewModels/Pages/SettingsPageViewModel.cs` — the reset confirmation says "your lists of repositories"
- `Navigation/StartNavigation.cs` — a doc comment
- `DependencyInjection/ServiceCollectionExtensions.cs` — the two services in place of the old store

**Tests**

- `RecentRepositoryStoreTests.cs` → `RepositoryListStoreTests.cs`: append order, a listed path keeps
  its place and takes its new name, an unnamed entry, no cap, remove keeps the order, per-profile
  isolation, `RemoveProfileAsync` (and nothing written when there is no list), restart, the old file
  ignored and left untouched, corrupt file, newer version, null and path-less entries, `Exists`, the
  file's shape, concurrency across profiles, two stores, no temporary files, profile id required
- new `ProfileSelectionTests.cs`: Default created once and selected, two instances leave one, the first
  selected until one is chosen, the choice remembered, a gone choice falls back, an unreadable file
  lets Default stand in, re-selecting changes nothing
- `Core.UnitTests/Identity/IdentityProfileStoreTests.cs`: `EnsureAnyAsync` creates Default, leaves
  profiles alone, and creates it again after the last one is gone
- `RepositoriesPageTests.cs`: the first start creates Default and shows its (empty) list; only the
  selected profile's repositories are shown; opening a listed repository again leaves it in place; the
  existing tests moved to the new API
- `ProfilesPageTests.cs`: deleting a profile takes its list and leaves the directory and the other
  profile's list; the failing store double implements `EnsureAnyAsync`
- `AboutDialogTests.cs`, `InstanceTests.cs`: the renamed API

**Docs**

- `README.md` — the start window's feature line, and the files table (`repository-lists.json`; profiles
  may have no name and email)
- `docs/roadmap.md`, `docs/plan/FEATURE-711F.md`

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| The page's member names | `Repositories`, `HasRepositories`, `ReloadListAsync`, `OpenListedCommand`, `ForgetCommand` | "Recent" is no longer true, and every caller changed anyway |
| How the page learns the profile | It asks `IProfileSelection` on every read and write | The profiles page runs a clone through this page before it has ever been shown. A cached id would be unset, or stale after the Profiles page deleted a profile |
| A profiles file that cannot be read | Default stands in, in memory, under its real id | The start window must still list something. Repositories added meanwhile land in Default's list, which is still the right one once the file is writable |
| When deleting the last profile recreates Default | The next time the start page or the opener reads the profiles | The Profiles page shows what the file holds. Recreating it there would make a delete look like it failed |
| A listed repository opened again | Keeps its place and takes the new name | The order is the user's (the plan). The name follows the directory, as it did before |
| The settings reset | Also resets `SelectedProfileId`, back to the first profile | A reset puts every remembered value back, `CloneParentDirectory` included. The lists themselves are untouched, as the reworded dialog says |

## Deviations & follow-ups

- **None from the plan.**
- Upgraders keep an unused `recent-repositories.json` in their configuration directory, as decided.
  A later cleanup release could delete it.
- Two instances racing on a machine with no profiles: the fixed id avoids duplicates when the writes
  are sequential (covered by a test). Truly simultaneous writes are still last-writer-wins on the same
  single profile, which is harmless.

## Build/test evidence

- `dotnet build Enigma.GitClient.slnx`: 0 warnings, 0 errors.
- `dotnet test --solution Enigma.GitClient.slnx`: 2554 passed, 0 failed (2536 before; old pin and cap
  tests removed, new ones added). Green on the first run, and again after the documentation edits.
