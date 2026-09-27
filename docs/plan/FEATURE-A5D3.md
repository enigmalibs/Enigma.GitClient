# FEATURE-A5D3 — Info bars that never block

**Status:** IN PROGRESS
**Type:** FEATURE
**Branch:** one per phase, see below
**Run:** feature/2026-09-27-infobars-dialogs-diff-release

## Objective

An info bar must never hold up what the application does next. Today every notification is
awaited, and the task `IInfoBarService.ShowAsync` returns completes only when the bar closes — so
whatever follows the report (a refresh, the end of a busy state, the result an operation hands
back to its caller) waits until the user dismisses the bar. After this item:

- **Success and info** bars close themselves after **5 seconds** and block nothing.
- **Warning and error** bars stay until the user closes them, as today — but block nothing either.

It uses the timed info bars of **Enigma.Avalonia.Desktop 1.1.0**.

## Context & constraints

- `IInfoBarService.ShowAsync(Action<InfoBar>?)` resets the single host bar, applies the
  configuration, and returns `InfoBar.ShowAsync()`, a task completed by the bar's `Closed` event.
  Every call site in the App awaits it: 15 private `ReportAsync`/`ShowAsync` helpers (one per
  operations service or page ViewModel) and five direct calls (`MainWindowViewModel`,
  `SettingsPageViewModel`, `HistoryPageViewModel`, `AppWindows`, `BranchDropOperations`) — about
  72 awaited reports.
- Examples of the blocking: `HistoryPageViewModel`'s load keeps `IsBusy` set until a "history could
  not be read" error is closed; `MergeOperations.AbortAsync`/`ContinueAsync` return — and so let the
  caller refresh — only after the "Merge abandoned" / "Merge committed" bar is closed.
- 1.1.0 (on nuget.org) adds `InfoBar.DisplayDuration` (`TimeSpan?`, `null` = stays open) and the
  extension `IInfoBarService.ShowAsync(TimeSpan displayDuration, Action<InfoBar>? configure)`, which
  applies the duration after `configure`. `InfoBarService` clears `DisplayDuration` between
  messages, so a timed message never makes the next one timed; a new message on the open bar
  restarts or cancels the countdown.
- 1.1.0 is built against the same Avalonia 12.1.1 and CommunityToolkit.Mvvm 8.4.2 as 1.0.0, so the
  version-coupled set in `Directory.Packages.props` does not move. `Enigma.Icons.Avalonia` stays at
  1.0.0 (its latest).
- `InfoBarService.ShowAsync` throws `InvalidOperationException` when no host is registered — inside
  an `async` method, so it arrives as an already-faulted task. Awaiting it surfaced the exception at
  the call site; a non-blocking call must not turn that into an unobserved task.
