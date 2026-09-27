# FEATURE-6C81 — Git signs in with the profile's token

**Status:** DONE — see `docs/done/FEATURE-6C81-PHASE01.md` to `docs/done/FEATURE-6C81-PHASE04.md`
**Type:** FEATURE
**Branch:** one per phase, see below
**Run:** feature/2026-09-27-profile-token-git-auth

## Objective

A user enters a token once, in a profile's integration, and every network git operation to that
host works — fetch, pull, push, a branch's fast-forward, the quiet background fetch, pushing or
deleting a tag, deleting a remote branch, and clone — with no credential helper, no
`~/.git-credentials` and no git-side setup at all.

## Context & constraints

- Today a connected account's token serves the host API (who am I, list repositories) and, since
  FEATURE-1406-PHASE05, is the *permission* to push (`ProfilePushRule`: "the integration is the
  permission, not the credential"). git's transport still authenticates with the user's own credential
  helper or SSH key. With none, and `GIT_TERMINAL_PROMPT=0` (`GitProcessRunner.ApplyEnvironment`), a
  push fails with *The remote refused your credentials*.
- Every git invocation is a `GitCommand` (argument vector + per-command `Environment` + standard
  input) built by `IGitCommandFactory` and run by `IGitProcessRunner`. The environment is never logged;
  `GitCommand.ToString()` and `GitCommandException` render only the redacted arguments.
- Network operations: `SyncService.RunAsync` (fetch, pull, fast-forward, push — every sync the App
  runs, the quiet fetch included), `TagService.PushAsync` / `DeleteRemoteAsync`,
  `BranchService.DeleteRemoteAsync`, `RepositoryService.CloneAsync`.
- The repository's profile is the first profile matching the identity git commits with there
  (`IGitIdentityService.GetEffectiveAsync`), exactly as `PushGuard` decides it. Accounts belong to a
  profile (`HostAccount.BelongsTo`) and own a remote by host (`HostAccount.Owns`). Tokens are read with
  `IHostAccountService.GetTokenAsync` and wrapped in `SecretString`.
- The minimum git is 2.20 (`GitVersion.Minimum`), so `GIT_CONFIG_COUNT` (2.31) is not available.
  Credential helpers starting with `!` run through `sh`, which Git for Windows ships.
- The README (*Connecting a host*) and the providers' `TokenScopeHint` ask for read-only scopes.

## Decisions

| Decision | Rationale |
|---|---|
| A per-command inline credential helper: `-c credential.<origin>.helper=` (resets the list) then `-c credential.<origin>.helper=!f() { test "$1" = get \|\| exit 0; printf 'username=%s\npassword=%s\n' "$ENIGMA_GIT_USERNAME_<n>" "$ENIGMA_GIT_PASSWORD_<n>"; }; f` | The spec's mechanism: the token is only in that one process's environment, never in argv, on disk or in `.git/config`; the reset keeps the user's helpers from answering first and from storing the token |
| `GitCommand.WithCredentials(GitCredentials)` prepends the `-c` pairs | git takes its global options in any order before the verb, so no factory or interface changes; `ForbiddenGitOperations` still checks the whole vector |
| One helper per **origin** (`scheme://host[:port]`) the repository's remotes use over HTTP(S), each fed by the first account of the repository's profile that owns it | A fetch of every remote gets a helper for each host it reaches; an SSH-only repository runs exactly as today, and the token is not handed to processes that cannot use it |
| The token goes to an `http://` remote only when the account itself is `http://` | An `https` account's token must never cross the network in the clear; an `http` instance already receives it that way for its API |
| `IRepositoryHostProvider.GitUserName`: GitHub `x-access-token`, GitLab `oauth2`, Azure DevOps `pat` | What each host documents (or accepts) as the user name paired with a token |
| A token holding CR, LF or NUL, or an origin whose host is not `[A-Za-z0-9.-]` (or a bracketed IPv6 literal), is not injected | A newline would let a token forge lines of git's credential protocol; a strange host could break the `-c` key |
| Reading profiles, identity, remotes, accounts or tokens fails → no credentials, a warning in the log | This is a login, not a permission: git's own credentials still get their chance, and the push guard keeps failing closed on its own |
| Clone: `CloneRequest.Account` (the account the host browser listed the repository with); otherwise the current profile's (global identity) account that owns the URL | The spec's rule; a clone has no repository identity yet |
| Refused-token wording lives in `SyncErrorMapper`, told whether a token was used; a 401/403 is an authentication failure | One mapper, one vocabulary; today a 403 reads as a network problem |

## PHASE01 — Credential helper for git

**Status:** DONE — see `docs/done/FEATURE-6C81-PHASE01.md`
**Branch:** `feature/feature-6c81-phase01-credential-helper`

### Steps

1. `Core/Git/GitCredentials.cs` — `GitHostCredential(string Origin, string UserName, SecretString Token)`
   and `GitCredentials`: `None`, `IsEmpty`, `Origins`, `ConfigArguments`, `Environment`, and
   `For(IEnumerable<GitHostCredential>)`, which validates each entry (http/https origin, safe host,
   non-empty user name and token, no CR/LF/NUL), keeps the first entry per origin, and numbers the
   environment variables.
2. `GitHostCredential.OriginOf(Uri)` — `scheme://host[:port]`, lower-cased, default port dropped.
3. `GitCommand.WithCredentials(GitCredentials)` — a new command with the `-c` pairs first and the
   variables merged into its environment; returns itself when the credentials are empty.
4. `IRepositoryHostProvider.GitUserName` on the three providers and the App tests' `FakeHostProvider`.
5. Tests:
   - unit — the reset comes before the helper for each origin; the key is host-scoped; two origins get
     distinct variables; the token is in the environment and absent from the arguments, from
     `ToString()` and from a `GitCommandException` message; unsafe tokens and origins are skipped;
     duplicates keep the first; `WithCredentials(None)` changes nothing; the provider user names.
   - integration, against a real git and no network — `git credential fill` through the product's
     runner: the helper answers with the user name and token for its host; another host gets nothing
     (the fill fails with prompts disabled); a helper configured in the repository is not consulted;
     `git credential approve` does not reach a repository-configured storing helper.

### Acceptance criteria

- `GitCredentials` builds host-scoped, reset-first helpers whose secrets live only in the environment.
- A real git returns the token through the helper, for its host only, and never stores it elsewhere.
- Build clean with zero warnings; the whole suite green, including the new tests.

## PHASE02 — Sync and remote pushes sign in

**Status:** DONE — see `docs/done/FEATURE-6C81-PHASE02.md`
**Branch:** `feature/feature-6c81-phase02-sync-signs-in`

### Steps

1. `Core/Hosting/ProfileCredentialRule.cs` — a pure rule: profiles, accounts, identity and the
   repository's remote URLs in; the accounts to sign in with, one per HTTP(S) origin, out (the first
   matching profile; its accounts that own the URL; the http/https rule).
