# BUG-3024-PHASE04 — Visible git sign-in fallback

**Item:** BUG-3024 — Unreadable tokens cannot be reset, PHASE04 — Visible git sign-in fallback
**Branch:** `bugfix/bug-3024-phase04-git-fallback-notice`
**Run:** bugfix/2026-10-23-crash-net-token-reset

## Summary

On 2026-10-23, a fetch whose integration token could not be read showed Git Credential Manager's
Windows prompt with no explanation. The resolver had swallowed the failure with a log warning. Git still
falls back to its own sign-in, since this is a login and not a permission, but the repository window now
says why, once.

- **Core, `GitCredentialResolver`:**
  - `BuildAsync` reads each sign-in's token on its own. A `TokenProtectionException` is logged as a
    Warning with its reason and raises **`IGitCredentialResolver.TokenUnreadable`**
    (`TokenUnreadableEventArgs`: account, origin, reason). That origin is left to git, and the other
    integrations keep their logins. Previously one unreadable token dropped every login of the
    repository.
  - The outer catches are unchanged, so it still fails open.
  - Clones go through the same `BuildAsync`.
- **Core, `ITokenStore.Changed`** (`TokenStoreChangedEventArgs`: `Written`, `Deleted` or `Reset`, and the
  key) is raised by `FileTokenStore` after the gate is released. It fires only for a change that happened:
  not for a delete that deleted nothing, nor for a write that threw.
- **Desktop, `GitSignInNoticeViewModel`** (a singleton for the session):
  - It follows both events on the UI thread.
  - It says "The stored token for <host> could not be read, so git is using its own sign-in." For one
    damaged entry it adds "Replace its token on the Profiles page", and that is said once per integration.
    For a store that cannot be read at all it adds "…reset them, then give each integration its token
    again" with **Reset stored tokens**, said once for every integration, because one reset fixes them
    all.
  - It always offers **Open Profiles** and a dismiss. A dismissed notice stays away for the session, so
    repeated automatic fetches say nothing more.
  - It goes away by itself when the problem is resolved:
    - a reset (forgets everything said);
    - that integration's token written again, or the integration disconnected;
    - for a store-wide notice, any token written, because a store that takes a token can be read again.
- **`MainWindowViewModel.SignInNotice`**, and a warning band in `MainWindow.axaml` under the operation
  banner, in the same pattern: key icon, text, the three buttons, and `AutomationProperties.Name` on each.

## Files / modules touched

**Created**

- `src/Enigma.GitClient.Desktop/ViewModels/GitSignInNoticeViewModel.cs`
- `tests/Enigma.GitClient.Desktop.UnitTests/GitSignInNoticeTests.cs`: 7 cases.
  - Repeated fetches say it once, and a dismissed notice stays away.
  - A store that cannot be read offers the reset and is said once for all integrations.
  - The reset puts the notice away and forgets it.
  - The integration's token stored again puts its notice away, and another's does not.
  - A token stored in a store that could not be read puts the store-wide notice away.
  - Open Profiles goes to the Profiles page.
  - The repository window carries the notice.
- `docs/done/BUG-3024-PHASE04.md`

**Modified**

- `src/Enigma.GitClient.Core/Hosting/GitCredentialResolver.cs`: `TokenUnreadableEventArgs`, the event,
  the per-sign-in catch, a remark.
- `src/Enigma.GitClient.Core/Security/TokenStore.cs`: `TokenStoreChange`, `TokenStoreChangedEventArgs`,
  `ITokenStore.Changed` and where it is raised.
- `src/Enigma.GitClient.Desktop/ViewModels/MainWindowViewModel.cs`: `SignInNotice`.
- `src/Enigma.GitClient.Desktop/Views/MainWindow.axaml`: the band.
- `src/Enigma.GitClient.Desktop/DependencyInjection/ServiceCollectionExtensions.cs`: the notice.
- `tests/Enigma.GitClient.Core.IntegrationTests/Hosting/GitCredentialResolverTests.cs`: 3 cases.
  - One damaged integration among two leaves git that origin only, and the event names it.
  - An unopenable key raises `KeyUnavailable`.
  - A clone says so too.
- `tests/Enigma.GitClient.Core.UnitTests/Security/TokenStoreTests.cs`: 2 cases.
  - Every change is said after it is made.
  - A write that fails says nothing.
- `IGitCredentialResolver` test doubles in `RepositoryServiceTests`, `SignedInOperationsTests` and
  `SyncProgressDeliveryTests`: an event with empty accessors.
- `docs/roadmap.md`, `docs/plan/BUG-3024.md`: statuses; the item's own row is now `DONE`.

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| One notice or one per integration | One at a time. Per integration for a damaged entry, once for all for an unreadable store | One reset fixes the whole store; a notice per integration would repeat itself |
| What clears a store-wide notice | A reset, or any token written | A store that takes a token can be read again; nothing else proves it |
| Where the notice lives | A session singleton, built with the repository window's ViewModel | What it said stays said from one repository window to the next; the start window fetches nothing |
| Icon | Phosphor *Key* | It is about a sign-in, beside the operation banner's *Warning* |
| Thread | `Dispatcher.UIThread.Post` from both events | Core raises them on the thread-pool thread its resolution or write ran on |

## Deviations & follow-ups

- **No deviation from the plan.**
- **Manual check (Windows), not performed:** this run executed on Linux, where DPAPI does not exist. The
  steps stay for a Windows run before tagging:
  1. Back up `%APPDATA%\Enigma.GitClient`, close the app, and overwrite `tokens.key` with random bytes.
  2. Connect: the reset is offered, and the connect then works.
  3. Fetch: the notice is shown.
  4. Force an exception from a command: the dialog appears and the app keeps running.

  Their automated equivalents ran green here:
  - step 2: `TokenResetTests.ConnectingAgain_AfterThePasswordReset_OffersTheReset_ThenStoresTheTypedToken`,
    with a three-byte key file, which is what an unopenable key looks like on Linux;
  - step 3: `GitCredentialResolverTests.AKeyThatCannotBeOpened_SaysSo_…` and `GitSignInNoticeTests`;
  - step 4: `UnhandledErrorReporterTests` and `UnhandledErrorPolicyTests`.
- **Follow-up:** the notice is not shown for a clone started from the start window, because no repository
  window is open. The clone itself still runs with git's own sign-in, as before.

## Build/test evidence

- `dotnet build Enigma.GitClient.slnx`: 0 warnings, 0 errors.
- `dotnet test --solution Enigma.GitClient.slnx`: 3430 total, 3427 passed, 3 skipped, 0 failed (+12 new:
  3 Core integration, 2 Core unit, 7 Desktop). Green on the first run.
