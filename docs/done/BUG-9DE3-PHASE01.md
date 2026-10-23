# BUG-9DE3-PHASE01 — The safety net

**Item:** BUG-9DE3 — The app crashes on unhandled errors, PHASE01 — The safety net
**Branch:** `bugfix/bug-9de3-phase01-safety-net`
**Run:** bugfix/2026-10-23-crash-net-token-reset

## Summary

Before this change, an exception nothing caught ended the process. On 2026-10-23 that was a
`TokenProtectionException` rethrown by `AsyncRelayCommand` on the dispatcher. Every unhandled exception
.NET lets an application catch now reaches one safety net. The net logs it in full at Error, then shows
it in a ContentDialog that says the application is still running, and the application carries on.

**The hooks** (`UnhandledExceptionHooks`, static, because the events it adapts are):

- `Install()` is the first line of `Program.Main`. It hooks `AppDomain.CurrentDomain.UnhandledException`
  (logged; .NET still ends the process afterwards, see *Deviations*) and
  `TaskScheduler.UnobservedTaskException` (`SetObserved`, then reported).
- `InstallDispatcher()` is the first line of the desktop-lifetime branch of
  `App.OnFrameworkInitializationCompleted`. Avalonia's platform is set up by then, which it is not when
  `Main` starts. It hooks `Dispatcher.UIThread.UnhandledException`, sets `Handled = true` and reports.
  Command exceptions, `async void` handlers and posted callbacks all arrive here.
- `Attach(reporter)` runs as soon as the host has started. Reports made earlier are kept (at most 32),
  traced (type and redacted message only), and replayed to the reporter. `Detach()` runs in `OnExit`
  before the container is disposed, so a late report never logs through a disposed logger.
- `Report(exception, source)` is the entry point for code the container does not build:
  `AppWindows.StartAsync` now, `Task.Forget()` in PHASE02.

**The policy** (`UnhandledErrorPolicy`, platform-free, unit-tested):

- **Ignored:** a cancellation while the application closes, or one whose own token was cancelled (also an
  `AggregateException` holding only such cancellations); an exception instance already offered (the same
  fault reaching two hooks). A cancellation nobody asked for, such as a timeout, is still shown.
