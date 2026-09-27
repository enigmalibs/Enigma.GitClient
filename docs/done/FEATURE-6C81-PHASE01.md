# FEATURE-6C81-PHASE01 — Credential helper for git

**Item:** FEATURE-6C81 — Git signs in with the profile's token
**Phase:** PHASE01 — Credential helper for git
**Branch:** `feature/feature-6c81-phase01-credential-helper`
**Run:** feature/2026-09-27-profile-token-git-auth

## Summary

Core can now hand git a login for one invocation without writing it anywhere. Nothing uses it yet:
PHASE02 wires it into sync and remote pushes.

- **`GitHostCredential`** — a user name and a token for one **origin** (`scheme://host[:port]`,
  lower-cased, default port dropped).
  - `TryGetOrigin(Uri)` computes the origin of an instance root or a remote URL.
  - `TryCreate(location, userName, token)` refuses:
    - anything that is not HTTP(S);
    - a host outside `[a-z0-9.-]` or a bracketed IPv6 literal;
    - an empty user name or token, or one holding CR, LF or NUL.
- **`GitCredentials`** — the logins of one invocation, the first kept per origin.
  - `BuildConfigArguments()` gives, per origin:

    ```
    -c credential.<origin>.helper=
    -c credential.<origin>.helper=!f() { test "$1" = get || exit 0; printf 'username=%s\npassword=%s\n' "$ENIGMA_GIT_USERNAME_<n>" "$ENIGMA_GIT_PASSWORD_<n>"; }; f
    ```

    The empty value empties git's helper list, so the user's own helpers neither answer first nor
    receive the token to store. The helper answers `get` only.
  - `BuildEnvironment()` is the one place the tokens are revealed, into the variables the helpers read.
- **`GitCommand.WithCredentials(credentials)`** — a new command with the pairs **first** in the vector
  (git takes global options in any order before the verb) and the variables merged into its
  environment. With no login it returns the same command. The runner never logs the environment, and
  `ToString()` / `GitCommandException` only ever show the arguments, which hold variable names.
- **`IRepositoryHostProvider.GitUserName`** — `x-access-token` (GitHub), `oauth2` (GitLab), `pat`
  (Azure DevOps).

## Files / modules touched

**Created**

- `src/Enigma.GitClient.Core/Git/GitCredentials.cs` — `GitHostCredential`, `GitCredentials`
- `tests/Enigma.GitClient.Core.UnitTests/Git/GitCredentialsTests.cs` — 29 cases:
  - origins: case, user info, default and explicit ports, `http`, a path-carrying Azure DevOps root,
    IPv6;
  - what is refused: non-HTTP schemes, an unnameable host, tokens with CR/LF/NUL or empty, bad user
    names;
  - the configuration: reset before helper and host-scoped, `get` only, two origins with their own
    variables, first login kept, `None`;
  - on a command: config first and the verb kept, the token only in the environment (not in the
    arguments, `ToString()` or a `GitCommandException`), the command's own environment and input kept,
    `None` a no-op, the forbidden-operation check still passing.
- `tests/Enigma.GitClient.Core.IntegrationTests/Git/GitCredentialHelperTests.cs` — against a real git,
  through the product's runner, no network:
  - `git credential fill` answers with the user name and token for its origin;
  - another host gets nothing (prompts are disabled, so it fails);
  - a helper configured in the repository does not answer first;
  - `git credential approve` does not reach a configured `store` helper, which does store without the
    injection.

**Modified**

- `src/Enigma.GitClient.Core/Git/GitCommand.cs` — `WithCredentials`
- `src/Enigma.GitClient.Core/Hosting/IRepositoryHostProvider.cs` — `GitUserName`
- `src/Enigma.GitClient.Core/Hosting/Providers/GitHubProvider.cs`, `GitLabProvider.cs`,
  `AzureDevOpsProvider.cs` — `GitUserName`
- Tests: `tests/Enigma.GitClient.App.UnitTests/Infrastructure/FakeHostProvider.cs` and the registry
  tests' `StubProvider` implement `GitUserName`; the three provider tests assert it.
- `docs/roadmap.md`, `docs/plan/FEATURE-6C81.md` — statuses

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| Configuration and environment as properties or methods | `BuildConfigArguments()` / `BuildEnvironment()` | Revealing the tokens is an explicit call, not a property a debugger or serializer reads by accident |
| How a login is built | `GitHostCredential.TryCreate` from any address on the origin, no public constructor | An unsafe value cannot exist as a login at all; the resolver can log a refusal |
| Where the origin comes from | Scheme, lower-cased host and non-default port of the address | git's HTTP credential context carries exactly these unless `useHttpPath` is set |
| `printf` vs `echo` | `printf '%s'` | No backslash interpretation differences between `sh` implementations |

## Deviations & follow-ups

- The plan named `ConfigArguments`, `Environment` and `Origins`. They became `BuildConfigArguments()`,
  `BuildEnvironment()` and `Credentials`, for the reason in the table above.
- Documentation sweep: nothing is stale. No behaviour visible to users changed in this phase.

## Build / test evidence

- `dotnet build Enigma.GitClient.slnx`: 0 warnings, 0 errors (after adding `GitUserName` to the
  registry tests' stub, which the first build flagged).
- `dotnet test --solution Enigma.GitClient.slnx`: **2311 passed**, 0 failed. That is 2278 before, plus
  33 new (29 unit, 4 integration).
