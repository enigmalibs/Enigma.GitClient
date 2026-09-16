# FEATURE-22C0-PHASE01 — Provider abstraction & tokens

**Item:** FEATURE-22C0 — Repository hosting integrations
**Branch:** `feature/feature-22c0-phase01-provider-abstraction`
**Run:** feature/2026-09-15-enigma-git-client

## Summary

The shape every hosting integration fits into, and the safe place its credential lives.

`IRepositoryHostProvider` is deliberately small — who a token belongs to, which repositories it can
reach, and where a commit, a branch or a file lives in a browser. That is the whole useful surface
once issues and pull requests are excluded, which is why a fourth host will be a class rather than a
redesign.

`RemoteUrl` parses the four shapes git accepts for the same remote — `https://host/owner/repo.git`,
`ssh://git@host:22/owner/repo.git`, the scp-like `git@host:owner/repo.git` and `git://host/…` — so a
repository cloned over SSH is recognised as the same repository cloned over HTTPS. Anything that is
not an address, such as a local path or a Windows drive, is an answer of "no host" rather than a
failure, and any credential inside the address is dropped during parsing rather than carried around
waiting to be logged.

`HostProviderRegistry` turns a remote into "which host, and which account can speak to it". The
accounts are consulted before the table of public host names, because that is the only way a
self-hosted GitLab or a GitHub Enterprise Server can be recognised at all — `git.example.com` says
nothing about itself. An account of the right kind on a *different* instance is deliberately not
lent to a remote: an enterprise token is not a github.com token.

Tokens are kept by `FileTokenStore`: each one sealed with AES-GCM under one random 256-bit data key,
with its own nonce, and with the key it is stored under fed in as associated data — so an entry
cannot be moved to another key, and a file edited by hand fails authentication instead of decrypting
to something. The data key is DPAPI-protected for the current user on Windows and a `0600` file
inside a `0700` directory on Linux. `SecretString` carries the value: `ToString()` is `***`, its hash
code is only the length, and the value comes out through `Reveal()` — a word that shows up in a
review.

## Files / modules touched

**Created — Core**

- `Hosting/RemoteUrl.cs` — the remote-address parser
- `Hosting/HostModel.cs` — `HostKind`, `WellKnownHosts`, `HostAccount`, `HostIdentity`,
  `HostRepositoryQuery`, `HostVisibility`, `HostRepositoryPage`, `HostRepository`, and the four
  exceptions (`HostException`, `HostAuthenticationException`, `HostRateLimitException`,
  `HostRequestException`)
- `Hosting/IRepositoryHostProvider.cs` — the provider contract, `HostMatch`, `IHostProviderRegistry`
  and `HostProviderRegistry`
- `Hosting/HostAccountService.cs` — `IHostAccountService` and its JSON-plus-token-store implementation
- `Hosting/HostHttp.cs` — the shared client's name, timeout and user agent, `HostRetryHandler`, and
  `HostResponse.Classify`
- `Security/SecretString.cs`
- `Security/TokenStore.cs` — `ITokenStore`, `FileTokenStore`, `TokenProtectionException`

**Modified**

- `DependencyInjection/ServiceCollectionExtensions.cs` — `AddRepositoryHosting`, called from
  `AddGitClientCore`
- `Directory.Packages.props`, `Enigma.GitClient.Core.csproj` —
  `System.Security.Cryptography.ProtectedData`
- `docs/roadmap.md`, `docs/plan/FEATURE-22C0.md`

**Created — tests**

- `tests/Enigma.GitClient.Core.UnitTests/Hosting/RemoteUrlTests.cs` — 40 cases over the four address
  shapes, credentials, ports, escaped paths, and which public instance each one is
- `tests/Enigma.GitClient.Core.UnitTests/Hosting/HostProviderRegistryTests.cs` — 16 cases on
  resolution, including a self-hosted instance and a GHE host found through their accounts
