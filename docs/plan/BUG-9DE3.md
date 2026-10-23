# BUG-9DE3 — The app crashes on unhandled errors

**Status:** TODO
**Type:** BUG
**Branch:** one per phase, see below
**Run:** bugfix/2026-10-23-crash-net-token-reset

## Objective

On 2026-10-23 the desktop application terminated on an unhandled exception: a `TokenProtectionException`
thrown under `ProfilesPageViewModel.ConnectAsync` was rethrown by `AsyncRelayCommand` on the Avalonia
dispatcher, and nothing caught it (Event Viewer: "The process was terminated due to an unhandled
exception"). After this item, no unhandled exception that .NET lets an application catch ends the
process: it is logged, shown in a ContentDialog that says the application is still running, and the
application carries on.

This is a safety net, not the error handling. Known failures keep their own targeted, friendly
handling (BUG-3024), and nothing already handled is ever reported to the net.

## Context & constraints

- **No global handling exists.** `Program.cs` and `App.axaml.cs` hook none of
  `Dispatcher.UIThread.UnhandledException`, `TaskScheduler.UnobservedTaskException` or
  `AppDomain.CurrentDomain.UnhandledException`.
- **What reaches what:**
  - `AsyncRelayCommand` (no `FlowExceptionsToTaskScheduler`) awaits the command in an `async void` and
    rethrows on the synchronisation context → the dispatcher's `UnhandledException`.
  - `async void` UI handlers (3: `MainWindow.OnOpened`, `StartWindow.OnOpened`,
    `AppWindows.OnFirstWindowOpened`) → the same.
  - `_ = SomethingAsync()` discards (~37 in `src/`, including `_ = windows.StartAsync(...)` in
    `App.OnFrameworkInitializationCompleted` and the lambda in `MainWindowViewModel`'s
    `autoRefresh.Refreshed` handler) → the task faults, nobody looks, and the failure vanishes until
    the finaliser raises `TaskScheduler.UnobservedTaskException` — or never.
  - `Task.Run` (1 site: `RepositoryWatcher.Session.RunAsync`, already inside a catch-all) and any
    exception escaping on a non-UI thread → `AppDomain.UnhandledException`, after which .NET always
    terminates the process, whatever the handler does.
- **The dialog host.** `IContentDialogService` drives one `ContentDialog` per window
  (`IHostWindow.DialogHost`, registered by `AppWindows.RegisterHosts`); a second `ShowAsync` while one
  is open replaces its content (`ContentDialogServiceExtensions` remark). The error dialog therefore
  waits for the host to be free and never shows over another question.
- **Startup.** `StartAsync` runs under the splash; until it shows a window there is no host. Its own
  unexpected failure today closes the splash and ends the application.
- **Secrets.** Exception messages can carry a remote URL or a header. `ArgumentRedactor.Redact` (Core)
  is the house redactor.
- **Tests.** The headless fixture builds `App` with `SetupWithoutStarting` (no desktop lifetime), so
  nothing installed from `Program.Main` or from the desktop-lifetime branch of
  `OnFrameworkInitializationCompleted` runs under the tests. The whole suite runs on this (Linux)
  machine.

### Shared design

| Piece | Where | Role |
|---|---|---|
| `UnhandledErrorPolicy` | `Desktop/Services/UnhandledErrorPolicy.cs` | Pure, no Avalonia: filters, queues and merges, suppresses repeats, builds the details text. Unit-tested without a platform. |
| `UnhandledErrorBatch` | same file | What one dialog shows: the first error, how many more, the details of all of them; raises `Changed` when an error is merged into it. |
| `IUnhandledErrorReporter` / `UnhandledErrorReporter` | `Desktop/Services/UnhandledErrorReporter.cs` | Singleton. `Report(exception, source)` from any thread: logs now through `ILogger` (Error), then posts to the UI thread where the policy decides and the dialog is shown on the current window's host when it is free. |
| `UnhandledExceptionHooks` | `Desktop/Services/UnhandledExceptionHooks.cs` | Static: the process-wide events are static, so their adapter is too. Installs the hooks, buffers what arrives before the reporter is attached, forwards to it afterwards. |
| `UnhandledErrorViewModel` / `UnhandledErrorView` | `ViewModels/Dialogs`, `Views/Dialogs` | The dialog's content: plain sentence, the message, "N more errors occurred", a collapsed *Details* expander with selectable text and a *Copy details* button. |
| `Task.Forget()` | `Desktop/Services/TaskExtensions.cs` (PHASE02) | Fire-and-forget that reports a fault to the hub and ignores cancellation. |

## PHASE01 — The safety net

**Status:** TODO
**Branch:** `bugfix/bug-9de3-phase01-safety-net`

### Steps

1. **Policy** (`UnhandledErrorPolicy`, constructed with a `TimeProvider`):
   - `IsShuttingDown` (set by the reporter).
   - `Offer(Exception)` → `UnhandledErrorDecision` { `Ignore`, `Suppress`, `Queue`, `Merge` }:
     - `Ignore` an `OperationCanceledException` (or an `AggregateException` holding only those) while
       shutting down, or whose own `CancellationToken` was cancelled — a cancellation someone asked
       for. A cancellation nobody asked for (a timeout) is shown.
     - `Ignore` an instance already offered (the same exception reaching two hooks).
     - `Merge` into the batch that is pending or on screen.
     - `Suppress` an error whose signature (type, redacted message, redacted stack) was shown less than
       `RepeatWindow` (1 minute) ago: logged, not shown again.
     - otherwise `Queue` a new batch.
   - `BeginShow()` → the pending batch, now on screen; `EndShow()`.
   - `static string BuildDetails(Exception)`: for the exception and every inner one (all of an
     `AggregateException`'s): full type name, message, stack trace, each passed through
     `ArgumentRedactor.Redact`. Never `Exception.Data`, never `ToString()`, never a request object.
   - `static string Summarise(Exception)`: the message shown in the dialog (the innermost meaningful
     message of an `AggregateException` / `TargetInvocationException`), redacted.
2. **Reporter** (`UnhandledErrorReporter`: `IAppWindows`, `IContentDialogService`, `ISystemInterop`,
   `TimeProvider`, `ILogger<UnhandledErrorReporter>`):
   - `Report(Exception, UnhandledErrorSource)`: logs the full exception at Error with its source (a
     cancellation that will be ignored: Debug), then `Dispatcher.UIThread.Post` the offer.
   - On the UI thread: offer to the policy; on `Queue`, present when possible; on `Merge`, the batch
     raises `Changed` and the open dialog's count and details follow.
   - Present: the host is `(IAppWindows.CurrentWindow as IHostWindow)?.DialogHost`.
     - No host yet → wait for `IAppWindows.CurrentWindowChanged` (new event, raised by `AppWindows`
       once the hosts of a newly shown window are registered).
     - Host open with another dialog → wait for that dialog's `Closed`, then try again.
     - Otherwise `BeginShow` and `IContentDialogService.ShowAsync` with the view; title "Something went
       wrong", close button "Close", warning icon.
     - The dialog itself throws → log it (Error) and stop showing dialogs for the rest of the session
       (every later error is still logged): no loop.
     - After it closes, `EndShow`, and present the next pending batch, if any.
   - `ShutdownStarted()` sets the policy's flag.
3. **Hooks** (`UnhandledExceptionHooks`):
   - `Install()` — first line of `Program.Main`: `AppDomain.CurrentDomain.UnhandledException` (logged,
     with `IsTerminating`; the process may still end) and `TaskScheduler.UnobservedTaskException`
     (`SetObserved`, reported).
   - `InstallDispatcher()` — first line of the desktop-lifetime branch of
     `App.OnFrameworkInitializationCompleted`: `Dispatcher.UIThread.UnhandledException` → `Handled =
     true`, reported.
   - `Attach(IUnhandledErrorReporter)` once the host is started; anything reported earlier is replayed
     to it. Before that, a report is kept (bounded) and written to `Trace`.
   - `Report(Exception, UnhandledErrorSource)` — the one entry point for code that is not in the
     container (PHASE02's `Forget`, `AppWindows.StartAsync`).
   - App: `desktop.ShutdownRequested` and `Exit` tell the reporter shutdown started.
4. **Startup fallback.** `AppWindows.StartAsync`: an unexpected exception while opening the command-line
   repository (anything but the three it already handles and a cancellation) is reported to the hub
   and the start window is shown instead — so the error has a host to be shown on.
5. **Dialog content**: `UnhandledErrorViewModel(UnhandledErrorBatch, ISystemInterop)` — `Lead` ("Something
   went wrong. The application is still running; you can carry on, or close it and start it again."),
   `Message`, `MoreText` / `HasMore` ("1 more error occurred" / "N more errors occurred"), `Details`,
   `CopyDetailsCommand` (`ISystemInterop.CopyTextAsync`); follows `Changed`. `UnhandledErrorView.axaml`:
   wrapped text, an `Expander` "Details" (collapsed) holding a read-only, selectable, monospace,
   scrolling `TextBox` and the *Copy details* button; `AutomationProperties.Name` on the button.
6. **Registration**: `IUnhandledErrorReporter` singleton in `AddGitClientApp`; the composition-root test
   still resolves everything.
7. **Tests** (Desktop, new `UnhandledErrorPolicyTests.cs` + `UnhandledErrorReporterTests.cs`):
   - policy: a burst while one is pending → one batch, `More` counts the rest, details hold all; an error
     while one is on screen merges and raises `Changed`; after `EndShow` a new error queues a new batch;
     the same instance twice counts once; a cancellation while shutting down is ignored; a cancellation
     of a cancelled token is ignored; a timeout-style cancellation is not; an identical error within the
     repeat window is suppressed and after it is shown again (manual clock);
   - details: type, message, inner exceptions and stack trace are there; `Exception.Data` values are
     not; a token inside a URL or an `Authorization:` header in the message is redacted;
   - reporter (headless): with no host window nothing is shown and the error stays pending; once a
     window with a host is current, one dialog is shown with the message; a dialog service that throws
     is logged and does not loop.

### Acceptance criteria

- A command, an `async void` handler or a posted callback that throws on the UI thread no longer ends
  the process: the exception is logged at Error and one dialog says what happened.
- Unobserved task exceptions are observed, logged and shown.
- An exception reaching `AppDomain.UnhandledException` is logged in full; the completion doc records
  that .NET still terminates the process in that case.
- Bursts make one dialog with "N more errors occurred"; no dialog is ever stacked over, or shown inside,
  another; errors before the first window wait for it.
- The details text has type, message, inner exceptions and stack trace, redacted, and never
  `Exception.Data`.
- Zero warnings; the whole suite is green, with the new tests.

## PHASE02 — Fire-and-forget work reaches it

**Status:** TODO
**Branch:** `bugfix/bug-9de3-phase02-forget-safely`

### Steps

1. `TaskExtensions` (extension block on `Task`): `Forget()` — observes the task; a fault (each inner
   exception of the `AggregateException`, unwrapped) goes to `UnhandledExceptionHooks.Report(…,
   FireAndForget)`; a cancellation is dropped; a task already completed is handled at once.
2. Audit every `_ = …` in `src/` whose value is a `Task` (grep `_ = ` and read each hit, including the
   multi-line ones and event-handler lambdas):
   - Desktop: replace with `….Forget()`. Where the method already catches everything it can throw, it is
     still converted (one idiom), unless the discard is not a fire-and-forget at all.
   - Core (`SettingsService`): Core cannot see Desktop; confirm each discarded method catches and logs
     its own failures, and add the catch where it does not. Record each site in the completion doc.
3. The three `async void` handlers become `private void On…(…) => …Async().Forget();`.
4. `Task.Run`: confirm the one site is awaited inside a catch-all; record it. Event handlers raised on
   worker threads that start async work are routed the same way.
5. `App.OnFrameworkInitializationCompleted`: `windows.StartAsync(…).Forget()`.
6. Tests (Desktop, `TaskForgetTests.cs`): a faulted task reaches an attached test reporter with the
   inner exception; a cancelled task reports nothing; a task that faults later reports once; a task
   already faulted reports synchronously. The hub gets an internal `Detach()` for tests.

### Acceptance criteria

- No `_ = …` in `src/Enigma.GitClient.Desktop` discards a `Task`; no `async void` remains.
- Every fault of fire-and-forget work reaches the reporter (and the dialog), none vanishes; cancellations
  do not.
- The audit (every site, what it was, what it became) is in the completion doc, with the
  `AppDomain.UnhandledException` limit.
- Zero warnings; the whole suite is green.

## Out of scope

- Changing what the known failures say (BUG-3024 owns the token ones).
- A standalone error window for a failure that prevents every window from being built: such a failure
  is logged only (recorded as a limit).
- Crash reporting to a server, a log file viewer.

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| Phases | Net first (hooks, policy, dialog), then the fire-and-forget audit | Each is one reviewable commit; the net alone already stops the crash seen on 2026-10-23 | One dev (too large to review); policy alone without the dialog (not usable) |
| Where the hooks go | AppDomain + TaskScheduler at the top of `Program.Main`; dispatcher first in the desktop branch of `OnFrameworkInitializationCompleted` | As early as each is safe: touching `Dispatcher.UIThread` before the platform is set up is not; the tests never run either path | Hooking in `App.Initialize` (runs under the headless tests and would change them) |
| Static hub | `UnhandledExceptionHooks` buffers until the DI reporter is attached | The events are process-wide statics; `Forget()` and the early hooks need an entry point outside the container | A static logger (no DI logging); injecting a reporter into every ViewModel that discards a task |
| Fire-and-forget idiom | `Task.Forget()` extension | One idiom, one place that decides; the house uses extension blocks | `async void` wrappers (crash off the UI thread); `ContinueWith` at every site |
| No host yet | Pending until `IAppWindows.CurrentWindowChanged`; `StartAsync` falls back to the start window on an unexpected failure | Every start ends with a window that has a host; no second dialog system | A standalone error window (a second UI to keep consistent) |
| Another dialog open | Wait for its `Closed`, then show | The host is shared; showing would replace the open question's content | A queuing decorator around `IContentDialogService` (would deadlock nested questions) |
| Bursts | One dialog; later errors merge live into "N more errors occurred" and the details | A loop of failures cannot open a dialog per failure | A queue of dialogs, one per error |
| Repeats | Same signature within 1 minute: logged, not shown | A failing periodic refresh would otherwise reopen the dialog every tick | Showing every occurrence; never showing a repeat again |
| Cancellations | Ignored when shutting down or when the exception's own token was cancelled; logged at Debug | They are requested stops, not errors; a timeout is still shown | Ignoring every cancellation (hides timeouts); showing them all |
| Dialog failure | Log and stop showing dialogs for the session | "Without looping" | Retrying |
| Secrets in details | `ArgumentRedactor.Redact` over message and stack; only type, message, stack, inners | Defence in depth: messages can carry a URL or a header | `Exception.ToString()` |
| Log level | Error for every reported exception, AppDomain included | As specified; one level to search for | Critical for AppDomain |
