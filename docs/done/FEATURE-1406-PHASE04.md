# FEATURE-1406-PHASE04 — Integrations inside each profile

**Item:** FEATURE-1406 — Profiles that own their integrations
**Phase:** PHASE04 — Integrations inside each profile
**Branch:** `feature/feature-1406-phase04-profile-integrations`
**Run:** feature/2026-09-27-diff-profiles-release

## Summary

Integrations now live on the **Profiles** page, under the profile they belong to, and the
**Integrations** page is gone from both rails. Each rail's footer reads *Profiles, Settings*.

- Every profile lists its own integrations. Each one shows the account, its host and its login, with
  *Browse repositories* and *Disconnect*. Under them, *No integrations.* when there are none, and
  *Connect an account*.
- Connecting runs the existing account dialog, now titled *Connect an account to <profile>*. The
  host still validates the token before anything is stored, and the account is stored under that
  profile's id.
- **Earlier integrations** is a card that appears only while some integration belongs to no profile.
  That covers:
  - one connected with 1.x (unassigned);
  - one whose profile is gone, deleted in another window or by hand.

  Each such integration offers *Move to…*, a menu of the profiles, plus *Browse repositories* and
  *Disconnect*. With no profile at all, the card says to add one. Nothing is ever placed by
  guesswork: an added profile is offered in the menu, but no integration is given to it.
- Moving keeps the account and its token (`AssignAsync`).
- Deleting a profile:
  - the confirmation now names what goes with it (*Its integration, Work GitHub, is disconnected and
    its token deleted.*, or the count for several);
  - the profile is removed first, then its accounts with their tokens (`RemoveForProfileAsync`);
  - if the second step fails, those accounts show up as earlier integrations, still removable, and the
    failure is reported.
- After every change of accounts, `HostLinkService` works out the open repository's host again.
- Browsing and cloning moved here from the Integrations page unchanged: the dialog from PHASE03, then
  `RunCloneAsync`.

## Files / modules touched

**Created**

- `src/Enigma.GitClient.App/ViewModels/Pages/HostAccountRowViewModel.cs` — the integration row (moved
  out of the Integrations page's file, now owned by the Profiles page, with `Details` and `MoveTargets`)
  and `AccountMoveTargetViewModel`

**Deleted**

- `src/Enigma.GitClient.App/ViewModels/Pages/IntegrationsPageViewModel.cs`
- `src/Enigma.GitClient.App/Views/Pages/IntegrationsPageView.axaml(.cs)`

**Renamed**

- `tests/…/IntegrationsPageTests.cs` → `ProfileIntegrationsTests.cs`, rewritten for the Profiles page

**Modified**

- `src/Enigma.GitClient.App/ViewModels/Pages/ProfilesPageViewModel.cs`:
  - `ProfileRowViewModel.Integrations`, `HasIntegrations`, `ConnectCommand`;
  - `EarlierIntegrations`, `LoadIntegrationsAsync`, `ConnectAsync(profile, model)`;
  - the connect, disconnect, browse and move commands;
  - profile deletion taking its integrations;
  - five new dependencies (accounts, registry, links, browser, the repositories page for the clone).
- `src/Enigma.GitClient.App/Views/Pages/ProfilesPageView.axaml` — the integration row template, each
  profile's integrations, the *Earlier integrations* card, the Profiles card's description
- `src/Enigma.GitClient.App/Navigation/ShellNavigation.cs`, `StartNavigation.cs` —
  `ShellPage/StartPage.Integrations` and their rail items removed
- `src/Enigma.GitClient.App/DependencyInjection/ServiceCollectionExtensions.cs` — the page's
  registrations removed
- Comments naming the Integrations page: `MainWindowViewModel.cs`, `RepositoriesPageViewModel.cs`,
  `StartWindowViewModel.cs`, `Views/StartWindow.axaml(.cs)`
- Tests:
  - `ProfileIntegrationsTests.cs` — 18 tests:
    - the four account-dialog tests, kept;
    - connecting under the profile, and the dialog naming it;
    - a refused token, a rate limit, and the account's own name;
    - each profile lists its own and the rest are earlier (unassigned and orphaned);
    - moving one; no profile to move to; an added profile offered and not given;
    - browsing opens the dialog for that account;
    - disconnecting;
    - deleting a profile takes its integrations and no one else's;
    - they come back after a restart;
    - the view: its texts, one *Connect* per profile, *Browse*/*Disconnect* per integration, and
      *Move to* on the earlier one only, plus a snapshot.
  - `AutoRefreshTests.cs` — the "no refresh lists a host's repositories" test, on the Profiles page.
  - `AppWindowsTests.cs`, `MainWindowShellTests.cs` — the rails read *Profiles, Settings*.
  - `CompositionRootTests.cs`, `ShellRenderTests.cs` — the Integrations page's rows removed.
- `README.md` — docs sweep:
  - the features list: integrations connected under a profile, and the Profiles page carrying them;
  - *Connecting a host*: where integrations are connected, and the earlier integrations to move;
  - *Where your things are kept*: `host-accounts.json` holds each account's profile.
- `docs/roadmap.md`, `docs/plan/FEATURE-1406.md` — statuses

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| An account whose profile no longer exists | Listed with the earlier integrations | It must stay visible and removable. It counts for no profile either way |
| Order when deleting a profile | The profile, then its accounts; a failure of the second is reported and leaves them under *Earlier integrations* | Never an account under a profile nobody can see |
| *Move to* | A small button with a menu of the profiles (`MenuFlyout` + `ItemContainerTheme`, as the history's row menu does) | Any number of profiles, one control. A button per profile does not scale |
| Where the connect tests went | A file of their own, `ProfileIntegrationsTests` | `ProfilesPageTests` already holds the identity's tests (1,000+ lines). The two concerns read better apart |
| A profile's integrations when it is deleted from another window | Shown as earlier on the next load | The store is re-read on every load, and the page never assumes |

## Deviations & follow-ups

- The *Browse → clone* call is not covered by a test, as in PHASE03: the clone targets the user's
  home directory. The wiring up to the dialog is covered (`BrowsingOpensTheRepositoriesDialogForThatAccount`).
- The *Local only* wording on a profile without integrations comes with the push check in PHASE05, as
  planned. Until then the row says *No integrations.*
- Documentation sweep: `README.md`, as listed above.

## Build / test evidence

- `dotnet build Enigma.GitClient.slnx`: 0 warnings, 0 errors, and no `AVLN` XAML warnings.
- `dotnet test --solution Enigma.GitClient.slnx`: **2254 passed**, 0 failed, on the first run. That is
  2250 before: the old 10 integrations-page tests became 18, and 4 page-specific data rows went with
  the page.
- A rendered snapshot of the page, with two profiles, one integration and one earlier integration, was
  checked by eye (`snapshots/profiles-integrations.png`, beside the test assembly).