- **Merged:** while a batch waits or is on screen, a new error is counted into it ("N more errors
  occurred", with every error in the details). A burst gives one dialog.
- **Suppressed:** an error with the same signature (type, redacted message, redacted stack) as one the
  reader closed less than a minute ago is logged and not shown again.
- **`BuildDetails`:** for the exception and every inner one (all of an `AggregateException`'s), the full
  type name, the message and the stack trace. Each is passed through `ArgumentRedactor.Redact`, and the
  depth is capped at 8. `Exception.Data`, `ToString()` and derived-type properties are never used.
- **`Summarise`:** gives the message of the exception that carries the meaning: it unwraps a
  single-inner `AggregateException` and a `TargetInvocationException`, then redacts.

**The reporter** (`UnhandledErrorReporter`, singleton):

- It logs on the reporting thread, which may be about to end the process: Error, or Debug for an ignored
  cancellation. It then posts the offer to the UI thread.
- The dialog goes on `(IAppWindows.CurrentWindow as IHostWindow).DialogHost`, through
  `IContentDialogService`:
  - before the first window, it waits for `IAppWindows.CurrentWindowChanged`, a new event raised once a
    newly shown window is on screen;
  - with a question open on the host, it waits for that question's `Closed`, posted at Background so the
    question's own continuation runs first;
  - it never shows over a question or inside one.
- A dialog left on a window that went away is given up when the next window comes, so its batch does not
  hold the net for the rest of the session.
- If the dialog itself throws, the failure is logged and no further dialog is tried this session. Later
  errors are still logged, and the net never loops.

**The dialog:** title "Something went wrong", warning icon, Close button.
`UnhandledErrorViewModel`/`UnhandledErrorView` show:

- the plain sentence (the application is still running);
- the message;
- "N more errors occurred", updated live when the batch grows;
- a collapsed *Details* expander with a read-only, selectable, monospace, scrolling text box and a
  *Copy details* button (`ISystemInterop.CopyTextAsync`).

**Startup fallback:** `AppWindows.StartAsync` reports any exception it does not already handle while
opening the command-line repository (anything except a cancellation or an out-of-memory). It then shows
the start window, so the error has a host to be shown on. Previously such a start closed the splash and
ended with no window.

## Files / modules touched

**Created**

- `src/Enigma.GitClient.Desktop/Services/UnhandledErrorPolicy.cs`: `UnhandledErrorSource`,
  `UnhandledErrorDecision`, `UnhandledErrorBatch`, `UnhandledErrorPolicy`.
- `src/Enigma.GitClient.Desktop/Services/UnhandledErrorReporter.cs`: `IUnhandledErrorReporter`,
  `UnhandledErrorReporter`.
- `src/Enigma.GitClient.Desktop/Services/UnhandledExceptionHooks.cs`
- `src/Enigma.GitClient.Desktop/ViewModels/Dialogs/UnhandledErrorViewModel.cs`
- `src/Enigma.GitClient.Desktop/Views/Dialogs/UnhandledErrorView.axaml` (+ `.axaml.cs`)
- `tests/Enigma.GitClient.Desktop.UnitTests/UnhandledErrorPolicyTests.cs`: 20 cases on bursts, merging,
  repeats, cancellations and details/redaction.
- `tests/Enigma.GitClient.Desktop.UnitTests/UnhandledErrorReporterTests.cs`: 6 headless cases on
  logging, the dialog, waiting for a window or a question, shutdown cancellations, a failing dialog, and
  copying the details.
- `docs/done/BUG-9DE3-PHASE01.md`

**Modified**

- `src/Enigma.GitClient.Desktop/Program.cs`: `UnhandledExceptionHooks.Install()` first.
- `src/Enigma.GitClient.Desktop/App.axaml.cs`: `InstallDispatcher()` first, `Attach` after the host
  starts, `ShutdownRequested` → `ShutdownStarted()`; `OnExit` announces shutdown and detaches.
- `src/Enigma.GitClient.Desktop/Services/AppWindows.cs`: `IAppWindows.CurrentWindowChanged`, raised at the
  end of `Show`; the startup fallback.
- `src/Enigma.GitClient.Desktop/DependencyInjection/ServiceCollectionExtensions.cs`: the reporter.
- `tests/Enigma.GitClient.Desktop.UnitTests/Infrastructure/UiServiceDoubles.cs`: `RecordingAppWindows`
  implements the event and gets `SetCurrentWindow`; new `RecordingUnhandledErrorReporter`.
- `tests/Enigma.GitClient.Desktop.UnitTests/AppWindowsTests.cs`:
  `StartAsync_WhenOpeningFailsUnexpectedly_ReportsIt_AndStillOpensTheStartWindow`.
- `docs/roadmap.md`, `docs/plan/BUG-9DE3.md`: statuses.

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| When a repeat's minute starts | When the reader closes the dialog, for every error of the batch | "Not again for a minute" counts from when the reader last saw it, merged errors included |
| Thread model | Log on the reporting thread, decide and show on the UI thread | The policy needs no lock, and the log is written even when the thread ends the process |
| Unobserved task exceptions | Reported as the one `AggregateException` | The policy unwraps the message and lists every inner exception in the details; a burst would merge anyway |
| A dialog stranded on a closed window | Given up when the next window appears (a counter guards the late continuation) | Otherwise one batch would hold the net silently for the rest of the session |
| Waiting for a question to close | Posted at Background priority after its `Closed` | The question's continuation may ask the next question straight away; the net must not take the host first |
| Test seam for the window | `RecordingAppWindows.SetCurrentWindow` plus a real headless `StartWindow` | The reporter's host lookup is tested against the real `IHostWindow`, with no new production interface |
| Icon and colour | Phosphor *Warning*, `#E8A33D` | The same as the "git is required" dialog in `AppWindows` |

## Deviations & follow-ups

- **Limit (as planned):** an exception that reaches `AppDomain.UnhandledException` escaped on a thread
  with nothing above it, and .NET terminates the process after the event whatever a handler does. It is
  logged in full at Error. No dialog can follow. PHASE02 routes the application's own fire-and-forget
  work to the net so that it never gets there.
- **Limit:** a failure thrown synchronously from `OnFrameworkInitializationCompleted` before the host
  exists (building the container, reading the settings) leaves Avalonia's start and reaches
  `AppDomain.UnhandledException`. It is traced, and nothing can be shown.
- **Log destination:** the host's default providers apply. On Windows the EventLog provider records
  Warning and above, which covers these Error entries.
- No deviation from the plan's steps.

## Build/test evidence

- `dotnet build Enigma.GitClient.slnx`: 0 warnings, 0 errors.
- `dotnet test --solution Enigma.GitClient.slnx`: 3370 total, 3367 passed, 3 skipped, 0 failed (+27 new
  tests: 20 policy, 6 reporter, 1 `AppWindows`).
