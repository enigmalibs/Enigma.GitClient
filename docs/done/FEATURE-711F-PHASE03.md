# FEATURE-711F-PHASE03 — The profile picker on the home page

**Item:** FEATURE-711F — Repository lists per profile
**Branch:** `feature/feature-711f-phase03-profile-picker`
**Run:** feature/2026-09-30-profile-lists-readme-diffs

## Summary

The repositories page has a combobox of the profiles in its header, after the "Repositories" title.
Picking a profile shows its list and remembers the choice (`IProfileSelection.Select`, stored as
`AppSettings.SelectedProfileId`), so the next start opens on it. Git's identity is untouched.

- `RepositoriesPageViewModel.Profiles` and `SelectedProfile` (two-way). A pick from the user records
  the choice and loads that profile's list. If a quicker second pick lands first, the slower read is
  dropped instead of overwriting it.
- `ReloadListAsync` (every showing of the page, and the start window's return from a repository)
  re-reads the profiles, so one added, renamed or deleted on the Profiles page shows up. A selected
  profile that is gone falls back to the first.
- Putting the profiles and the selection back into the picker is never taken for a user choice
  (`_showingProfiles`). That covers the null the `ComboBox` pushes while its items are replaced. The
  items are only replaced when they changed, so the picker does not flicker on every showing.

## Files / modules touched

**Modified — App**

- `ViewModels/Pages/RepositoriesPageViewModel.cs` — `Profiles`, `SelectedProfile`, `ShowProfiles`,
  `ShowListOfAsync`; `ReloadListAsync` fills the picker
- `Views/Pages/RepositoriesPageView.axaml` — the `ProfilePicker` combobox (profile labels, tooltip and
  automation name "The profile whose repositories are listed")

**Modified — tests**

- `RepositoriesPageTests.cs` — the picker lists every profile with the remembered one selected; a pick
  shows its list, is stored, and a fresh page opens on it; a later showing picks up a rename, a delete
  (falling back to the first profile) and an addition; a repository opened while a profile is picked
  joins that profile's list only; the combobox is in the rendered header with the profile selected.
  Adds a `WaitUntilAsync` helper, as other test classes have.

**Modified — docs**

- `README.md` — the start window's line says where the profile is picked
- `docs/roadmap.md`, `docs/plan/FEATURE-711F.md`

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| Where the picker sits | In the header's title group, right after "Repositories" | It qualifies the title ("Repositories of Work"); the right-hand group is actions |
| What the picker shows | The label only | The picker chooses a list, not an identity; the name and email are the Profiles page's |
| Loading a pick's list from the setter | A discarded task (`_ = ShowListOfAsync(id)`), with a guard for stale results | The house pattern for a property-triggered load (`ProfilesPageViewModel.OnRepositoryChanged`). A command adds nothing for a combobox |
| Where adds and forgets go | Still asked of `IProfileSelection` on each write | The pick is recorded in the settings before the list loads, so the service already agrees with the picker. It is also right when the page was never shown (a clone from the Profiles page) |

## Deviations & follow-ups

- **None from the plan.**
- The picker does not follow the Profiles page live while both are open in different windows. The
  start window shows one page at a time, and the picker re-reads on every showing.

## Build/test evidence

- `dotnet build Enigma.GitClient.slnx`: 0 warnings, 0 errors.
- `dotnet test --solution Enigma.GitClient.slnx`: 2559 passed, 0 failed (+5).
- One fix cycle: the first build failed on the `Enigma.Avalonia` / `Avalonia` namespace collision in
  a test (`Avalonia.Automation` written inline). It was fixed with a file-scope `using`.
