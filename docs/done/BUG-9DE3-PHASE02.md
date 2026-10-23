# BUG-9DE3-PHASE02 — Fire-and-forget work reaches it

**Item:** BUG-9DE3 — The app crashes on unhandled errors, PHASE02 — Fire-and-forget work reaches it
**Branch:** `bugfix/bug-9de3-phase02-forget-safely`
**Run:** bugfix/2026-10-23-crash-net-token-reset

## Summary

Work the application starts and does not await now reaches the safety net when it fails, instead of
vanishing in a task nobody looks at. Previously the failure might surface on the finaliser's thread
much later, or never.

- **`Task.Forget()`** (`Services/TaskExtensions.cs`, extension block) is the one idiom for
  fire-and-forget work. It observes the task. Each exception of a fault is reported to
  `UnhandledExceptionHooks.Report(…, FireAndForget)`, from whatever thread the task ends on. A cancelled
  or successful task reports nothing, and a task already completed is handled at once.
- **Every `_ = …` that discarded a `Task` in `src/Enigma.GitClient.Desktop` is now `….Forget()`:**
  40 sites, listed below, plus the three former `async void` handlers (43 calls in all). A method that already catches what it expects is converted too. What it does
  not catch is exactly what the net is for.
- **The three `async void` handlers are gone.** `MainWindow.OnOpened`, `StartWindow.OnOpened` and
  `AppWindows.OnFirstWindowOpened` are now plain handlers that hand over with `….Forget()`.
- **Core cannot see the Desktop net.** `SettingsService.WriteAsync`, run fire-and-forget by `Schedule`,
  caught I/O and access failures only. It now also logs any other failure at Error
  (`OperationCanceledException` and `OutOfMemoryException` are excluded).

### The audit

| Where (after the change) | What it was | What it is |
|---|---|---|
| `App.axaml.cs:116` | `_ = windows.StartAsync(…)` posted at Background | `.Forget()` |
| `MainWindowViewModel.cs:85-87` | `autoRefresh.Refreshed` lambda discarding a ternary of two refreshes | `( … ? … : … ).Forget()` |
| `Views/MainWindow.axaml.cs:74` | `async void OnOpened` | `RunStartupChecksAsync().Forget()` |
| `Views/StartWindow.axaml.cs:44` | `async void OnOpened` | `InitialiseAsync().Forget()` |
| `Services/AppWindows.cs:423` | `async void OnFirstWindowOpened` → `CheckGitAsync` | `CheckGitAsync().Forget()` |
| `Services/AutoRefreshService.cs:295` | `_ = RunAsync(_interval, loop)` | `.Forget()` |
| `Services/HostLinkService.cs:120` | `RepositoryChanged` lambda, `_ = RefreshAsync()` | `.Forget()` |
| `Services/HostRepositoryBrowser.cs:117` | `_ = _dialogs.HideAsync()` | `.Forget()` |
| `Services/RepositoryOpener.cs:94` | `_ = _refresh.RefreshNowAsync(…)` | `.Forget()` |
| `Services/RepositoryWatcher.cs:195` | `_ = session.RunAsync()` (already a catch-all inside) | `.Forget()` |
| `Services/ToolDialogService.cs:248` | `_ = host.HideAsync()` | `.Forget()` |
| `Services/UnhandledErrorReporter.cs:127, 203, 231, 258` | `_ = PresentAsync()` (never faults: the dialog call is caught) | `.Forget()` |
| `ViewModels/Dialogs/HostRepositoriesDialogViewModel.cs:203` | `_ = LoadAsync()` on a visibility change | `.Forget()` |
| `ViewModels/Pages/ConflictResolutionPageViewModel.cs:509, 821, 831` | `LoadDocumentAsync`, `RefreshAsync` ×2 | `.Forget()` |
| `ViewModels/Pages/HistoryPageViewModel.cs:482, 514, 645, 1207, 1290, 1307, 1316, 1326, 1850, 2063, 2137, 2562` | changed files, working tree, reloads, `_links.RefreshAsync`, selected file, in-place refresh | `.Forget()` |
| `ViewModels/Pages/ProfilesPageViewModel.cs:552` | `_ = LoadLocalAsync()` | `.Forget()` |
| `ViewModels/Pages/RemotesPageViewModel.cs:296, 300` | `RefreshAsync` ×2 | `.Forget()` |
| `ViewModels/Pages/RepositoriesPageViewModel.cs:177` | `_ = ShowListOfAsync(…)` | `.Forget()` |
| `ViewModels/Panels/DiffViewerViewModel.cs:401, 465, 481, 1030` | `ReloadAsync` ×4 | `.Forget()` |
| `ViewModels/Panels/WorkingTreePanelViewModel.cs:381, 389` | `RefreshAsync` ×2 | `.Forget()` |
| `Views/Pages/RepositoriesPageView.axaml.cs:280` | `_ = page.MoveAsync(…)` on a drop | `.Forget()` |
| `Views/Pages/WorktreesPageView.axaml.cs:42` | `_ = row.OpenCommand.ExecuteAsync(row)` | `.Forget()` |
| `Core/Configuration/SettingsService.cs:231, 238` | `_ = WriteAsync(…)` / `_ = DelayThenWriteAsync(…)` | unchanged (Core); `WriteAsync` now logs any unexpected failure at Error |
| `Services/RepositoryWatcher.cs:294` | the only `Task.Run`: awaited inside a catch-all that logs | unchanged |

