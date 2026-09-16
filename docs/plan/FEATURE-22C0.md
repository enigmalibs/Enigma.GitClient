# FEATURE-22C0 — Repository hosting integrations

**Status:** DONE
**Type:** FEATURE
**Branch:** `feature/feature-22c0-<phase>-<slug>` (one per phase)
**Run:** feature/2026-09-15-enigma-git-client

## Objective

Connect the client to GitHub, GitLab and Azure DevOps: sign in with a personal access token, browse
and clone the repositories the account can reach, and jump from a commit, branch or file straight to
its page on the host — **without** touching issues or pull requests, which are explicitly excluded.

## Context & constraints

- One abstraction, three implementations, so a fourth host is a class rather than a refactor.
- Tokens are secrets: encrypted at rest, never logged, never written to a plan or completion doc, and
  redacted from every git argv and HTTP log.
- Every provider call is cancellable, paged and rate-limit aware.
- Host detection from a remote URL is best-effort and never blocks anything: an unrecognised remote
  simply has no integration.

## PHASE01 — Provider abstraction & tokens

**Status:** DONE — see `docs/done/FEATURE-22C0-PHASE01.md`

**Steps**

1. `IRepositoryHostProvider`: `HostKind`, `DisplayName`, `MatchesRemote(Uri|string)`,
   `ValidateCredentialAsync(HostAccount, ct)`, `ListRepositoriesAsync(HostAccount, query, ct)`
   (paged, returning `HostRepository`: full name, description, default branch, clone URLs, visibility,
   web URL, last push), `BuildCommitUrl/BuildBranchUrl/BuildFileUrl(remoteUrl, …)`.
2. `HostAccount` — host kind, instance base URI (self-hosted GitLab/GHE/Azure Server supported),
   display name, user name, token reference. **Never** carries the token value itself.
3. `ITokenStore`: `SetAsync(key, secret)`, `TryGetAsync(key)`, `DeleteAsync(key)`, `ListKeysAsync`.
   `FileTokenStore` encrypts with AES-GCM; the data key is random per machine-user and is itself
   protected with DPAPI (`CurrentUser` scope) on Windows, or stored in a `0600` file under
   `$XDG_CONFIG_HOME/Enigma.GitClient` on Linux, in a directory created `0700`.
4. `SecretString` — a wrapper that never returns its value from `ToString()` and is redacted by the
   logging formatter; `ArgumentRedactor` extended to strip tokens from URLs.
5. `IHostAccountService` — account CRUD over the store plus a `HostProviderRegistry` resolving a
   provider from a remote URL.
6. A shared `HttpClient` configured through `IHttpClientFactory` with a product user agent, a 30 s
   timeout, and a retry-once policy on 5xx/429 honouring `Retry-After`.

**Acceptance criteria**

- Unit tests: `FileTokenStore` round-trips a secret; a tampered ciphertext fails authentication rather
  than returning garbage; deleting removes it; the file is created with `0600`/directory `0700` on
  Linux (asserted via `File.GetUnixFileMode`); two different data keys cannot read each other's
  secrets; `SecretString.ToString()` never leaks; the redactor strips `https://x:token@host`,
  `?private_token=`, `Authorization:` and `PRIVATE-TOKEN:` values.
- Unit tests: the registry resolves the right provider for `github.com`, a GHE host, `gitlab.com`, a
  self-hosted GitLab, `dev.azure.com`, the legacy `*.visualstudio.com`, and returns none for an
  unknown host — for both HTTPS and `git@host:path` SSH forms.

## PHASE02 — GitHub provider

**Status:** DONE — see `docs/done/FEATURE-22C0-PHASE02.md`

**Steps**

1. `GitHubProvider` on REST v3 (`/user`, `/user/repos?affiliation=…&per_page=100`, link-header
   paging), `Authorization: Bearer`, `X-GitHub-Api-Version` pinned, GitHub Enterprise Server supported
   by pointing the base URI at `https://<host>/api/v3`.
2. URL builders: `…/commit/<sha>`, `…/tree/<branch>`, `…/blob/<sha>/<path>#L<n>` — derived from the
   remote URL so a fork or a renamed repository still resolves.