- App code awaits with `.ConfigureAwait(true)` throughout (the repository's own policy).
- Tests replace the service with `RecordingInfoBarService` (`tests/…/Infrastructure/UiServiceDoubles.cs`),
  which records title, message and severity and returns a completed task — so no existing test can
  see the blocking.

## Decisions

| Decision | Rationale |
|---|---|
| One extension, `IInfoBarService.Notify(string title, string message, InfoBarSeverity severity)`, in a C# 14 extension block (`Services/InfoBarServiceExtensions.cs`, namespace `Enigma.GitClient.App.Services`) | One place holds the rule "success/info are timed, warning/error stay"; stateless, so no DI change; every consumer already holds an `IInfoBarService` |
| `Notify` returns `void`: it starts the bar and does not wait for it to close | The whole point; a `Task`-returning helper invites the next caller to await it again |
| A bar that could not be shown at all (the task is already faulted) rethrows at the call site | Keeps today's behaviour for a wiring error instead of hiding it in an unobserved task |
| 5 seconds, one constant | The draft's value; a constant rather than a setting (nobody asked for a preference) |
| The per-class helpers become `private void Report(...)` (the `MergeOperations` pair likewise) forwarding to `Notify`; direct calls become `Notify` | Smallest diff per class, and call sites read the same |
| `RecordingInfoBarService` records `DisplayDuration`, and gains a `HoldsOpen` mode whose `ShowAsync` completes only when `HideAsync` is called — as the real bar does | Lets a test prove an operation or a page finishes while its bar is still open |

## PHASE01 — The helper, in the operations

**Status:** DONE — see `docs/done/FEATURE-A5D3-PHASE01.md`
**Branch:** `feature/feature-a5d3-phase01-non-blocking-operations`

### Steps

1. `Directory.Packages.props` — `Enigma.Avalonia.Desktop` `1.0.0 → 1.1.0`; update the comment above
   the coupled set (12.1.1 is what 1.1.0 is built against too).
2. `src/Enigma.GitClient.App/Services/InfoBarServiceExtensions.cs` — the `Notify` extension and the
   5-second constant, documented.
3. `tests/…/Infrastructure/UiServiceDoubles.cs` — `RecordedNotification` gains `DisplayDuration`;
   `RecordingInfoBarService` records it and gains `HoldsOpen`.
4. Move every operations service onto `Notify`: `BranchOperations`, `TagOperations`,
   `SyncOperations`, `CheckoutOperations`, `MergeOperations`, `ResetOperations`,
   `BranchDropOperations`, `HostLinkService`, `AppWindows`. Remove any `async` that only existed for
   the report.
5. Tests (`App.UnitTests/InfoBarNotificationTests.cs`):
   - `Notify` with Success and Info records a 5-second duration; with Warning and Error, none.
   - `Notify` against the real `InfoBarService` and a realised host: the bar is open, its duration is
     as above, and a timed message followed by an error leaves the error untimed.
   - `Notify` before a host is registered throws `InvalidOperationException` at the call.
   - With `HoldsOpen`, a merge that succeeds (and one that is abandoned) returns while its bar is still
     open.

### Acceptance criteria

- Enigma.Avalonia.Desktop 1.1.0 is restored and used; nothing else in the coupled set moves.
- No operations service awaits an info bar; success/info reports are timed at 5 s, warning/error
  reports are untimed.
- Build clean with zero warnings; the whole suite green, including the new tests.

## PHASE02 — Every page on the helper

**Status:** TODO
**Branch:** `feature/feature-a5d3-phase02-non-blocking-pages`

### Steps

1. Move every page and shell ViewModel onto `Notify`: `MainWindowViewModel`,
   `HistoryPageViewModel`, `ChangesPageViewModel`, `RemotesPageViewModel`, `IdentityPageViewModel`,
   `IntegrationsPageViewModel`, `RepositoriesPageViewModel`, `ConflictResolutionPageViewModel`,
   `SettingsPageViewModel`. Remove any `async` that only existed for the report.
2. Confirm nothing in `src/` awaits `IInfoBarService.ShowAsync` any more (a search, recorded in the
   completion doc).
3. Tests: with `HoldsOpen`, the history page's failed load clears `IsBusy` while its error bar is
   still open; the settings page's reset completes while its info bar is still open; a page's
   success report is recorded with the 5-second duration.

### Acceptance criteria

- Nothing in the application awaits an info bar.
- A failed history load ends its busy state at once, whether or not the error is closed.
- Build clean with zero warnings; the whole suite green, including the new tests.

## Out of scope

- Queuing or stacking several bars (the library has one host; the newest message replaces the
  current one, as today).
- Pausing the countdown on hover, or a preference for the duration.
- Changing any message text or severity.

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| How to stop blocking | A `void Notify` extension on `IInfoBarService` | One rule in one place, no DI change, call sites stay one line | `_ = ReportAsync(...)` discards at ~72 call sites (the rule repeated, and the faulted-task case lost); a new `INotificationService` wrapper (a service and a registration for a stateless rule) |
| Which bars close themselves | Success and Info after 5 s; Warning and Error stay | The draft, verbatim | Timing warnings too |
| A bar that cannot be shown | Rethrow at the call site | Same failure mode as today; a missing host is a wiring bug that must be loud | Swallow it; log it |
| Where the package bump lands | PHASE01 | The first dev that uses 1.1.0 | A separate dependency dev (a one-line commit with nothing to exercise it) |
| Phase split | Operations services, then pages | Two reviewable commits of the same mechanical change; PHASE01 carries the helper and its tests | One commit touching ~20 files; one dev per class |
| How to test "non-blocking" | A hold-open mode on the recording double, mirroring the real bar | The existing double completes at once and cannot see the defect | Headless tests on the real animated bar for every operation (slow, and a 5-second real wait) |
| The duration | A constant | Nobody asked for a preference; YAGNI | A setting on the Settings page |
