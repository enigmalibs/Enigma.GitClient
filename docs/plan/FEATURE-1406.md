# FEATURE-1406 — Profiles that own their integrations

**Status:** IN PROGRESS
**Type:** FEATURE
**Branch:** one per phase, see below
**Run:** feature/2026-09-27-diff-profiles-release

## Objective

The hosting integrations (GitHub, GitLab, Azure DevOps accounts) belong to a **profile**, so each
profile can have its own accounts — and a profile with no integration is **local only**: it never
pushes. The **Integrations** page disappears; integrations are connected, browsed and disconnected on
the **Profiles** page, which is what the **Identity** page becomes (renamed in the view and in the
code).

## Context & constraints

- A profile (`Core/Identity/IdentityProfile`, stored in `identity-profiles.json`) is a label, a name
  and an email. It is *current* when it matches the identity git has; no active profile is stored.
- Accounts (`Core/Hosting/HostAccount`, stored in `host-accounts.json`, tokens encrypted apart in the
  token store) belong to nothing today. They serve the Integrations page (connect with a validated
  token, list an account's repositories, clone, open on the host) and `HostLinkService`, which uses
  every account to recognise a self-hosted instance for "Open on …" links.
- Pushing never uses an integration's token: git's own credential helper or SSH key authenticates.
  The tokens are read-scoped by design (README, *Connecting a host*). Every push the app runs goes
  through `ISyncOperations.PushAsync` / `PushBranchAsync`.
- The Identity and Integrations pages are on both rails (start window, repository window).
- Several instances share the configuration files; both stores re-read before they write.

## Decisions

| Decision | Rationale |
|---|---|
| `HostAccount.ProfileId` (`null` = connected before profiles owned integrations); `host-accounts.json` version 2 | An account belongs to one profile; the field is additive, so 1.x builds still read the file |
| A push goes ahead only when the repository's profile has an integration whose host owns the push remote (`HostAccount.Owns`); a profile with no integrations never pushes | The draft's rule, applied per remote: an integration is what connects a profile to a host |
| The repository's profile is the one matching the identity git commits with there — every scope, conditional includes included (`git config --get-regexp` in the work tree) | Consistent with how *current* already works, and true after a terminal change |
| No profile matches that identity → no check | People who do not use profiles keep today's behaviour |
| The integration is the permission, not the credential: git keeps authenticating | Tokens are read-scoped by design; using them for push is a different feature |
| Accounts without a profile are shown as **Earlier integrations**, with *Move to* a profile and *Disconnect*; they count for no profile until moved | Credentials are never assigned to a profile by guesswork, and the no-profile case is covered |
| Deleting a profile disconnects its integrations and deletes their tokens, after a confirmation that says so | A forgotten account never leaves a live credential behind (the store's own rule) |
| Browsing an account's repositories moves into a dialog opened from its row | The page that held it disappears; the dialog pattern is the app's own (tool dialogs, diffs) |
| Renamed: the page's view and ViewModel, its row ViewModel, `ShellPage`/`StartPage.Identity`, headers and tests. Kept: Core `IdentityProfile`/`GitIdentity`, the `Identity` namespace, the profile dialog, `identity-profiles.json` | The draft renames the page; the Core types model git's identity and the file is user data |

## PHASE01 — Identity becomes Profiles

**Status:** DONE — see `docs/done/FEATURE-1406-PHASE01.md`
**Branch:** `feature/feature-1406-phase01-profiles-page`

### Steps

1. `git mv` `IdentityPageViewModel.cs` → `ProfilesPageViewModel.cs` and `IdentityPageView.axaml(.cs)` →
   `ProfilesPageView.axaml(.cs)`; rename the types (`ProfilesPageViewModel`, `ProfileRowViewModel`,
   `ProfilesPageView`).
2. `ShellPage.Identity` → `ShellPage.Profiles`, `StartPage.Identity` → `StartPage.Profiles`; the rail
   items read **Profiles**; the page's title is **Profiles**.
3. DI registrations, comments and every reference follow.
4. Tests: `IdentityPageTests.cs` → `ProfilesPageTests.cs` (class renamed); the rail, composition-root and
   render tests name the new types and headers.

### Acceptance criteria

- Both rails read *Profiles, Integrations, Settings*; the page's header says *Profiles*.
- No type, file or enum value in `src/` or `tests/` is named after the Identity page any more.
- Build clean with zero warnings; the whole suite green.

## PHASE02 — Accounts belong to a profile

**Status:** DONE — see `docs/done/FEATURE-1406-PHASE02.md`
**Branch:** `feature/feature-1406-phase02-accounts-in-profiles`

### Steps

1. `Core/Hosting/HostModel.cs` — `HostAccount` gains `ProfileId` (constructor parameter, JSON
   `profileId`, optional), `IsUnassigned`, `BelongsTo(string profileId)`, `ForProfile(string?
   profileId)`; `HostAccount.Create` takes the profile.
2. `Core/Hosting/HostAccountService.cs` — `CurrentVersion = 2`; `AssignAsync(accountId, profileId)`
   moves an account; `RemoveForProfileAsync(profileId)` disconnects every account of a profile and
   deletes their tokens.
3. Tests (`Core.UnitTests/Hosting/HostAccountServiceTests.cs`): the profile round-trips; a version-1 file
   reads as unassigned; `AssignAsync` moves one account and leaves the rest; `RemoveForProfileAsync`
   removes only that profile's accounts and tokens.

### Acceptance criteria

- A version-1 `host-accounts.json` still loads, every account unassigned.
- Build clean with zero warnings; the whole suite green, including the new tests.

## PHASE03 — Browse repositories in a dialog

**Status:** DONE — see `docs/done/FEATURE-1406-PHASE03.md`
**Branch:** `feature/feature-1406-phase03-browse-dialog`

### Steps

1. `ViewModels/Dialogs/HostRepositoriesDialogViewModel.cs` — the listing moved out of
   `IntegrationsPageViewModel`: the account, All/Public/Private, the filter, the paged listing
   (`PageLimit`), the summary (which also carries a failure), Open and Clone; `HostRepositoryRowViewModel`
   moves with it.
2. `Views/Dialogs/HostRepositoriesDialogView.axaml` — the listing pane, sized for a dialog.
3. `Services/HostRepositoryBrowser.cs` — `IHostRepositoryBrowser.BrowseAsync(HostAccount)` shows the
   dialog; Clone closes it and then runs the clone (`RepositoriesPageViewModel.RunCloneAsync`).
   Registered in DI with the dialog view.
4. The Integrations page keeps its account list; each row gains *Browse repositories*; the page's
   listing pane and its toolbar-refresh hook go.
5. Tests: the listing tests move to `HostRepositoriesDialogTests.cs` (listing, paging, filter,
   visibility, failure, open, clone closes the dialog and clones); the integrations page tests keep
   connect/disconnect.

### Acceptance criteria

- An account's repositories are listed, filtered, opened and cloned from the dialog.
- Build clean with zero warnings; the whole suite green.

## PHASE04 — Integrations inside each profile

**Status:** TODO
**Branch:** `feature/feature-1406-phase04-profile-integrations`

### Steps

1. `ProfilesPageViewModel` — every profile row lists its integrations (`HostAccountRowViewModel`) with
   *Connect an account* (the existing dialog; the account is stored under that profile), *Browse
   repositories* and *Disconnect*. An **Earlier integrations** card lists unassigned accounts with
   *Move to <profile>* and *Disconnect*, and says to add a profile when there is none.
2. Deleting a profile names its integrations in the confirmation and disconnects them
   (`RemoveForProfileAsync`); `HostLinkService.RefreshAsync` after every change.
3. The Integrations page goes: its view and ViewModel, `ShellPage`/`StartPage.Integrations`, the rail
   items and the DI registrations.
4. Tests: connecting (validated first, refused token never stored, rate limit, own display name),
   disconnecting, moving an earlier integration, a profile's deletion taking its integrations and
   tokens, accounts surviving a restart, and a paint test — in `ProfilesPageTests`; the rail tests read
   *Profiles, Settings*.

### Acceptance criteria

- Integrations are connected, browsed, moved and disconnected on the Profiles page only.
- Both rails read *Profiles, Settings*.
- Build clean with zero warnings; the whole suite green, including the new tests.

## PHASE05 — A profile pushes only where it may

**Status:** TODO
**Branch:** `feature/feature-1406-phase05-push-guard`

### Steps

1. `Core/Identity/GitIdentityService.cs` — `GetEffectiveAsync(repository)`: the identity git commits
   with in the repository (`git config -z --get-regexp ^user\.(name|email)$`, no scope flag, last value
   wins).
2. `Core/Hosting/ProfilePushRule.cs` — a pure rule: profiles, accounts, identity and the push URL in, a
   verdict out (no profile → allowed; the profile has an integration owning the URL → allowed;
   otherwise refused, with the profile and the host).
3. `App/Services/PushGuard.cs` — `IPushGuard.CheckAsync(repository, remote)` reads the profiles, the
   accounts, the effective identity and the remote's push URL, and applies the rule; it fails closed.
   `SyncOperations.PushAsync` / `PushBranchAsync` refuse a push the guard refuses, with a warning that
   names the profile and the host and points at the Profiles page.
4. The Profiles page says *Local only — never pushes* on a profile without integrations.
5. Tests: the rule (every branch, SSH and HTTPS remotes, an unassigned account not counting, an email
   matched regardless of case); the effective identity (a repository's own over the global one); a
   push refused under a local-only profile leaves the remote untouched; without profiles, pushes work as
   before.

### Acceptance criteria

- Under a profile without an integration for the remote's host, no push runs and the reason is shown.
- Under a profile with one, and without any profile, pushes run as before.
- Build clean with zero warnings; the whole suite green, including the new tests.

## Out of scope

- Authenticating pushes with an integration's token, or asking for a push scope.
- Checking fetch and pull.
- Storing an explicit active profile, or switching profiles per repository beyond git's own identity.
- Renaming the Core identity types, the profile dialog or `identity-profiles.json`.

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| How an integration governs pushing | Per remote: the profile needs an integration owning the push remote's host | "Their own GitHub accounts" and "local only" are one rule; a Work profile must not push to a host only Personal is connected to | Per profile (any integration unlocks every remote) |
| Which profile a repository uses | The profile matching the identity git commits with there | The app's existing notion of *current*; survives terminal edits; per-repository identities already exist | A stored active profile; the global identity only |
| No profile matches | No check | Users without profiles keep working exactly as in 1.1 | Refusing every push |
| Push credentials | git's own, unchanged | The tokens are read-scoped by design; injecting them changes authentication for every push | A credential helper fed with the token |
| Earlier (unassigned) accounts | Listed apart, with Move and Disconnect; they count for no profile | No guessing about whose credential it is; works with zero profiles | Adopted by the current or the first profile automatically |
| Deleting a profile | Disconnects its integrations and deletes their tokens, confirmed first | The store's rule: no live credential left behind | Leaving them unassigned |
| Repository browsing | A dialog per integration | The page is gone; the app already moved its tool pages into dialogs | A card on the Profiles page with an account picker; dropping the feature |
| The guard cannot read what it needs | Refuse (fail closed) | "Never able to push" is a guarantee | Allowing the push |
| Remotes no integration can own (a path, an unknown server) | Refused under a profile | The rule has no exception to explain | Exempting local paths |
| Fetch and pull | Not checked | The draft says push; reading changes nothing on the remote | Checking every transfer |
| Rename scope | The page and its navigation; Core types and files kept | The draft names the page; renaming user-data files breaks upgrades | Renaming `IdentityProfile` and `identity-profiles.json` |
| Rail icon | `IdentificationCard`, unchanged | Still reads as "who you are"; no reason to move it | A new icon |
