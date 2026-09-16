# FEATURE-22C0-PHASE03 — GitLab & Azure DevOps providers

**Item:** FEATURE-22C0 — Repository hosting integrations
**Branch:** `feature/feature-22c0-phase03-gitlab-azure`
**Run:** feature/2026-09-15-enigma-git-client

## Summary

The other two hosts, written against the same contract GitHub already fitted — which is the point of
PHASE01's abstraction, and the reason this phase is two classes and their tests rather than a second
integrations page.

**GitLab** speaks v4 on `<instance>/api/v4`, so a self-hosted instance is the ordinary case rather
than a special one. The token goes in GitLab's own `PRIVATE-TOKEN` header, never in a query string
where it would end up in a log. Paging follows `X-Next-Page` — which is present and *empty* on the
last page, GitLab's way of saying "that was the last one". A project path keeps its subgroups:
`group/subgroup/service` is one project, and its web address is the same path with GitLab's `/-/`
in the middle.

**Azure DevOps** is one product with four address shapes. An account is an organisation (or a Server
collection) rather than a host, because that is what a token is issued at and what repositories are
listed from — so the instance URL carries a path: `https://dev.azure.com/contoso`,
`https://contoso.visualstudio.com`, or `https://tfs.example.com/tfs/DefaultCollection`. The token is
HTTP Basic with an empty user name, as Azure documents. One call returns every repository in the
organisation, so the filters are applied to what came back rather than pretended to the API. Its deep
links use Azure's own parameters — `?version=GB<branch>` for a branch and `?path=…&version=GC<sha>`
for a file — and the provider works out which of the two it was handed.

The integrations page needed no change to gain them: the connect dialog lists whatever the registry
holds, and the host chooser appears now that there is more than one. The README documents the exact
scope each host needs and repeats that no issue, work-item, merge-request or pull-request scope is
ever requested.

## Files / modules touched

**Created — Core**

- `Hosting/Providers/GitLabProvider.cs` — identity, `X-Next-Page` paging, subgroup-aware links, and
  `JsonHelp`, the three JSON helpers the providers share
- `Hosting/Providers/AzureDevOpsProvider.cs` — connection identity, the organisation-wide listing,
  the four address shapes, and `LooksLikeSha`

**Modified**

- `Hosting/Providers/GitHubProvider.cs` — folded onto the shared `JsonHelp` helpers
- `DependencyInjection/ServiceCollectionExtensions.cs` — both providers registered
- `README.md` — the per-host scope table, and self-hosted instances in the feature list
- `docs/roadmap.md`, `docs/plan/FEATURE-22C0.md`

**Created — tests**

- `tests/Enigma.GitClient.Core.UnitTests/Hosting/GitLabProviderTests.cs` — 25 cases
- `tests/Enigma.GitClient.Core.UnitTests/Hosting/AzureDevOpsProviderTests.cs` — 31 cases

**Modified — tests**

- `Hosting/HostProviderRegistryTests.cs` — the registry as the application builds it, asserting that
  each of the three claims exactly its own hosts and nothing else claims them
- `IntegrationsPageTests.cs` — the connect dialog over the real registry

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| What an Azure DevOps account is | An organisation or a collection, not a host | That is the level a token is issued at and repositories are listed from; one host serves many organisations a token cannot see |
| Azure identity | `_apis/connectionData` | The one identity call that works the same on the cloud and on Server, and needs no scope beyond the one the token has. The profile API lives on a different host in the cloud and does not exist on Server |
| A non-JSON answer from Azure | A rejected token | Azure DevOps answers a bad token with a sign-in page and a 203, not a 401, so "this is not JSON" is where a wrong token actually lands |
| Azure filters | Applied to what came back | Its listing endpoint takes neither a search nor a visibility parameter; applying them locally is honest, and it is one request either way |
| GitLab search | Passed to the API | Unlike GitHub's listing, GitLab's takes a `search` parameter, so it is asked rather than filtered afterwards |
| GitLab's last page | An empty `X-Next-Page` | The header is present and blank at the end rather than absent, and treating blank as "another page" would loop |
| GitLab visibility | Only sent when it is not "all" | `visibility=` with no value is not the same request as omitting it |
| Anything not `public` | Private | GitLab's `internal` is not public, and showing it as public in a list of repositories would be the wrong way round to be wrong |
| The shared JSON helpers | `JsonHelp`, internal | Three providers reading the same shapes three ways is three chances to differ on what a missing field means |
| The SSH endpoints | Mapped back to their web hosts | `ssh.dev.azure.com`, `vs-ssh.visualstudio.com` and `altssh.gitlab.com` serve no web pages; a link to one is a dead link |

## Deviations & follow-ups

- **Deviation:** the plan's Azure DevOps steps mentioned `_apis/profile/profiles/me` and a separate
  `_apis/projects` call. `connectionData` covers the identity on both the cloud and Server, and
  `git/repositories` already returns the project each repository belongs to, so the extra call would
  only have been a second way to learn the same thing.
- **Follow-up:** an Azure DevOps organisation with thousands of repositories is fetched in one
  response, because the endpoint has no paging. It is the same request the web interface makes, but
  a `$top`/`continuationToken` loop would be gentler on a very large collection.
- **Follow-up:** GitLab's rate-limit headers (`RateLimit-Remaining`/`Reset`) are read where a
  self-hosted instance sends them; gitlab.com more often answers with `Retry-After`, which the shared
  classification already handles.
- **Deviation from nothing:** the integrations page and its dialog needed no change at all to gain
  two hosts, which is the result PHASE01's abstraction was for.
- **Line endings (recommendation only):** no CRLF churn observed. No action taken.

## Build/test evidence

```
dotnet build Enigma.GitClient.slnx --no-incremental
  0 Warning(s)
  0 Error(s)

dotnet test --solution Enigma.GitClient.slnx
  Test run summary: Passed!
  total: 1532  failed: 0  succeeded: 1532  skipped: 0
```

72 tests are new in this dev and none of them contacts the network: every provider conversation runs
against a scripted `HttpMessageHandler`, and the address shapes are asserted directly.
