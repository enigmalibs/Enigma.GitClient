# FEATURE-1E0D-PHASE01 — The profile keeps a base directory

**Item:** FEATURE-1E0D — A base directory per profile
**Branch:** `feature/feature-1e0d-phase01-profile-base-directory`
**Run:** feature/2026-10-05-revert-tree-basedir-release

## Summary

A profile can now name a **base directory**: `IdentityProfile.BaseDirectory`, kept in
`identity-profiles.json` beside the label, name and email (empty means none; a file written before has
no key and reads as none, so the store's version stays 1). It is set in the **Add / Edit profile**
dialog: an optional *Base directory* field with a *Browse* button — the picker starts on the field's
directory while it exists, else on the home folder — and a line saying what it is for. A relative or
multi-line path disables the dialog's primary button with a sentence; a directory that does not exist
(a drive unplugged) is accepted. PHASE02 is where the home page's buttons start there.

## Files / modules touched

**Modified — Core**

- `Identity/IdentityProfile.cs` — `BaseDirectory` (init, default empty); `With` trims it too;
  `WithBaseDirectory`; `IdentityProfileRules.ValidateBaseDirectory` (empty, or one line and fully
  qualified); `Validate` checks it last
- `Identity/IdentityProfileStore.cs` — a `null` base directory in a hand-edited file reads as none

**Modified — App**

- `ViewModels/Dialogs/IdentityProfileDialogViewModel.cs` — takes `IFolderDialogService` and the
  starting base directory; `BaseDirectory` with validation; `BrowseCommand`; `ToProfile` carries it
- `Views/Dialogs/IdentityProfileDialogView.axaml` — the *Base directory* field, *Browse*, and its hint
- `ViewModels/Pages/ProfilesPageViewModel.cs` — takes `IFolderDialogService`; the dialog starts with the
  edited profile's base directory

**Tests**

- `Core.UnitTests/Identity/IdentityProfileTests.cs` — none by default, trimmed, kept by `With`;
  validation accepts none, a full path and a missing one, refuses relative and two-line paths, and
  comes after the label (6 tests)
- `Core.UnitTests/Identity/IdentityProfileStoreTests.cs` — round-trips; an older file and a `null` read
  as none; a relative path is refused and nothing written (3 tests)
- `Desktop.UnitTests/ProfilesPageTests.cs` — adding stores it, trimmed; editing shows it and changes
  or clears it; a relative path disables the primary button; *Browse* starts on home, then on the
  field's directory, and a cancelled picker leaves the field (4 tests); the dialog's constructor
  takes the folder picker
- `Desktop.UnitTests/Infrastructure/UiServiceDoubles.cs` — `RecordingFolderDialogService`: records
  where each picker starts and answers with a folder the test chose, through a headless window's real
  storage provider (Avalonia 12 does not let a test implement one)
- `Desktop.UnitTests/Infrastructure/TestServices.cs` — the folder picker is replaced by the recording
  one in every test container, as the clipboard and the file manager are; `Folders` exposes it

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| How a test sees the folder picker | A recording `IFolderDialogService` in every test container, its storage provider a headless window's | The library's own tests use the same pattern; no test could raise the real picker anyway (it throws without a storage provider) |
| Invalid path characters | Refused with the same sentence as a relative path | Either way it is not a path a picker can start on |
| Where the field sits | After the identity's hint, with its own hint below | The identity fields stay together; the directory is a separate concern |
| Where *Browse* starts with an empty or missing directory | The home folder | Where the home page's dialogs start without one |

## Deviations & follow-ups

- None from the plan.
- Recommendation only: line endings were not examined; nothing in this diff showed CRLF churn.

## Documentation sweep

- Nothing made wrong by this phase: no doc describes the profile dialog's fields. README gains the base
  directory with PHASE02, where it changes what the home page does.

## Build/test evidence

- `dotnet build Enigma.GitClient.slnx`: 0 warnings, 0 errors.
- `dotnet test --solution Enigma.GitClient.slnx`: 2783 passed, 0 failed (13 new).
- Fix budget: 0 cycles used.
