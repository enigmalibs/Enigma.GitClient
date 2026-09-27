# FEATURE-1406-PHASE02 — Accounts belong to a profile

**Item:** FEATURE-1406 — Profiles that own their integrations
**Phase:** PHASE02 — Accounts belong to a profile
**Branch:** `feature/feature-1406-phase02-accounts-in-profiles`
**Run:** feature/2026-09-27-diff-profiles-release

## Summary

A hosting account now records the identity profile it belongs to. The store can move an account to a
profile, and can disconnect every account of a profile. This is the Core half: nothing in the
interface uses it yet (PHASE04 does).

- `HostAccount.ProfileId` — `null` for an account that belongs to no profile. A blank id is normalised
  to `null`.
  - The constructor takes it as an optional `profileId`, so the JSON reader fills it from `profileId`
    and leaves it `null` when the field is absent.
  - `IsUnassigned`, `BelongsTo(profileId)` and `ForProfile(profileId)` go with it. `ForProfile`
    returns a copy with the same id, so the same token key: moving an account never moves its token.
  - `HostAccount.Create` takes the profile.
- `host-accounts.json` is written as **version 2**.
  - A version-1 file still loads, with every account unassigned.
  - A 1.x build reads a version-2 file and ignores the new field, since `System.Text.Json` skips unknown
    members.
- `IHostAccountService`:
  - `AssignAsync(accountId, profileId)` gives one account to a profile, under the store's lock and with
    its read-modify-write;
  - `RemoveForProfileAsync(profileId)` removes every account of a profile and then deletes their
    tokens, in the same order as `RemoveAsync`: the list is written first.

## Files / modules touched

**Modified**

- `src/Enigma.GitClient.Core/Hosting/HostModel.cs` — `HostAccount.ProfileId` and its helpers, and the
  remarks on what an account belongs to
- `src/Enigma.GitClient.Core/Hosting/HostAccountService.cs` — `CurrentVersion = 2`, documented;
  `AssignAsync`; `RemoveForProfileAsync`
- `tests/Enigma.GitClient.Core.UnitTests/Hosting/HostAccountServiceTests.cs` — six new tests:
  - the profile survives a restart, and the file says version 2;
  - a hand-written version-1 file reads as unassigned;
  - assigning moves one account and keeps its token;
  - assigning an unknown account says so;
  - removing a profile's accounts takes only those and their tokens;
  - `ForProfile` keeps everything else, and treats a blank id as none.
- `docs/roadmap.md`, `docs/plan/FEATURE-1406.md` — statuses

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| Where the profile lives | On the account (`profileId`), not on the profile | An account belongs to one profile. `identity-profiles.json` stays untouched |
| `AssignAsync` result | `bool`, like `RemoveAsync` | The caller only needs to know whether the account was there |
| A blank profile id | Normalised to `null` (unassigned) | A hand-edited `"profileId": ""` must not create a profile nobody can see |
| Unassigning (`AssignAsync(…, null)`) | Not offered | No step of the plan needs it. `ForProfile(null)` exists for the model |

## Deviations & follow-ups

- None from the plan.
- Documentation sweep: nothing user-visible changed. `README.md`'s *Where your things are kept* is
  updated in PHASE04, where accounts appear under their profiles.

## Build / test evidence

- `dotnet build Enigma.GitClient.slnx`: 0 warnings, 0 errors.
- `dotnet test --solution Enigma.GitClient.slnx`: **2245 passed**, 0 failed (2239 before, plus 6 new).
