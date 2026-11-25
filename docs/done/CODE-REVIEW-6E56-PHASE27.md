# CODE-REVIEW-6E56-PHASE27 — [Medium] SOLID-02 Profiles page split

## Summary

`ProfilesPageViewModel` (1,649 lines, 17 constructor parameters) held two pages' worth of work: the
identities (global, profiles, the repository's own) and the connected hosting accounts. The accounts
half moves out:

- `HostAccountsViewModel` (new, singleton, 731 lines, 11 parameters) owns:
  - the account rows under each profile and the earlier integrations;
  - connect, sign in again with a new token, disconnect, browse and clone, move to a profile;
  - the reset of a token store that cannot be read, and the band that says so;
  - `LoadIntegrationsAsync`, `ConnectAsync` and `ReplaceTokenAsync`.
  It takes the account service, the provider registry, the host links, the repository browser, the
  token recovery, the cloner, the dialogs, the info bar, the account dialog's view factory, the
  repository context (for its lifetime) and its logger.
- `ProfilesPageViewModel` (1,057 lines, 11 parameters) shows it as its `Accounts` property and keeps
  the identities.
- `IHostAccountsHost`, implemented by the page, is what the accounts need of it: the profile rows to
  list each account under, and the page's busy flag, which a sign-in sets as before. The page
  attaches itself once, from its constructor, because the accounts are a singleton the page takes
  (the page in their constructor would be a cycle).
- Deleting a profile still disconnects its accounts and refreshes the host links. The page now asks
  `Accounts.RemoveForProfileAsync` and `Accounts.RefreshHostLinksAsync`, so it no longer needs the
  account service or the links itself.
- `HostAccountRowViewModel`'s owner is `HostAccountsViewModel`, whose commands its buttons run. A
  profile row's "Connect an account" runs `Accounts.ConnectAccountCommand`.
- The view: five bindings gained the `Accounts.` prefix (the token-store band's visibility, text and
  reset button, and the earlier integrations' visibility and list). The row templates bind the rows'
  own commands and did not change.
- The code moved verbatim by a script. References to the page became the host's
  (`Host.Profiles`, `Host.IsBusy`), the context's lifetime, or `ProfilesPageViewModel.Describe`.

## Files/modules touched

- Created: `src/Enigma.GitClient.Desktop/ViewModels/Pages/HostAccountsViewModel.cs` (with `IHostAccountsHost`)
- Modified: `src/Enigma.GitClient.Desktop/ViewModels/Pages/ProfilesPageViewModel.cs`
- Modified: `src/Enigma.GitClient.Desktop/ViewModels/Pages/HostAccountRowViewModel.cs`
- Modified: `src/Enigma.GitClient.Desktop/Views/Pages/ProfilesPageView.axaml`
- Modified: `src/Enigma.GitClient.Desktop/DependencyInjection/ServiceCollectionExtensions.cs`
- Modified: `tests/Enigma.GitClient.Desktop.UnitTests/ProfileIntegrationsTests.cs`
- Modified: `tests/Enigma.GitClient.Desktop.UnitTests/TokenStateOnProfilesTests.cs`
- Modified: `tests/Enigma.GitClient.Desktop.UnitTests/TokenResetTests.cs`
- Modified: `tests/Enigma.GitClient.Desktop.UnitTests/AutoRefreshTests.cs`
- Modified: `tests/Enigma.GitClient.Desktop.UnitTests/CompositionRootTests.cs`
- Modified: `docs/roadmap.md`, `docs/plan/CODE-REVIEW-6E56.md`
- Created: `docs/done/CODE-REVIEW-6E56-PHASE27.md`

## Deviations & follow-ups

- **11 constructor parameters each, the plan's "fewer than ~10" by one.** The page's eleven are the
  identities' own needs plus `Accounts`. The accounts' eleven include the repository context, needed
  for the lifetime every read and write is cancelled with.
- **The host is attached once, not injected.** `AttachTo` is called by the page's constructor and
  refuses a second page. `LoadIntegrationsAsync` before any page is attached lists nothing, which only
  a token reset at startup could reach.
- **The accounts' log category is now `HostAccountsViewModel`** (it was the page's).
- **Tests reach the accounts through `page.Accounts`.** Thirty references in four test files gained
  the prefix, at the exact positions the compiler reported, and nothing else in them changed.

## Build/test evidence

- Baseline: see `docs/done/CODE-REVIEW-6E56-PHASE01.md`.
- **This dev:**
  - `dotnet build Enigma.GitClient.slnx --no-incremental`: 0 warnings (`AVLN*` included), 0 errors. The
    view uses compiled bindings, so every moved binding path is checked at build.
  - Core unit: 1534 run, 1531 passed, 0 failed, 3 skipped.
  - Core integration: 449 run, 447 passed, 0 failed, 2 skipped (6 min 30 s).
  - Desktop: compiled, **not run** (this machine's standing rule).
- **Throwaway harness outside the repository**, on the headless platform with the app's styles and the
  real container (`ValidateOnBuild`, `ValidateScopes`, a temporary configuration directory) holding
  one profile. Every check passed:
  - the container validates, and the page shows the accounts singleton;
  - the profile is listed, and its "connect" is the accounts' command;
  - the integrations load through the attached page (none; the store readable), and again on request;
  - the real `ProfilesPageView` bound to the page draws the profile card with "Connect an account",
    and no token-store band.
- **New test (Desktop, compiled, not run)** in `CompositionRootTests`: the page shows the accounts
  singleton and no longer takes the seven dependencies it handed over.
- **Fix budget:** 0 cycles.

## Documentation sweep

- Nothing stale: no user doc describes the page's structure.

## Decisions taken at build time

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| How the accounts reach the page | `AttachTo(IHostAccountsHost)`, from the page's constructor | The accounts are a bound view model with state, not command factories. One attach keeps DI acyclic | Constructor injection of the page (cycle); building the accounts inside the page (the page would take all their dependencies) |
| The busy flag during a sign-in | The page's, through the host | A sign-in disabled the page's commands before, and still does | The accounts' own flag (would let a profile be edited mid sign-in) |
| Deleting a profile's accounts | Two small methods on the accounts | The page no longer holds the account service or the links | Keeping both on the page for that one step |
| Forwarders on the page | None; tests moved to `page.Accounts` | The plan asks the tests to reach the members through the new property | Forwarding fourteen members (the page's surface would not shrink) |
