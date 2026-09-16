# FEATURE-22C0-PHASE02 — GitHub provider

**Item:** FEATURE-22C0 — Repository hosting integrations
**Branch:** `feature/feature-22c0-phase02-github`
**Run:** feature/2026-09-15-enigma-git-client

## Summary

GitHub is connected, and the first thing it is asked is who the token belongs to — before anything
is stored, so a token that does not work is refused now rather than at the first listing. The
integrations page then lists the repositories that account can reach, newest push first, with a
filter box, a public/private switch and a **Clone** button that runs the same clone flow (progress
overlay, working cancel, opens when it lands) the repositories page uses.

GitHub Enterprise Server works by typing the instance's own address: `github.com` answers on
`api.github.com`, and every Enterprise Server answers on `<host>/api/v3`, which is the only
difference between them as far as this client is concerned. Paging follows the `Link` header's
`rel="next"` exactly as GitHub gives it rather than counting pages, and a rate limit is read from
`X-RateLimit-Remaining`/`Reset` so the message can say when it lifts instead of "try again later".

Deep links need no account at all: the graph's row menu gains **Open this commit on GitHub** and the
changed-files menu gains the same for a file at the selected commit, both derived from the
repository's own remote — so a repository cloned over SSH resolves to the same web page as one
cloned over HTTPS, and a fork or a rename follows wherever the remote now points.

No issue or pull-request scope is requested anywhere, and the dialog says so where the user chooses
the token.

## Files / modules touched

**Created — Core**

- `Hosting/Providers/GitHubProvider.cs` — identity, paged listing, the three URL builders,
  `ApiBase`, `NextLink` and `RateLimitReset`

**Created — App**

- `Services/HostLinkService.cs` — `IHostLinkService`: which host the open repository is on, and
  opening a commit, a branch, a file or a repository on it
- `ViewModels/Dialogs/AddHostAccountDialogViewModel.cs`
- `Views/Dialogs/AddHostAccountDialogView.axaml` (+ `.axaml.cs`)

**Modified — App**

- `ViewModels/Pages/IntegrationsPageViewModel.cs` — the whole page: accounts, repositories, the
  filter, the visibility switch, connect, disconnect, clone and open
- `Views/Pages/IntegrationsPageView.axaml` — replaced the placeholder with the page
- `ViewModels/Pages/CommitRowViewModel.cs` — `HistoryRowCommands.OpenOnHost` and `HostLabel`
- `ViewModels/Pages/HistoryPageViewModel.cs` — the command behind them, and the panel's file link
- `ViewModels/Panels/ChangedFilesPanelViewModel.cs` — `OpenOnHost`, `OpenOnHostCommand`,
  `HostLinkLabel`
- `ViewModels/Pages/RepositoriesPageViewModel.cs` — `DefaultParentDirectory` made public
- `Services/SystemInterop.cs` — `OpenUrlAsync`, which refuses anything but http and https
- `Views/Pages/HistoryPageView.axaml`, `Views/Panels/ChangedFilesPanelView.axaml` — the menu items
- `DependencyInjection/ServiceCollectionExtensions.cs` (both projects)

**Created — tests**

- `tests/Enigma.GitClient.Core.UnitTests/Hosting/GitHubProviderTests.cs` — 36 cases against scripted
  responses: identity, paging across three pages, field mapping, every failure, and the link builders
- `tests/Enigma.GitClient.App.UnitTests/IntegrationsPageTests.cs` — 17 cases on the dialog, connecting,
  listing, filtering, disconnecting, and a rendered frame
- `tests/Enigma.GitClient.App.UnitTests/HostLinkTests.cs` — 9 cases against real repositories with
  real remotes

**Modified — tests**

- `Infrastructure/TestServices.cs` — a hook for replacing a registration
- `Infrastructure/UiServiceDoubles.cs` — `OpenUrlAsync` on the recording interop

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| When the token is checked | Before the account is stored | A stored account whose token does not work fails later, somewhere else, for no visible reason |
| What the account is named | The host's own display name, unless the user typed one | Someone with two accounts needs to tell them apart; someone with one should not have to name it |
| Paging | The `Link` header's URL, followed verbatim | Counting pages re-derives what GitHub already said, and gets it wrong the moment the query changes |
| Search | Filters what has been read, up to ten pages | `/user/repos` takes no query, and `/search/repositories` cannot see the organisation repositories that listing can. Filtering what was fetched is honest about what it is doing, and the page says when there is more |
| Sort order | `sort=pushed` | The repository someone wants to clone is almost always one they have touched recently |
| Visibility switch | A new request, not a local filter | "Only private" is a different question to ask the host, and asking it is cheaper than fetching everything twice |
| Deep links | No account needed | A public repository has a public page. Requiring an account to open one would be a worse product for no security gain |
| Where the link comes from | The repository's remote | A fork, a rename or a transfer resolves to wherever the remote now points, without anything being remembered or refreshed |
| The SSH endpoint | Mapped to the web host | `ssh.github.com` is where SSH answers, not a place with web pages on it |
| Opening a URL | A separate `OpenUrlAsync` that refuses anything but http and https | `OpenPathAsync` requires the path to exist, and a shell should never be handed whatever a malformed remote parsed as |
| The token in the dialog | An ordinary string while typed, a `SecretString` the moment it leaves | That is what a `TextBox` binds to, and the window in between has no logging in it |

## Deviations & follow-ups

- **Deviation:** the plan put a "search" on the repository listing. GitHub's listing endpoint has no
  query parameter and its search endpoint sees a different set of repositories, so the page filters
  what it read and says so when there was more than it fetched.
- **Deviation:** the host chooser in the connect dialog is hidden while only one provider is
  registered, which is the case until PHASE03 adds the other two. Nothing about it is
  GitHub-specific — it already lists whatever the registry holds.
- **Follow-up:** the page fetches up to ten pages (a thousand repositories) when an account is
  selected. An account with more than that sees the most recently pushed thousand; a "load more"
  button would be the honest fix, and the summary line already says when the list was cut short.
- **Follow-up:** `HostLinkService` resolves the host once per repository change. A remote added
  while the application is open is picked up on the next repository refresh rather than immediately.
- **Deviation:** the README already lists all three providers, having been written ahead of them.
  PHASE03 makes it true and documents the scopes per provider, so nothing was edited here.
- **Line endings (recommendation only):** no CRLF churn observed. No action taken.

## Build/test evidence

```
dotnet build Enigma.GitClient.slnx --no-incremental
  0 Warning(s)
  0 Error(s)

dotnet test --solution Enigma.GitClient.slnx
  Test run summary: Passed!
  total: 1460  failed: 0  succeeded: 1460  skipped: 0
```

62 tests are new in this dev. None of them contacts the network: the provider's conversation runs
against a scripted `HttpMessageHandler`, the page runs against a provider double, and the deep-link
tests build real repositories with real remotes and assert on the address handed to the (recorded)
browser. The rendered frame is saved as `snapshots/integrations-page-dark.png` and was looked at.