- `tests/Enigma.GitClient.Core.UnitTests/Hosting/HostAccountServiceTests.cs` — 10 cases on the
  account list, the tokens behind it, and what removal takes with it
- `tests/Enigma.GitClient.Core.UnitTests/Hosting/HostHttpTests.cs` — 17 cases on the retry and the
  classification
- `tests/Enigma.GitClient.Core.UnitTests/Security/TokenStoreTests.cs` — 17 cases on the round trip,
  the tamper, the moved entry, another machine's key, the file modes and the nonces

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| Where the token lives in the model | Nowhere in it | `HostAccount` carries a `TokenKey`, never a value, so an account can be logged, serialised or shown in a dialog with no chance of taking the credential with it. The provider is handed the token explicitly by the caller |
| What an account's `BaseUri` means | The instance's root, not the API base | It is what the user knows and types (`https://github.com`, `https://gitlab.example.com`, `https://dev.azure.com/contoso`), and it is what a remote address can be matched against. Each provider derives its own API base from it |
| Associated data for each entry | The key it is stored under | An entry copied to another key then fails authentication, so the file cannot be rearranged into someone else's credential |
| A tampered or foreign entry | `TokenProtectionException`, not "no token" | Returning nothing would look like "not signed in" and send the user to add the account again, hiding a file that has been edited |
| A corrupt *account list* | An empty list | There is nothing secret in it and nothing to lose: the tokens are still there, and adding the account again rewrites the file. The asymmetry with the token file is deliberate |
| The key file's mode | Set at creation, not afterwards | Between creating a world-readable file and tightening it there is a window in which the key is readable, and `FileStreamOptions.UnixCreateMode` closes it |
| Retry policy | Once, and never for a wait over ten seconds | A client that keeps retrying a rate-limited host gets the account throttled harder, and waiting out an hour-long reset behind a dialog is a hang rather than a retry |
| Where a failed response is classified | One shared method | Three providers wording the same four failures three ways is three chances to word one badly; the user's message no longer depends on which host refused them |
| `SecretString.GetHashCode` | The length only | A hash of the value is a fingerprint that can be logged, compared across processes and brute-forced offline |
| Recognising a host | Accounts first, then the public table, then the providers | Only the public instances can be recognised by name; everything else is recognised because the user told us about it |

## Deviations & follow-ups

- **Deviation:** the plan listed extending `ArgumentRedactor` as part of this phase. It already
  strips `https://x:token@host`, `?private_token=`, `Authorization:`, `PRIVATE-TOKEN:` and
  `--token=` — those patterns and their tests arrived with the git engine — so nothing was added,
  and the acceptance criterion is met by the tests that already exist.
- **Deviation:** the registry's resolution is tested against stub providers, because the real ones
  are PHASE02's and PHASE03's. The host table they will use (`WellKnownHosts`) is tested directly.
- **Follow-up:** `HostRepositoryQuery.Cursor` is an opaque string each provider defines for itself —
  a link header for GitHub, a page number for GitLab, a continuation token for Azure DevOps. Nothing
  enforces that a cursor is used with the provider that produced it; a page from the wrong provider
  would simply come back wrong rather than dangerous.
- **Follow-up:** a token is only ever as safe as the account it runs under. Nothing here defends
  against another process running as the same user, which is out of reach of any file-based store;
  `libsecret` would move the bar on Linux at the cost of a native dependency many sessions do not
  have.
- **Line endings (recommendation only):** no CRLF churn observed. No action taken.

## Build/test evidence

```
dotnet build Enigma.GitClient.slnx --no-incremental
  0 Warning(s)
  0 Error(s)

dotnet test --solution Enigma.GitClient.slnx
  Test run summary: Passed!
  total: 1398  failed: 0  succeeded: 1398  skipped: 0
```

112 tests are new in this dev, and no test contacts the network: the retry and the classification run
against a scripted `HttpMessageHandler`, and the token store runs against a throwaway configuration
directory that is deleted afterwards.
