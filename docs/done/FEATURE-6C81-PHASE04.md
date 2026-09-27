# FEATURE-6C81-PHASE04 — Token scopes and refused tokens

**Item:** FEATURE-6C81 — Git signs in with the profile's token
**Phase:** PHASE04 — Token scopes and refused tokens
**Branch:** `feature/feature-6c81-phase04-token-messages`
**Run:** feature/2026-09-27-profile-token-git-auth

## Summary

A refused login now says whose it was, and what to do about it.

- **`SyncErrorMapper.Map(standardError, standardOutput, usedHostToken)`.** An authentication failure
  reads one of two messages:
  - **`RefusedTokenMessage`**, when the command signed in with a profile integration's token: *The
    host refused this profile's token. It may have expired, or not allow this: pushing needs write
    access — Contents: read and write for a fine-grained GitHub token (or the classic 'repo' scope),
    'write_repository' on GitLab, 'Code: Read & write' on Azure DevOps. On the Profiles page,
    disconnect the account and connect it again with a new token, then try again.*
  - **`RefusedCredentialsMessage`**, when it did not: *The remote refused your credentials. Connect an
    account for this host to the profile on the Profiles page, or check the credential helper or the
    SSH key this remote uses, then try again.*
- **401 and 403 are authentication failures.** `The requested URL returned error: 401` / `403` and
  GitHub's `remote: Permission to … denied` now count as authentication. Before, git's accompanying
  *unable to access* made them read as a network failure. A host that cannot be resolved is still a
  network failure.
- **`SyncService`** tells the mapper whether its command carried credentials.
- **Scopes.** The three providers' `TokenScopeHint` (shown when connecting an account) name the write
  scope a push needs. The README's *Connecting a host* table gains a *To push as well* column and says
  what happens when a token is refused.

## Files / modules touched

**Modified**

- `src/Enigma.GitClient.Core/Sync/SyncFailure.cs` — the two messages, the `usedHostToken` parameter,
  401/403
- `src/Enigma.GitClient.Core/Sync/SyncService.cs` — passes `usedHostToken`
- `src/Enigma.GitClient.Core/Hosting/Providers/GitHubProvider.cs`, `GitLabProvider.cs`,
  `AzureDevOpsProvider.cs` — `TokenScopeHint`
- Tests:
  - `tests/Enigma.GitClient.Core.UnitTests/Sync/SyncParsingTests.cs` — 6 cases:
    - without an integration, the Profiles page and git's own setup;
    - a refused token, with every host's push scope;
    - 401, 403 and GitHub's permission denial as authentication;
    - an unresolvable host still a network failure with a token.
  - `tests/Enigma.GitClient.Core.UnitTests/Hosting/SignedInOperationsTests.cs` — a refused push reads
    the token message when signed in and the credentials message when not. The recording runner can
    answer a failure.
  - The three provider tests assert the write scope in the hint.
- `README.md` — *Connecting a host*: the scope table, and what a refused token says (the planned
  documentation step, and the docs sweep).
- `docs/roadmap.md`, `docs/plan/FEATURE-6C81.md` — statuses. The item's own row flips to `DONE` with
  this last phase.

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| What to tell the user to do with a refused token | Disconnect the account and connect it again | The Profiles page has no "replace token" action; the message must name one that exists |
| Where the messages live | Public constants on `SyncErrorMapper` | One wording, asserted by the tests at both levels |
| Which 403s count | Every `returned error: 403`, and GitHub's `remote: Permission to` | A 403 is always the host refusing what the credentials allow; none of them is a network problem |
| The scope hints | The read scope as before, and the write scope "for the client to push" | Someone who only browses and clones keeps the smallest token; someone who pushes knows what to add |

## Deviations & follow-ups

- The plan said "replace it on the Profiles page". There is no such action, so the message says to
  disconnect and connect again.
- Follow-up idea, not done: a *Replace token* action on an integration's row. `IHostAccountService.UpdateAsync`
  already accepts a new token.
- Out of scope, as planned: clone, tag and remote-branch-delete failures keep git's own first line.
- Documentation sweep: `README.md`, as listed above. Nothing else is stale.

## Build / test evidence

- `dotnet build Enigma.GitClient.slnx`: 0 warnings, 0 errors, on the first build.
- `dotnet test --solution Enigma.GitClient.slnx`: **2355 passed**, 0 failed, on the first run. That is
  2348 before, plus 7 new (6 mapper cases, 1 signed-operation test).
