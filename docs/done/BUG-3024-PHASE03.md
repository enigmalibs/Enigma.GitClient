# BUG-3024-PHASE03 — Profiles band, browse and replace

**Item:** BUG-3024 — Unreadable tokens cannot be reset, PHASE03 — Profiles band, browse and replace
**Branch:** `bugfix/bug-3024-phase03-profiles-browse`
**Run:** bugfix/2026-10-23-crash-net-token-reset

## Summary

The user can now recover without first hitting a failure, and browsing never throws on an unreadable
token.

- **The Profiles page reads the store's status** (`ITokenRecovery.CheckAsync`) with the accounts, on every
  `LoadIntegrationsAsync`:
  - **the band:** while the whole store is unreadable, a warning band under the page header (the
    repository window's operation-banner pattern: warning brushes, icon, text) says why with the
    `Describe` sentence and offers **Reset stored tokens** (`ResetStoredTokensCommand` → the confirmed
    reset, then the lines are read again);
  - **each line's `TokenState`:**
    - `Stored`;
    - `Missing` ("No token stored: replace its token to sign in with it again.");
    - `Unreadable` ("The stored token cannot be read: replace it, or disconnect the account."), for a
      damaged entry or a store that cannot be read.

    A line with a problem shows it in the warning colour, with a **Replace token** button beside
    *Browse repositories*. The existing *Disconnect* (trash) stays.
  - a store that cannot even be looked at (an I/O error) claims nothing: no band, every line `Stored`;
  - a reset made anywhere else (the safety net, a browse) reloads the lines through
    `ITokenRecovery.TokensReset`.
- **Replace token** opens the connect dialog pre-filled with the account's host, instance and name.
  `AddHostAccountDialogViewModel.CanChooseInstance = false` disables the host box and makes the instance
  read-only. After the host validates the token, `UpdateAsync` stores it on that account: same id, the
  login the host returned, and the name typed. A store-wide failure goes through the same reset offer as
  connecting, and then stores the token. `ConnectAsync` and the new public `ReplaceTokenAsync` share one
  `SignInAsync`. The dialog itself is shown by `AskForTokenAsync`.
- **Browse repositories:**
  - `HostRepositoriesDialogViewModel.LoadAsync` catches `TokenProtectionException`. It says "The stored
    token cannot be read" in the dialog's summary and on the bar, with `Describe`, and keeps
    `TokenFailure`.
  - When the failure is store-wide, `HostRepositoryBrowser` closes the dialog first (a question on the
    shared host would replace it), then offers the reset.
  - One damaged entry stays said in the dialog ("Replace its token, or disconnect it"), and its line on
    the Profiles page offers both.

## Files / modules touched

**Created**

- `tests/Enigma.GitClient.Desktop.UnitTests/TokenStateOnProfilesTests.cs`: 8 cases.
  - A healthy store has no band and no problem.
  - The band appears for an unreadable store, and its reset leaves each line `Missing`.
  - The band's reset declined changes nothing.
  - One damaged entry is that line's problem, not the store's.
  - Replace token asks for that account (instance locked) and keeps the line.
  - Replace token in an unreadable store offers the reset, then stores.
  - A browse with an unreadable store closes the dialog and offers the reset.
  - A browse with one damaged entry says the fix and offers no reset.
- `docs/done/BUG-3024-PHASE03.md`

**Modified**

- `src/Enigma.GitClient.Desktop/ViewModels/Pages/ProfilesPageViewModel.cs`:
  - the band properties and the reset command;
  - the status read and `TokenStateOf`;
  - `ReplaceTokenCommand` / `ReplaceTokenAsync`;
  - `SignInAsync` and `AskForTokenAsync`;
  - `StoreAsync(…, replacing)`;
  - the `TokensReset` subscription.
- `src/Enigma.GitClient.Desktop/ViewModels/Pages/HostAccountRowViewModel.cs`: `HostAccountTokenState`,
  `TokenState`, `HasTokenProblem`, `TokenProblem`, `ReplaceTokenCommand`.
- `src/Enigma.GitClient.Desktop/Views/Pages/ProfilesPageView.axaml`: the band, plus the problem line and
  *Replace token* on each integration.
- `src/Enigma.GitClient.Desktop/ViewModels/Dialogs/AddHostAccountDialogViewModel.cs` and
  `Views/Dialogs/AddHostAccountDialogView.axaml`: `CanChooseInstance`.
- `src/Enigma.GitClient.Desktop/ViewModels/Dialogs/HostRepositoriesDialogViewModel.cs`: `ITokenRecovery`,
  `TokenFailure`, the catch.
- `src/Enigma.GitClient.Desktop/Services/HostRepositoryBrowser.cs`: `ITokenRecovery`, close then offer.
- `src/Enigma.GitClient.Desktop/Services/TokenRecovery.cs`: `Describe(TokenProtectionReason)`, which the
  band uses.
- `tests/Enigma.GitClient.Desktop.UnitTests/HostRepositoriesDialogTests.cs`: the helper passes the
  recovery.
- `tests/Enigma.GitClient.Desktop.UnitTests/Infrastructure/UiServiceDoubles.cs`: the double's
  `Describe(reason)`.
- `docs/roadmap.md`, `docs/plan/BUG-3024.md`: statuses.

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| Where the band sits | Docked under the page header, outside the scroll | Always in view, as the repository window's operation banner is |
| Lines in a store that cannot be read | Each one `Unreadable`, with *Replace token* | True for every line, and replacing one goes through the reset, then stores, which is the quickest way back for a single account |
| The instance during *Replace token* | Locked (`CanChooseInstance`), validated against the account's own | A replaced token is that account's; a different instance would be a different account |
| A token for another login in *Replace token* | Stored on that line, with the login the host returned | The reader chose that line; the line then says who it signs in as |
| Reloading after a reset | Through `TokensReset`, and awaited by the band's own command | Resets made elsewhere update the page; the band's own is deterministic |
| The browse dialog's state | `TokenFailure` exposed, which the browser reads | The browser owns the host, and so the order "close, then ask" |

## Deviations & follow-ups

- **No deviation from the plan.** The plan's "the Profiles page reloads its rows after a browse that
  failed this way" is met by `TokensReset` for the store-wide case. The single-entry case needs no
  reload, because the line already showed the problem when the page loaded.
- **Follow-up (cosmetic):** the add-account dialog keeps its "Change this for a self-hosted instance" hint
  while the instance is locked for *Replace token*.

## Build/test evidence

- `dotnet build Enigma.GitClient.slnx`: 0 warnings, 0 errors.
- `dotnet test --solution Enigma.GitClient.slnx`: 3418 total, 3415 passed, 3 skipped, 0 failed (+8 new).
  Green on the first run.
