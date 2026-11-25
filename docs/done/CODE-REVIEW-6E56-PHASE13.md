# CODE-REVIEW-6E56-PHASE13 — [Medium] LOG-01 Rolling log file

## Summary

The application had no log a user could reach. The host kept its default providers. A WinExe's
console goes nowhere, the debug provider needs a debugger, and only warnings and errors reached the
Windows Event Log, under the generic .NET source. The "Something went wrong" dialog left nothing to
attach to a report.

What changed:

- `FileLoggerProvider` (Desktop, new, no package) writes the log to
  `<configuration directory>/logs/enigma-gitclient-<yyyyMMdd>.log`, one file per UTC day.
  - Logging never waits for the disk: an entry goes into a bounded queue (4,096 entries), and one
    background writer on the thread pool writes it. When the queue is full, the entry is dropped and
    counted, and a warning line gives the count once the writer catches up.
  - An entry starts its own line: UTC time, level, category, message. An exception, and the further
    lines of a multi-line message, follow on indented lines.
  - The message and the exception text both go through `ArgumentRedactor.Redact`.
  - Its alias is `File`, so `Logging:File:LogLevel` in the configuration applies to it alone.
  - Disposing the provider (with the container, at exit) drains the queue, waiting 5 s at most.
- `RollingLogFile` (Desktop, new) owns the day's file.
  - It opens the next day's file when the date changes, and keeps the files of the 7 most recent
    days that have one. It deletes only files named as it names them.
  - The file is shared for reading, so a user can open it while the application runs.
  - Each batch is appended in one write at the end of the file, under a named mutex that every
    process shares, so two instances never write over each other.
- `App.ConfigureLogging`, called by `App.BuildHost`, keeps the minimum level at `Information` and
  registers the provider. The host builder already reads the `Logging` section of an
  `appsettings.json` beside the executable, and that section overrides the minimum. The test
  container (`ConfigureServices`) never registers the provider.
- The unhandled-error dialog has a selectable line: "The log is kept in `<folder>`, one file a day.
  Attach the most recent one to a bug report." `UnhandledErrorReporter` takes `IAppPaths` to name
  the folder.
- The README lists `logs/` under "Where your things are kept" and shows how to turn on `Debug`.

## Files/modules touched

- Created: `src/Enigma.GitClient.Desktop/Services/FileLoggerProvider.cs`
- Created: `src/Enigma.GitClient.Desktop/Services/RollingLogFile.cs`
- Created: `tests/Enigma.GitClient.Desktop.UnitTests/FileLoggerProviderTests.cs`
- Modified: `src/Enigma.GitClient.Desktop/App.axaml.cs`
- Modified: `src/Enigma.GitClient.Desktop/Services/UnhandledErrorReporter.cs`
- Modified: `src/Enigma.GitClient.Desktop/ViewModels/Dialogs/UnhandledErrorViewModel.cs`
- Modified: `src/Enigma.GitClient.Desktop/Views/Dialogs/UnhandledErrorView.axaml`
- Modified: `tests/Enigma.GitClient.Desktop.UnitTests/CompositionRootTests.cs`
- Modified: `tests/Enigma.GitClient.Desktop.UnitTests/Infrastructure/UiServiceDoubles.cs`
- Modified: `tests/Enigma.GitClient.Desktop.UnitTests/TokenResetTests.cs`
- Modified: `tests/Enigma.GitClient.Desktop.UnitTests/UnhandledErrorReporterTests.cs`
- Modified: `README.md`
- Modified: `docs/roadmap.md`, `docs/plan/CODE-REVIEW-6E56.md`
- Created: `docs/done/CODE-REVIEW-6E56-PHASE13.md`

## Deviations & follow-ups

- **An exception is not on its entry's line.** The plan said "one line per entry". The entry's
  first line holds the time, level, category and message. The stack trace follows on indented
  lines, since a stack trace folded into one line cannot be read. Every entry still starts a line
  with its time, and nothing else does.
- **A cross-process lock, not in the plan.** The plan did not say what happens when two instances
  write the same day's file. The harness's first run showed that two writers can find the same end of
  file and write over each other's line. Each append now takes a named mutex keyed by the log
  directory. It is held only for the seek and write. A writer that waits 1 s for it writes without
  it rather than lose a batch.
- **Registered in `App.ConfigureLogging`**, which `App.BuildHost` calls. The plan named `BuildHost`
  itself. The separate method lets a test check the registration without resolving the provider
  against the developer's own configuration directory.