3. Rate-limit handling: `X-RateLimit-Remaining`/`Reset` surfaced as a typed exception with the reset
   time, not a raw 403.
4. UI: the Integrations page — add a GitHub account (base URI, token, with a "how to create a token
   and which scopes" hint), validate, list repositories with search and a visibility filter, and
   **Clone** wired into FEATURE-52FB's clone flow; per-account remove with token deletion.
5. Graph and file context menus gain "Open on GitHub" when the remote matches an account.

**Acceptance criteria**

- Unit tests against a stubbed `HttpMessageHandler`: `/user` maps to the account identity; repository
  listing follows link-header paging across three pages and maps every field; a 401 maps to
  `HostAuthenticationException`; a rate-limited 403 maps to `HostRateLimitException` carrying the
  reset; a 5xx is retried once then surfaced.
- Unit tests: URL builders produce the right links for `https://github.com/o/r.git`,
  `git@github.com:o/r.git`, and a GHE host, including paths containing spaces and `#`.
- No test contacts the network.

## PHASE03 — GitLab & Azure DevOps providers

**Status:** DONE — see `docs/done/FEATURE-22C0-PHASE03.md`

**Steps**

1. `GitLabProvider` on REST v4 (`/user`, `/projects?membership=true&per_page=100`, `X-Next-Page`
   paging), `PRIVATE-TOKEN` header, self-hosted instances via the base URI; URL builders
   `…/-/commit/<sha>`, `…/-/tree/<branch>`, `…/-/blob/<sha>/<path>#L<n>`; subgroup paths handled.
2. `AzureDevOpsProvider` on REST 7.1 (`/_apis/profile/profiles/me`, `/_apis/projects`,
   `/<project>/_apis/git/repositories`), Basic auth with an empty user and the PAT as the password,
   both `dev.azure.com/<org>` and the legacy `<org>.visualstudio.com` forms, plus Azure DevOps Server
   with a collection path; URL builders `…/commit/<sha>`, `…?version=GB<branch>`,
   `…/commit/<sha>?path=<path>`.
3. The Integrations page becomes provider-agnostic: one account list, the provider chosen from a
   dropdown, one repository browser.
4. README documents, per provider, exactly which token scopes are needed (`repo` / `read_api` +
   `read_repository` / `Code: Read`) and that no issue or pull-request scope is ever requested.

**Acceptance criteria**

- Unit tests against stubbed handlers for both providers: identity, paged listing (GitLab
  `X-Next-Page`, Azure continuation), field mapping, 401 and rate-limit mapping.
- Unit tests: URL builders for `gitlab.com`, a self-hosted GitLab with a subgroup, `dev.azure.com`,
  `*.visualstudio.com`, and an Azure DevOps Server collection URL.
- The registry test from PHASE01 is extended so every provider claims exactly its own hosts.
- No test contacts the network.

## Out of scope

- **Issues and pull/merge requests** — explicitly excluded by the specification, in every provider.
- OAuth device-flow sign-in (needs registered applications per host; PAT covers every provider today
  and is recorded as a follow-up).
- CI/pipeline status (follow-up).

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| Authentication | Personal access tokens | Works identically on all three hosts, on self-hosted instances too, and needs no registered OAuth application or callback listener | OAuth device flow (needs per-host app registration); reusing the git credential helper (not readable by API clients on every platform) |
| Token storage | AES-GCM file; key DPAPI-protected on Windows, `0600` on Linux | Encrypted at rest with BCL-only crypto and works headless on both target platforms | plaintext (unacceptable); libsecret/keyring P/Invoke (native dependency, absent in many Linux sessions); Windows-only DPAPI for the payload (not cross-platform) |
| HTTP client | `IHttpClientFactory` + stubbed handlers in tests | Correct socket lifetime, and every provider is testable with no network | `new HttpClient()` per call; recorded live cassettes (need secrets) |
| Scope of "integration" | Accounts, repository browsing/cloning, deep links | Exactly the useful half once issues and PRs are excluded | building an issue/PR surface anyway (explicitly forbidden) |
| Self-hosted instances | Base URI on the account from day one | GHE, self-hosted GitLab and Azure DevOps Server are common and cost nothing to support if designed in | hard-coding the public hosts |