2. `Core/Hosting/GitCredentialResolver.cs` — `IGitCredentialResolver.ForRepositoryAsync(repository)`:
   reads the profiles (none → `GitCredentials.None`, nothing else read), the effective identity, the
   remotes, the accounts and their tokens; the user name from the provider registry. Fails open with a
   warning. Registered in `AddGitClientCore`.
3. `SyncService`, `TagService` (push, remote delete) and `BranchService` (remote delete) resolve the
   credentials and run their network commands `WithCredentials`.
4. Tests: the rule (no profile, a profile without accounts, an SSH-only remote, an HTTPS remote owned
   by the profile's account, another profile's and an unassigned account, two accounts on one origin,
   two hosts, an `https` account against an `http` remote, an `http` account); the resolver (no
   profiles reads nothing else; tokens; a failing read gives none); `SyncService` runs fetch, pull,
   fast-forward and push with the credentials the resolver gives, and the unchanged vector when it gives
   none; the tag and branch remote operations likewise.

### Acceptance criteria

- Under a profile with an integration for an HTTPS remote, every sync and remote push carries that
  integration's token through the helper; without one, the commands are exactly today's.
- Build clean with zero warnings; the whole suite green, including the new tests.

## PHASE03 — Clone signs in

**Status:** DONE — see `docs/done/FEATURE-6C81-PHASE03.md`
**Branch:** `feature/feature-6c81-phase03-clone-signs-in`

