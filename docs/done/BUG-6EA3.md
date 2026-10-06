# BUG-6EA3 — Open folder falsely fails on Windows

**Item:** BUG-6EA3 — Open folder falsely fails on Windows
**Branch:** `bugfix/bug-6ea3-windows-open-folder`
**Run:** vibe/2026-10-06-diff-search-refs-release

## Summary

On Windows, *Open the repository's folder in the file manager* opened the folder and then said "The
folder did not open: Nothing on this desktop opened '<path>'." It no longer reports that.

**The cause.**
- `SystemInterop.OpenPathAsync` starts the folder through the shell (`UseShellExecute = true`).
- `Launch` counted the start as a success only when `Process.Start` returned a process.
- On Windows, ShellExecute hands a folder to the Explorer instance that is already running. No new
  process starts, so `Process.Start` returns `null`. Its documentation describes exactly this case:
  "no process resource is started (for example, if an existing process is reused)". The folder still
  opens.
- On Linux the opener (`xdg-open`) is a new process every time, which is why only Windows was
  affected.

**The fix.**
- `Launch(startInfo, start)` treats any start that does not throw as a launch. A `null` from a
  shell start means another process took the request.
- Real failures still throw and are still reported:
  - no association, or a file that is gone (`Win32Exception`);
  - no handler at all (`InvalidOperationException`);
  - an unsupported platform (`PlatformNotSupportedException`).
- A direct start (`UseShellExecute = false`) never returns `null`, so the terminals and `explorer.exe
  /select` behave as before.
- The internal overload takes the start function, so the rule is tested without launching anything.

Every shell launch goes through this one method:
- opening a folder: the toolbar, and since FEATURE-630E the home list;
- opening a file from the changed-files list;
- opening a host link in the browser;
- revealing a file on Linux.

Each of these could hit the same false failure on Windows whenever an already-running process took
the request.

## Files / modules touched

**Modified**

- `src/Enigma.GitClient.Desktop/Services/SystemInterop.cs`: `Launch`.
- `tests/Enigma.GitClient.Desktop.UnitTests/RepositoryFolderTests.cs`, a "what counts as launched"
  section:
  - a `null` from the start is a launch;
  - a returned process is a launch;
  - each of the three refusals is not.
- `docs/roadmap.md`, `docs/plan/BUG-6EA3.md`: statuses.

**Created**

- `docs/done/BUG-6EA3.md`

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| Where | `Launch`, the one place every launch goes through | It fixes the root cause for folders, files and links alike. A folder-only `explorer.exe <path>` would have left the others |
| Process handle | `start(startInfo)?.Dispose()` | It releases the handle as the `using` did, and doesn't need a variable that is never read |

## Deviations & follow-ups

- **Not reproduced live.** The run did not open a real Explorer window on your desktop. The fix rests
  on `Process.Start`'s documented behaviour, which matches your report exactly: the folder opens and
  the warning appears. Worth one click on Windows: the toolbar's folder button, and a home-list row's.
- **Not run:** the new tests are compiled but were not run, because you asked to skip the Desktop
  tests this session.
- Line endings: the touched files are LF; no CRLF churn.

## Documentation sweep

Nothing to change. No document mentions the false warning.

## Build/test evidence

- `dotnet build Enigma.GitClient.slnx -c Debug --no-incremental`: 0 warnings, 0 errors. This
  compiles the tests.
- `Enigma.GitClient.Core.UnitTests`: 1159 total, 1158 passed, 1 skipped, 0 failed. The racing
  `AtomicFileTests` test is excluded (BUG-6EAA, it hangs on Windows).
- `Enigma.GitClient.Core.IntegrationTests`: 351 total, 349 passed, 2 skipped, 0 failed.
- `Enigma.GitClient.Desktop.UnitTests`: **not run**, at your request.
- Fix budget: 0 cycles used.