- **More tests than the plan listed:** the line format, the indented exception, appending to a day
  already written, two writers of one day, nothing written before the first entry or after disposal,
  and the dialog's log line.
- Follow-up: the level is configured by an `appsettings.json` beside the executable, which needs
  write access to the installation folder (Program Files under the MSI). A level read from the
  per-user configuration directory would be friendlier. That is a scope decision for its own item.
- Follow-up: the console provider still prints whatever a caller logs, unredacted. This is unchanged:
  callers redact credentials before logging, and the console reaches nobody in a WinExe.
- Follow-up: `CompositionRootTests.BuildHost_ProducesAHostWhoseServicesResolve` now builds a
  `FileLoggerProvider` against the real configuration directory. It writes there only if a resolved
  service logs at `Information` or above.
- The named mutex was checked across processes on Windows only. On Linux, .NET backs named mutexes
  with shared memory. Run the Desktop suite on Linux before tagging (standing recommendation).

## Build/test evidence

- Baseline: see `docs/done/CODE-REVIEW-6E56-PHASE01.md`.
- **This dev:**
  - `dotnet build Enigma.GitClient.slnx --no-incremental`: 0 warnings, 0 errors.
  - Core unit: 1534 run, 1531 passed, 0 failed, 3 skipped.
  - Core integration: 449 run, 447 passed, 0 failed, 2 skipped (6 min 16 s).
  - Desktop: compiled, **not run** (this machine's standing rule).
- **Throwaway harness outside the repository** (a console project named after the test assembly, to
  reach the internals). It builds the application's host the way `BuildHost` does, aimed at a
  temporary configuration directory, with `ValidateOnBuild` and `ValidateScopes`. Every check
  passed:
  - The container validates, and the host has one file provider.
  - Starting and stopping the host leaves one file named after today's UTC date. It holds the host's
    own lifetime lines, the hosted services' start and stop, a redacted URL, and an error with its
    exception indented.
  - `Debug` lines are filtered at the default level, and written once an `appsettings.json` asks
    for `Debug`.
  - At midnight UTC, logging moves to the next day's file. With nine older days present, 7 days
    remain, and a foreign file is untouched.
  - A burst of 50,000 entries is queued in about 30 ms. What does not fit is reported by the "could
    not be written" notice.
  - Three processes writing 3,000 entries each, at once, to one directory: 9,000 lines, all whole,
    3,000 per process, none lost.
- **New tests (Desktop, compiled, not run):**
  - `FileLoggerProviderTests` (9): the log directory, the line format, redaction in the message and
    the exception, the indented exception, a new file per day, retention with a gap and foreign
    files, appending to a day already written, two writers of one day, and nothing written before
    the first entry or after disposal.
  - `UnhandledErrorReporterTests`: the dialog names the log folder; without one it says nothing.
  - `CompositionRootTests`: the application logs to a file, and a test container never does.
- **Adapted tests (compiled):** the reporter's three construction sites take an `IAppPaths`
  (`FixedAppPaths`, a new double that never touches the disk).
- **Fix budget:** 0 cycles.

## Documentation sweep

- `README.md`: a `logs/` row under "Where your things are kept", and how to log at `Debug`.
- `SECURITY.md` ("redacted from logs") is still true. Nothing else describes logging.

## Decisions taken at build time

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| Whose lifetime the writer follows | The provider's: started when the logger factory creates it, drained when the container disposes it | The log must record the host's own start and stop, which happen outside a hosted service's window. The framework's console provider works the same way | A hosted service (misses the lines before start and after stop) |
| A full queue | Drop the entry, count it, write the count later | Logging must never block the UI thread. The loss is visible in the file | Waiting for room (blocks the caller); dropping the oldest silently |
| Two instances, one file | One file a day, each append under a named mutex | The file name the plan and the README give. The harness showed that the unguarded append loses lines | A file per process (two files to attach); appending without a lock (lines lost) |
| The day of a file | The UTC date, like the lines' times | A file and its lines agree, and no time-zone change splits a day | The local date |
| Retention | The 7 most recent days that have a file | A week without the application does not delete the last log before a report | 7 calendar days back from today |
| Shipping an `appsettings.json` | No: documented in the README | `Information` is the default with no file, and the installers do not have to carry a new one | Shipping a file the MSI may not list |
| Where the dialog names the folder | A selectable line in the dialog's body | Seen without opening the details, and copyable | Inside the details expander; an "Open folder" button (beyond the plan) |