### Steps

1. `CloneRequest.Account` (`HostAccount?`) — the account the clone came from.
2. `IGitCredentialResolver.ForCloneAsync(url, account)` — that account when it owns the URL; otherwise
   the current profile's (global identity) account that owns it; the same http/https rule.
3. `RepositoryService.CloneAsync` runs the clone `WithCredentials`.
4. The Profiles page's *Browse repositories* clone passes the row's account.
5. Tests: the resolver's clone rule (explicit account, profile account, SSH URL, no profile); the clone
   command carries the credentials; the Profiles page's clone request names the account.

### Acceptance criteria

- A repository picked in an integration's browser clones with that integration's token.
- Build clean with zero warnings; the whole suite green, including the new tests.

## PHASE04 — Token scopes and refused tokens

**Status:** DONE — see `docs/done/FEATURE-6C81-PHASE04.md`
**Branch:** `feature/feature-6c81-phase04-token-messages`

### Steps

1. `SyncErrorMapper.Map(standardError, standardOutput, usedHostToken)` — an authentication failure
   says, when a token was used, that the host refused the profile's token (expired, or without write
   access — GitHub fine-grained *Contents: read and write* or classic `repo`; GitLab
   `write_repository`; Azure DevOps *Code: Read & write*) and to replace it on the Profiles page;
   otherwise, to connect an account for this host to the profile or check the credential helper or SSH
   key. `The requested URL returned error: 401` / `403` count as authentication failures.
2. `SyncService` passes whether its command carried credentials.
3. Providers' `TokenScopeHint` and the README's *Connecting a host* ask for the scopes that also push.
4. Tests: both authentication messages; 401 and 403; the network rule still wins for an unreachable
   host.

### Acceptance criteria

- A refused token is explained as the profile's token, with the scopes pushing needs.
- The scope hints and the README match what the feature needs.
- Build clean with zero warnings; the whole suite green, including the new tests.

## Out of scope

- OAuth or device-flow sign-in.
- Converting SSH remotes to HTTPS.
- Writing credentials into git's own helper or configuration.
- Mapping clone, tag and remote-branch-delete failures through `SyncErrorMapper` (they keep git's own
  first line).

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| Where the helper config enters the command | `GitCommand.WithCredentials`, prepended | No interface change; git accepts global options in any order | A new `IGitCommandFactory` method (every stub changes) |
| Which origins get a helper | Those the repository's HTTP(S) remotes use, owned by the profile's accounts | Precise, SSH untouched, token not handed to processes that cannot use it | Every account of the profile (token in every child's environment) |
| Two accounts of a profile on one origin | The first, in stored order | git's credential context has no path by default, so one answer per origin | Asking; path-scoped helpers (`useHttpPath` changes git's behaviour) |
| `http` remotes | Only for an `http` account | Never downgrade an `https` token to cleartext | Always; never |
| Helper output | `printf` with `%s` | No `echo` backslash differences between shells | `echo` |
| Unsafe token or host | Skipped | A newline forges protocol lines; an odd host breaks the key | Escaping |
| A failing read | No credentials, a warning | A login, not a permission; git's own credentials still work | Failing the operation |
| Git user names | GitHub `x-access-token`, GitLab `oauth2`, Azure DevOps `pat` | Accepted by each host with a personal access token | The account's login (empty until validated, ignored by the hosts) |
| Clone without an explicit account | The current profile's (global identity) account owning the URL | There is no repository identity yet; *current* is the app's existing notion | Any account owning the host |
| Error wording | In `SyncErrorMapper`, with a `usedHostToken` flag | One mapper; the caller knows whether it signed in | A second mapper in the App |
| Failures outside sync (clone, tags, remote branch delete) | Keep git's own line | Out of the sync mapper's reach today; a follow-up | Wrapping each in `SyncException` |
| Phasing | Helper → sync → clone → messages | One reviewable commit each; each phase leaves the app working | One large dev |
