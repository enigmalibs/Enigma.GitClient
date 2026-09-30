# FEATURE-711F-PHASE01 — Profiles without a name and email

**Item:** FEATURE-711F — Repository lists per profile
**Branch:** `feature/feature-711f-phase01-identityless-profiles`
**Run:** feature/2026-09-30-profile-lists-readme-diffs

## Summary

A profile may now exist with a label and no name and email. PHASE02 creates a "Default" profile when
there is none, and that profile must not carry git's identity. A profile matching git's identity with
no integration refuses every push (`ProfilePushRule`), so a Default copied from the global identity
would silently stop every existing user from pushing.

- `IdentityProfile.HasIdentity`: the profile sets a name or an email.
- `IdentityProfileRules.ValidateIdentity`: no identity at all is accepted; half of one (a name without
  an email, or the reverse) or an invalid one is refused as before. `Validate` checks the label, then
  this.
- An identity-less profile never matches git's identity (`Matches` already required a complete one),
  so it is never current, never decides a push and never signs git in. Nothing in `ProfilePushRule`,
  `ProfileCredentialRule` or `GitCredentialResolver` changed.
- Profiles page: such a row reads "No name or email", its Use button is hidden (`CanUse`), and
  `OnUseProfileAsync` refuses it, since writing an empty identity would unset git's own. The "Profile
  added / saved" message uses the same wording.
- The profile dialog accepts both fields empty and says what such a profile is: "Leave both empty for
  a profile git never sees: commits, pushes and sign-ins go on as if it did not exist."

## Files / modules touched

**Modified — Core**

- `Identity/IdentityProfile.cs` — `HasIdentity`; `IdentityProfileRules.ValidateIdentity`; `Validate`
  uses it

**Modified — App**

- `ViewModels/Pages/ProfilesPageViewModel.cs` — `ProfileRowViewModel.Summary` via `Describe`, `CanUse`
  (raised with `IsCurrent`); the Use guard; the save message
- `Views/Pages/ProfilesPageView.axaml` — the Use button binds `CanUse`
- `ViewModels/Dialogs/IdentityProfileDialogViewModel.cs` — validation through `ValidateIdentity`
- `Views/Dialogs/IdentityProfileDialogView.axaml` — the explanatory line

**Modified — tests**

- `Core.UnitTests/Identity/IdentityProfileTests.cs` — the label-then-identity test now uses a
  half-identity (an empty one is legal now); new: empty accepted, half refused (theory), none or
  whole, an identity-less profile never matches
- `Core.UnitTests/Identity/IdentityProfileStoreTests.cs` — an identity-less profile is stored trimmed
  and read back; an email-less profile joins the refused cases
- `App.UnitTests/ProfilesPageTests.cs` — an identity-less row is never current, offers no Use, and Use
  writes nothing; the dialog accepts both fields empty but not one of them

**Modified — docs**

- `docs/roadmap.md`, `docs/plan/FEATURE-711F.md`

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| Where "none or whole" lives | A new `IdentityProfileRules.ValidateIdentity`, used by `Validate` and by the dialog | One rule for the store and the dialog. `GitIdentityRules.Validate` stays strict, because the global and repository identities on the same page must still be whole |
| What the row says | "No name or email" | An empty summary line reads as a rendering bug |
| The Use button | Hidden, like it is for the current profile, rather than disabled | The same treatment as "there is nothing to switch to", and the button would do nothing |
| `IsNotCurrent` | Kept public, now read by `CanUse` | Less churn; it still states a fact the view could use |

## Deviations & follow-ups

- **None from the plan.**
- An identity-less profile can still be connected to integrations. Browsing and cloning through the
  account work, but pushes and sign-ins, which go through the matching profile, never use it, as the
  dialog's line says. Worth revisiting if users connect accounts to "Default" and expect them to sign in.
- The roadmap diff is large because `IN PROGRESS` widens the Status column and the whole table is
  re-padded, as the house format requires.

## Build/test evidence

- `dotnet build Enigma.GitClient.slnx`: 0 warnings, 0 errors.
- `dotnet test --solution Enigma.GitClient.slnx`: 2536 passed, 0 failed (2526 before, +10).
- One fix cycle: a new theory row used `not an address`, which hits the "no spaces" rule before the
  "@" rule. The data was corrected to `nope`.