Other searches:

- `_ = ` hits that discard a `bool` or an `int` (`SelectPath`, `IsShowing`, `TryParse`, `Contains`) are
  not tasks and are unchanged.
- A search for un-discarded `…Async(…);` statements found only the two deliberate
  `GetAwaiter().GetResult()` calls in `App` (the settings load before the first window, and `OnExit`).
- Every `async` lambda in `src/` is a `Func<…, Task>` handed to a command or an operation runner. None is
  an `async void` event handler.

## Files / modules touched

**Created**

- `src/Enigma.GitClient.Desktop/Services/TaskExtensions.cs`
- `tests/Enigma.GitClient.Desktop.UnitTests/TaskForgetTests.cs`: 6 cases. A task that fails later reports
  once. A task already faulted reports at once. Every exception of a fault is reported. An async method
  failing after an await is reported. Cancelled and successful tasks say nothing.
- `docs/done/BUG-9DE3-PHASE02.md`

**Modified**

- The 22 Desktop files of the audit table.
- `src/Enigma.GitClient.Core/Configuration/SettingsService.cs`: the Error-logged catch.
- `tests/Enigma.GitClient.Desktop.UnitTests/AppWindowsTests.cs`: the PHASE01 test looks for the start's
  own report (see *Deviations*).
- `docs/roadmap.md`, `docs/plan/BUG-9DE3.md`: statuses.

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| Report the `AggregateException` or each inner exception | Each inner exception | The fault of an `async` method is the exception it threw; the policy merges several into one dialog |
| The reporter's own `PresentAsync` | Converted too | It cannot fault (the dialog call is caught), and if it ever did, the report merges into the open batch and never starts a loop |
| Core's two discards | Left as discards; `WriteAsync` logs any unexpected failure at Error | Core has no path to the Desktop net; logging there is observing the failure |
| The three refreshes that miss `GitNotFoundException` | Unchanged, recorded below | Their own handling is a separate concern. The net surfaces them now, merged and at most once a minute after the reader closes the dialog |

## Deviations & follow-ups

- **Deviation:** `UnhandledExceptionHooks.Detach()` was added as public in PHASE01, because `App.OnExit`
  needs it. The plan's "internal `Detach()` for tests" is that member.
- **Test fix from PHASE01 (fix cycle 1):** once `Forget()` routed faults to the hub, the reports earlier
  tests' fire-and-forget work made before any reporter was attached (28 in a full run) were replayed to
  the reporter that `StartAsync_WhenOpeningFailsUnexpectedly_…` attaches. That test now asserts on the
  start's own report, and `TaskForgetTests` assertions look only for their own exceptions.
- **Failures the net now surfaces (follow-up, each deserves its own handling):** these background reads
  fail with `GitNotFoundException` when the repository's working directory no longer exists (the tests'
  `/src/my-repo` paths; in use, a repository folder deleted or unmounted while open). Previously they
  vanished. Now they reach the net:
  - `HostLinkService.RefreshAsync` (catches `GitCommandException` only, `HostLinkService.cs:165`);
  - `ConflictResolutionPageViewModel.RefreshAsync` (same);
  - `HistoryPageViewModel.LoadPageAsync` / `IsWorkingTreeDirtyAsync` (same).

  The house pattern elsewhere is `GitCommandException or GitNotFoundException`.
- **Test noise worth a follow-up:** two `ObjectDisposedException` reports from
  `RepositoryContext.EndWrite` come from an automatic fetch still in flight when a test disposed its
  container. In use, the same can only happen at exit, after `App.OnExit` has detached the net.
- **Limit (from PHASE01):** an exception that still reaches `AppDomain.UnhandledException` is logged in
  full, and .NET then terminates the process. After this phase, no work the application starts can get
  there by itself: the only `Task.Run` is awaited inside a catch-all, nothing is `async void`, and every
  fire-and-forget task is observed.

## Build/test evidence

- `dotnet build Enigma.GitClient.slnx`: 0 warnings, 0 errors.
- `dotnet test --solution Enigma.GitClient.slnx`: 3376 total, 3373 passed, 3 skipped, 0 failed (+6 new).
  The first run after the change failed the PHASE01 `AppWindows` test (28 replayed reports). That was fix
  cycle 1, and it went green on cycle 1.
