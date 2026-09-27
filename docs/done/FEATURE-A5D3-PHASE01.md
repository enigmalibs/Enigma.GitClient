# FEATURE-A5D3-PHASE01 — The helper, in the operations

**Item:** FEATURE-A5D3 — Info bars that never block
**Branch:** `feature/feature-a5d3-phase01-non-blocking-operations`
**Run:** feature/2026-09-27-infobars-dialogs-diff-release

## Summary

The application is on **Enigma.Avalonia.Desktop 1.1.0**, and has one way of reporting something:
`IInfoBarService.Notify(title, message, severity)`, a C# 14 extension in
`Services/InfoBarServiceExtensions.cs`. It shows the bar and returns at once:

- **Success and info** messages go through 1.1.0's `ShowAsync(TimeSpan, configure)` with
  `TransientDisplayDuration` (5 seconds) and close themselves.
- **Warning and error** messages go through `ShowAsync(configure)` and stay until they are closed.
- Nothing waits for the task the service returns, since it only completes when the bar closes. A bar
  that could not be shown at all (no host registered) is already faulted, and `Notify` rethrows its
  exception at the call. That keeps a wiring mistake as loud as it was when the task was awaited.

Every operations service is moved onto it: `BranchOperations`, `TagOperations`, `SyncOperations`,
`CheckoutOperations`, `MergeOperations`, `ResetOperations`, `BranchDropOperations`,
`HostLinkService` and `AppWindows`. Each private `ReportAsync`/`ShowAsync` helper is now a
`void Report`/`Show` forwarding to `Notify`, and its 29 awaited call sites are plain calls. So a
merge, a pull, a checkout or a reset hands its result back to the page, and the page refreshes, while
the report is still on screen. Before, that happened only once the user had closed the bar.

## Files / modules touched

**Created**

- `src/Enigma.GitClient.App/Services/InfoBarServiceExtensions.cs` — `Notify` and
  `TransientDisplayDuration`
- `tests/Enigma.GitClient.App.UnitTests/InfoBarNotificationTests.cs`

**Modified**

- `Directory.Packages.props` — `Enigma.Avalonia.Desktop` `1.0.0 → 1.1.0`; the coupled-set comment
  says 1.1.0 is built against Avalonia 12.1.1 too
- `src/Enigma.GitClient.App/Services/` — `AppWindows.cs`, `BranchDropOperations.cs` (`ExplainAsync`
  → `Explain`), `BranchOperations.cs`, `CheckoutOperations.cs`, `HostLinkService.cs`,
  `MergeOperations.cs` (the outcome switch now picks the title, message and severity, then shows
  them), `ResetOperations.cs`, `SyncOperations.cs`, `TagOperations.cs`
- `tests/…/Infrastructure/UiServiceDoubles.cs` — `RecordedNotification` gains `DisplayDuration`;
  `RecordingInfoBarService` records it and gains `HoldsOpen` / `IsOpen`. With `HoldsOpen`, a shown
  bar's task completes only on `HideAsync`, as the real bar's does
- `tests/…/MergeOperationTests.cs` — the two *reports never block* tests
- `docs/roadmap.md`, `docs/plan/FEATURE-A5D3.md` — statuses

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| How `Notify` rethrows a failed show | `ExceptionDispatchInfo.Throw` of the faulted task's inner exception | Keeps the original exception and stack. Never `.GetAwaiter().GetResult()`, even on a completed task, per the house async rule |
| Class name | `InfoBarServiceExtensions`, the house `<Type>Extensions` rule, in `Enigma.GitClient.App.Services` | The library has a class of the same simple name. Extension lookup is unaffected, and the tests assert the literal 5 seconds (the requirement) rather than naming the constant, so no file has to pick between the two |
| `MergeOperations`' outcome report | A tuple switch, then one `Show` | Closest to the original switch expression, which returned the `Task` |
| The hold-open double | One shared `TaskCompletionSource` for everything shown while open | The real bar completes every pending `ShowAsync` task when it closes |
| Proving the tests catch the defect | Ran the two merge tests against `MergeOperations.cs` from `HEAD`, then put the new file back (not committed) | Both failed with `TimeoutException`: the awaited report never completed. With the change they pass |

## Deviations & follow-ups

- None from the plan.
- Known behaviour, unchanged from 1.0 and out of scope: the app has one bar, so a new message
  replaces the open one. A success arriving after an error replaces the error, and closes after
  5 seconds.
- Line endings: none of this dev's churn is CRLF. The touched files are LF, as `.gitattributes` asks.

## Documentation sweep

Nothing user-facing described info bars, and no README, `RELEASENOTES.md` or `docs/RELEASE.md` text
was made wrong by this dev. The release item writes the 1.1.0 notes.

## Build/test evidence

- Baseline (the run's starting commit): `dotnet build Enigma.GitClient.slnx` 0 warnings, 0 errors;
  `dotnet test --solution Enigma.GitClient.slnx` 2216 passed.
- After the change: `dotnet build Enigma.GitClient.slnx --no-incremental` 0 warnings, 0 errors;
  `dotnet test --solution Enigma.GitClient.slnx` **2225 passed**, 0 failed (9 new), with no fix cycle.
- `Enigma.Avalonia.Desktop/1.1.0` is the version in `obj/project.assets.json`.
- `grep` finds no `ReportAsync`, `ExplainAsync` or awaited `_infoBar.ShowAsync` left under
  `src/Enigma.GitClient.App/Services/`.
