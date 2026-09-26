# FEATURE-6151-PHASE03 — Identity profiles

**Item:** FEATURE-6151 — Git identity: global, profiles, local
**Branch:** `feature/feature-6151-phase03-identity-profiles`
**Run:** feature/2026-09-26-git-identity-profiles

## Summary

Identity profiles — a label, a name and an email each — are kept by the client and switch the global
identity in one click.

- **Core.** `IdentityProfile` (record: id, label, name, email; `Create`, `With`, `Matches`) and
  `IdentityProfileRules` (label: required, one line, at most 100 characters; then the identity rules).
  `IIdentityProfileStore` / `IdentityProfileStore` keep them in `identity-profiles.json`
  (`{ version: 1, profiles: [...] }`) in the configuration directory: every change re-reads the file
  and writes it atomically, so two instances do not lose each other's profiles; an unusable profile is
  refused before anything is written; an unreadable file is moved aside as
  `identity-profiles.corrupt-<stamp>.json` instead of being overwritten by the next save; a
  hand-edited entry without an id is skipped.
- **The page.** A **Profiles** card under the global identity lists the profiles in the order they
  were added: the label, `Name <email>`, a *Current* pill on the profile matching the identity git has
  (worked out on every load and after every write — nothing stored), and the row's actions — **Use**
  (not shown on the current profile), edit, delete. **Add a profile** opens a dialog that starts from
  the global identity; edit opens it on the profile; the dialog's primary button follows the
  validation, and its sentence says nothing until something is typed. Delete asks first and never
  touches git's configuration. Use writes the profile's identity to the global configuration, shows
  it in the global fields and moves *Current*. File failures are reported in the info bar.

## Files / modules touched

**Created — Core**

- `Identity/IdentityProfile.cs` — `IdentityProfile`, `IdentityProfileRules`
- `Identity/IdentityProfileStore.cs` — `IIdentityProfileStore`, `IdentityProfileStore`

**Created — App**

- `ViewModels/Dialogs/IdentityProfileDialogViewModel.cs` — the dialog's fields and validation
- `Views/Dialogs/IdentityProfileDialogView.axaml` / `.axaml.cs` — the dialog's body

**Modified**

- `Core/DependencyInjection/ServiceCollectionExtensions.cs` — registers `IIdentityProfileStore`
- `App/ViewModels/Pages/IdentityPageViewModel.cs` — `IdentityProfileRowViewModel`; the profiles,
  `CurrentProfile`, the add/edit/remove/use commands; global writes shared by Save and Use
- `App/Views/Pages/IdentityPageView.axaml` — the Profiles card and its row template
- `App/DependencyInjection/ServiceCollectionExtensions.cs` — the dialog view (transient)

**Docs**

- `README.md` — the feature line mentions profiles; *Where your things are kept* lists
  `identity-profiles.json` and says using a profile writes git's configuration (sweep)
- `RELEASENOTES.md` — profiles under *Your git identity* (sweep)

**Tests**

- `Core.UnitTests/Identity/IdentityProfileTests.cs` — create, with, matches (email case, incomplete
  identities), the label rules, whole-profile validation
- `Core.UnitTests/Identity/IdentityProfileStoreTests.cs` — no file, round trip in order, replace in
  place, trimming, unusable profiles refused with nothing written, id required, remove, the file's
  JSON shape, a corrupt file moved aside and not overwritten, entries without an id skipped, two
  stores on one file
- `App.UnitTests/IdentityPageTests.cs` — add (starts from the global identity, marked current, git
  untouched), the dialog's primary button follows validation, cancel adds nothing, edit keeps place
  and id, delete asks first and leaves git alone, use sets the global identity and moves *Current*,
  *Current* follows an identity saved by hand, a failed use changes nothing, a store that cannot read
  or write is reported, the dialog ViewModel, and the realised view's pill and Use buttons

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| The list control | An `ItemsControl` with a local style that drops the toolbar frame from its row actions | Selecting a profile means nothing, so a `ListBox`'s selection would be noise; the house rule keeps a list row's actions frameless |
| Use on the current profile | Hidden; the *Current* pill stands in its place | Using what is already in use does nothing worth a button |
| Several profiles with the same identity | All marked *Current*; `CurrentProfile` is the first | Both are true; the copy button (PHASE04) needs one |
| Profiles with an incomplete identity (hand-edited) | Listed, never *Current* | `Matches` refuses an incomplete identity, so a broken entry cannot claim git's |
| File errors on read | Reported; the global identity is still read and shown | The two sections do not depend on each other |
| Store failures on save | Caught as `IOException`, `UnauthorizedAccessException`, `ArgumentException` and reported | The store is the last check, and the file system can refuse |
| Where the dialog starts on Add | The identity git has (`GlobalIdentity`), not unsaved typing | "Save my identity as a profile" is the usual first step; unsaved typing may be half-done |

## Deviations & follow-ups

- None from the plan.
- Follow-up: a corrupt file that also cannot be moved aside (permissions) is overwritten by the next
  save, as `host-accounts.json` would be; the failure to move it is logged.
- The page was rendered headlessly in both themes with a throwaway test (not committed) to check the
  Profiles card.
- Recommendation only: line endings were not examined; nothing in this diff showed CRLF churn.

## Documentation sweep

- `README.md` — feature line; the files table (`identity-profiles.json`, placed after the tokens row so
  "Those tokens" still refers to the accounts); the sentence on what is written outside the directory.
- `RELEASENOTES.md` — profiles.

## Build/test evidence

- `dotnet build Enigma.GitClient.slnx --no-incremental`: 0 warnings, 0 errors.
- `dotnet test --solution Enigma.GitClient.slnx`: 2092 passed, 0 failed (33 new), first run.
