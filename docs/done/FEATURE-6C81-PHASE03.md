# FEATURE-6C81-PHASE03 — Clone signs in

**Item:** FEATURE-6C81 — Git signs in with the profile's token
**Phase:** PHASE03 — Clone signs in
**Branch:** `feature/feature-6c81-phase03-clone-signs-in`
**Run:** feature/2026-09-27-profile-token-git-auth

## Summary

A clone now signs in with an integration's token.

- **Which integration: `IGitCredentialResolver.ForCloneAsync(url, account)`.**
  - `CloneRequest.Account` set → that integration, when it owns the URL (`ProfileCredentialRule.ForAccount`).
    The profile does not matter here: the user picked the repository from that integration's own
    listing.
  - No account → the current profile's integration that owns the URL. The current profile is the
    one matching the **global** identity, since a clone has no repository identity yet
    (`ProfileCredentialRule.ForProfile`).
  - Otherwise, none.
  - The same rules as the repository case apply: SSH never signs in, no `https` → `http` downgrade,
    and a read failure fails open with a warning.
- **The clone:** `RepositoryService.CloneAsync` resolves the credentials after validating the request
  and runs `git clone` `WithCredentials(...)`. Global `-c` options are not persisted by `git clone`
  (only `git clone --config` is), so nothing is written into the new repository. A test proves it.
- **The Profiles page:** *Browse repositories* → *Clone* passes the row's account as
  `CloneRequest.Account`.
- **Submodules:** git hands `-c` options to the gits it starts, through `GIT_CONFIG_PARAMETERS`, and
  they inherit the environment. So a `--recurse-submodules` clone signs its submodules on the same
  origin in too.

## Files / modules touched

**Modified**

- `src/Enigma.GitClient.Core/Repositories/CloneRequest.cs` — `Account`
- `src/Enigma.GitClient.Core/Hosting/GitCredentialResolver.cs` — `ForCloneAsync`
- `src/Enigma.GitClient.Core/Repositories/RepositoryService.cs` — the resolver in the constructor; the
  clone signed in
- `src/Enigma.GitClient.App/ViewModels/Pages/ProfilesPageViewModel.cs` — the browsed clone names its
  account
- Tests:
  - `tests/Enigma.GitClient.Core.IntegrationTests/Hosting/GitCredentialResolverTests.cs` — 4 clone
    cases:
    - a picked integration signs in whatever the identity;
    - a picked integration of another host signs nothing in;
    - a URL signs in with the current profile's integration, and not over SSH;
    - with no current profile (only the repository's identity matches), nothing.
  - `tests/Enigma.GitClient.Core.IntegrationTests/Repositories/RepositoryServiceTests.cs` — a clone
    asks the resolver with its URL and account, and the new repository's `config` holds no credential
    and no token.
  - `tests/Enigma.GitClient.Core.IntegrationTests/Infrastructure/CoreTestHost.cs` — `CreateWithServices`,
    to stand a double in for one service.
  - `tests/Enigma.GitClient.App.UnitTests/ProfileIntegrationsTests.cs` — a repository picked in the
    dialog is cloned with the integration it was listed with.
  - `tests/Enigma.GitClient.Core.UnitTests/Sync/SyncProgressDeliveryTests.cs`,
    `Hosting/SignedInOperationsTests.cs` — stubs implement `ForCloneAsync`.
- `README.md` — docs sweep: *Connecting a host* says a clone signs in too, and with which integration.
- `docs/roadmap.md`, `docs/plan/FEATURE-6C81.md` — statuses

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| A picked integration from another profile than the current one | Signs in anyway | The user chose it by browsing its listing; the profile only decides when nothing was chosen |
| When the credentials are resolved | After the request is validated, before any directory is created | A refused URL reads nothing; a read failure leaves no half-made folder |
| The Clone dialog (a typed URL) | No account, so the current profile's integration | The dialog names no integration; the resolver covers it |
| Proving nothing is persisted | A real clone with a stub resolver, then reading `.git/config` | The spec's "never in `.git/config`" is git's behaviour, and worth a regression test |

## Deviations & follow-ups

- None from the plan.
- Follow-up idea, not done: the Clone dialog could let the user pick the integration explicitly when
  the profile has several on one host.
- Documentation sweep: `README.md`, as listed above.

## Build / test evidence

- `dotnet build Enigma.GitClient.slnx`: 0 warnings, 0 errors, on the first build.
- `dotnet test --solution Enigma.GitClient.slnx`: **2348 passed**, 0 failed, on the first run. That is
  2342 before, plus 6 new (4 resolver, 1 clone service, 1 App).
