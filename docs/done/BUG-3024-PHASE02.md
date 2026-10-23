# BUG-3024-PHASE02 — Reset on connect, no duplicates

**Item:** BUG-3024 — Unreadable tokens cannot be reset, PHASE02 — Reset on connect, no duplicates
**Branch:** `bugfix/bug-3024-phase02-connect-reset`
**Run:** bugfix/2026-10-23-crash-net-token-reset

## Summary

This closes the crash path reported on 2026-10-23. Before, typing a token again after the Windows
password reset threw `TokenProtectionException` out of `ConnectAsync`, and the process terminated. Now:

- **`ITokenRecovery` / `TokenRecovery`** (Desktop, singleton) holds the one wording and the one reset.
  - `Describe(exception)` gives the sentence the user reads, offering the fix:
    - store-wide: "…can no longer be read on this computer or by this account — a Windows password reset
      does this. Reset the stored tokens, then give each integration its token again.";
    - the file: "…cannot be read. Reset the stored tokens…";
    - one entry: "…Replace its token, or disconnect it."
  - `ConfirmAndResetAsync()` asks `ConfirmDestructiveAsync("Reset stored tokens", …)` (red, harmless
    default). The question says that every stored token is deleted, that each connected account then
    needs its token again, and that nothing on the host is touched (plus the integrations stay listed,
    and it cannot be undone). On yes it calls `ITokenStore.ResetAsync`, says "Stored tokens reset" and
    raises `TokensReset`. An I/O failure is reported and answers `false`.
  - `CheckAsync()` is for PHASE03. `FindIn(exception)` finds a `TokenProtectionException` inside
    wrappers.
- **`HostAccount.IsSameIdentityAs`** (Core): same kind, same instance (scheme, host, port and path,
  ignoring case and a trailing `/`), same login (ignoring case) and same profile.
- **`ProfilesPageViewModel.ConnectAsync`**, once the host has accepted the token, goes through a new
  `StoreAsync`:
  - an account already listed with the same identity gets the new token through `UpdateAsync`. It keeps
    its id, and its name changes only if one was typed. The report reads "Token replaced for GitHub".
    There is no second line;
  - otherwise the account is added, as before;
  - a store-wide `TokenProtectionException` offers the reset. Confirmed, the token just typed is stored
    without a second validation or dialog (one retry only). Declined, "The token was not stored" with the
    `Describe` sentence;
  - any other `TokenProtectionException` is reported the same way;
  - nothing reaches the safety net. `IOException` / `UnauthorizedAccessException` while writing the
    account list are reported too, as the other account operations already did.
- **The safety net:** when the error shown is, or wraps, a store-wide `TokenProtectionException`, the
  dialog says what fixes it (`Remedy` in `UnhandledErrorViewModel`, a line in the view) and offers
  *Reset stored tokens* as its primary button. The confirmation then follows on the host the dialog has
  just freed. A damaged single entry, or any other exception, gets no reset button.
- **README:** "disconnect the account and connect it again" becomes "connect the account again; the
  integration already listed takes it".

## Files / modules touched

**Created**

- `src/Enigma.GitClient.Desktop/Services/TokenRecovery.cs`
- `tests/Enigma.GitClient.Core.UnitTests/Hosting/HostAccountIdentityTests.cs`: 9 cases.
- `tests/Enigma.GitClient.Desktop.UnitTests/TokenResetTests.cs`: 9 cases.
  - Connecting again after the key became unreadable: one reset question naming the three facts, then
    the typed token on the same account.
  - The reset declined: nothing deleted or stored, and the error offers the reset.
  - A new account added once the reset is confirmed, the old one listed with no token.
  - The same account connected again: same id, new token, the name kept.
  - A typed name renames.
  - Another profile or another login makes a second line.
  - The safety net offers the reset for a store-wide token failure, and not for a damaged entry or any
    other error.
- `docs/done/BUG-3024-PHASE02.md`

**Modified**

- `src/Enigma.GitClient.Core/Hosting/HostModel.cs`: `IsSameIdentityAs`.
- `src/Enigma.GitClient.Desktop/ViewModels/Pages/ProfilesPageViewModel.cs`: `ITokenRecovery`,
  `StoreAsync`, the reports, the I/O catch.
- `src/Enigma.GitClient.Desktop/Services/UnhandledErrorReporter.cs`: `ITokenRecovery`, the reset button
  and its confirmation.
- `src/Enigma.GitClient.Desktop/ViewModels/Dialogs/UnhandledErrorViewModel.cs`,
  `src/Enigma.GitClient.Desktop/Views/Dialogs/UnhandledErrorView.axaml`: `Remedy`.
- `src/Enigma.GitClient.Desktop/DependencyInjection/ServiceCollectionExtensions.cs`: `ITokenRecovery`.
- `tests/Enigma.GitClient.Desktop.UnitTests/Infrastructure/UiServiceDoubles.cs`: `RecordingTokenRecovery`.
- `tests/Enigma.GitClient.Desktop.UnitTests/UnhandledErrorReporterTests.cs`: the harness passes a recovery.
- `README.md`: the reconnect sentence (documentation sweep).
- `docs/roadmap.md`, `docs/plan/BUG-3024.md`: statuses.

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| How often the reset is offered in one connect | Once; a second store-wide failure after a reset is reported | A failure that survives the reset is not one the reset fixes, and asking again would loop |
| The safety net's reset | Primary button "Reset stored tokens", then the same confirmation | One wording and one confirmation everywhere; the reset is never one click away from the error |
| Login casing kept on a replace | The listed account's | The identity matched without regard to case; nothing visible changes but the token |
| I/O failures while storing | Reported on the bar | The neighbouring operations (disconnect, move) already do; otherwise they would reach the safety net |
| README | Minimal edit of the reconnect advice | Disconnecting first is no longer needed, and it throws the integration's name away |

## Deviations & follow-ups

- **No deviation from the plan's steps.** The confirmation's wording adds that the integrations stay
  listed and that the reset cannot be undone, as the other destructive questions in the application do.
- **One fix cycle:** the first build failed on a test that called `Clear()` on the read-only list of
  info-bar notices. The call was not needed and was removed.
- **Follow-up (PHASE03):** a row for an account whose token is missing after a reset, and *Replace token*
  on it.

## Build/test evidence

- `dotnet build Enigma.GitClient.slnx`: 0 warnings, 0 errors.
- `dotnet test --solution Enigma.GitClient.slnx`: 3410 total, 3407 passed, 3 skipped, 0 failed (+18 new:
  9 Core, 9 Desktop).
