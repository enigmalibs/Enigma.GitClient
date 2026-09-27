# FEATURE-6C81-PHASE02 — Sync and remote pushes sign in

**Item:** FEATURE-6C81 — Git signs in with the profile's token
**Phase:** PHASE02 — Sync and remote pushes sign in
**Branch:** `feature/feature-6c81-phase02-sync-signs-in`
**Run:** feature/2026-09-27-profile-token-git-auth

## Summary

Every network operation run in a repository now signs in with the token of the profile's
integration. This covers fetch, pull, a branch's fast-forward and push (so the toolbar, the quiet
background fetch and the Changes page's fetch too), pushing or deleting a tag on a remote, and
deleting a remote branch.

- **Which integration: `ProfileCredentialRule`** (pure).
  - An address gets a login when it is HTTP(S) and an integration of the profile owns its host
    (`HostAccount.Owns`).
  - The login is keyed by the address's own origin, port included.
  - The first integration found for an origin answers for it.
  - SSH, scp-like and local addresses never get one.
  - An `https` integration's token never goes to an `http` address. An `http` integration signs in to
    its own `http` instance.
  - `ForProfile` and `ForAccount` answer for one address. PHASE03's clone uses them.
- **What is read: `GitCredentialResolver.ForRepositoryAsync`** (`IGitCredentialResolver`, a singleton
  in `AddGitClientCore`).
  - The steps:
    1. It reads the profiles. With none, it stops there.
    2. It reads the identity git commits with in the repository. The profile is
       `IdentityProfile.FirstMatching` (now shared with `ProfilePushRule`).
    3. It reads the remotes' fetch and push URLs and the accounts, and applies the rule.
    4. It reads each chosen integration's token, and takes the user name from its provider.
  - It fails open. An I/O, git or token-store failure gives `GitCredentials.None` and a warning, and
    git keeps its own credentials.
  - An integration without a usable token is skipped, with a warning naming its id and the origin.
  - What signs in is logged at debug, by origin and account id.
- **Where.** The network commands run `WithCredentials(...)`:
  - `SyncService.RunAsync` (fetch, pull, fast-forward, push);
  - `TagService.PushAsync` and `DeleteRemoteAsync`, through a new `RunOnRemoteAsync`;
  - `BranchService.DeleteRemoteAsync`.

  Local operations never ask for a login.

## Files / modules touched

**Created**

- `src/Enigma.GitClient.Core/Hosting/ProfileCredentialRule.cs` — `HostSignIn`, `ProfileCredentialRule`
- `src/Enigma.GitClient.Core/Hosting/GitCredentialResolver.cs` — `IGitCredentialResolver`,
  `GitCredentialResolver`
- `tests/Enigma.GitClient.Core.UnitTests/Hosting/ProfileCredentialRuleTests.cs` — 19 cases:
  - a profile without integrations;
  - an HTTPS remote owned by the profile's integration;
  - SSH, scp-like, SSH-over-443, local and empty addresses;
  - another profile's integration, an unassigned one, another instance;
  - two integrations on one origin; two hosts, in remote order; fetch and push URLs deduplicated;
  - `https` → `http` refused; an `http` instance;
  - the origin's port; Azure DevOps organisations;
  - `ForAccount`; `FirstMatching`.
- `tests/Enigma.GitClient.Core.UnitTests/Hosting/SignedInOperationsTests.cs` — 5 tests:
  - fetch, pull, fast-forward and push carry the resolver's login (the reset first, the token in the
    environment only);
  - with none, the vector is exactly the factory's and the environment is empty;
  - pushing and deleting a tag on a remote sign in; a local tag delete asks for nothing;
  - deleting a remote branch signs in.
- `tests/Enigma.GitClient.Core.IntegrationTests/Hosting/GitCredentialResolverTests.cs` — 7 tests,
  against real profiles, accounts, token store and git config:
  - no profiles; an HTTPS remote under the profile's integration; an identity no profile matches;
  - an SSH remote;
  - a push URL on another host getting its own integration;
  - a corrupt token store giving none instead of failing;
  - the resolver's output handed to a real `git credential fill` answering with the token.

**Modified**

- `src/Enigma.GitClient.Core/Sync/SyncService.cs`, `Tags/TagService.cs`, `Branches/BranchService.cs` —
  the resolver in the constructor; network commands signed in
- `src/Enigma.GitClient.Core/Identity/IdentityProfile.cs` — `FirstMatching`
- `src/Enigma.GitClient.Core/Hosting/ProfilePushRule.cs` — uses `FirstMatching`; its remark no longer
  says git signs in with its own credentials
- `src/Enigma.GitClient.Core/DependencyInjection/ServiceCollectionExtensions.cs` — the resolver
- Tests:
  - `tests/Enigma.GitClient.Core.UnitTests/Sync/SyncProgressDeliveryTests.cs` — a `NoCredentials`
    stub.
  - `tests/Enigma.GitClient.Core.IntegrationTests/Infrastructure/CoreTestHostFactory.cs` — the
    integration hosts get a configuration directory inside the workspace instead of the developer's
    own.
- `README.md` — docs sweep:
  - *Connecting a host* no longer says git signs in with its own credential helper. A new paragraph
    says the integration's token signs git in over HTTPS, how it is handed over, and what keeps git's
    own credentials.
  - *Requirements* says an integration's token takes over HTTPS sign-in to its own host only.
- `docs/roadmap.md`, `docs/plan/FEATURE-6C81.md` — statuses

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| The rule's input | The matched profile, not every profile and the identity | The resolver matches first, so an identity no profile matches reads no remotes and no accounts |
| Shared profile matching | `IdentityProfile.FirstMatching`, used by both rules | One definition of "the repository's profile", not two loops that could drift |
| Which URLs a repository reaches | Every remote's fetch and push URL | A fetch of all remotes, a pull from the upstream and a push to a separate push URL are all covered, and git only asks the helper whose origin it contacts |
| Tag and branch remote operations | A `RunOnRemoteAsync` / signed command beside the local runner | Local operations never read profiles or tokens |
| Integration tests' configuration directory | Inside the workspace | Every transfer now reads the profiles file; a test must not read the developer's own |
| The Changes page's direct `ISyncService` fetch | Covered with no change | It goes through `SyncService`, like every other transfer |

## Deviations & follow-ups

- The plan's rule took "profiles, accounts, identity and remote URLs". It takes the matched profile
  instead, for the reason in the table above.
- `RemotesPageViewModel` failures and other non-sync errors keep git's own first line. That is
  PHASE04's scope for sync, and out of scope elsewhere.
- Documentation sweep: `README.md`, as listed above. `ProfilePushRule`'s XML remark was corrected with
  it.

## Build / test evidence

- `dotnet build Enigma.GitClient.slnx`: 0 warnings, 0 errors, on the first build.
- `dotnet test --solution Enigma.GitClient.slnx`: **2342 passed**, 0 failed, on the first run. That is
  2311 before, plus 31 new (19 rule cases, 5 signed-operation tests, 7 resolver integration tests).
