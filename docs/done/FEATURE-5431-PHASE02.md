# FEATURE-5431-PHASE02 — Several instances side by side

**Item:** FEATURE-5431 — A start window, one repo per window
**Branch:** `feature/feature-5431-phase02-instances`
**Run:** feature/2026-09-22-home-window-merges-refresh

## Summary

Working on several repositories at once means running several instances, one repository each. There
are now two ways to start another instance from inside the application:

- **"Open in a new window"** on every row of the recent list. It starts another instance straight on
  that repository and leaves this window as it is.
- **"New window"** in the repository window's strip. It starts another instance on its start window.

Both go through `IInstanceLauncher`, which runs this executable again with an **argument list**, never
a command line: the repository path reaches the new process as one argument whatever characters it
contains, and nothing is handed to a shell. When the application runs as
`dotnet Enigma.GitClient.App.dll`, the running executable is the host, so it is given the assembly
again.

Instances share the configuration directory, so the two files every instance writes — the recent list
and the settings — are now replaced **atomically**. `AtomicFile` writes to a temporary file beside the
target and moves it over. Before, a plain write truncated and then refilled the file. Another
instance reading in between took the half document for a corrupt one and moved it aside, and the
recent list was lost.

## Files / modules touched

**Added**

- `src/Enigma.GitClient.Core/Configuration/AtomicFile.cs` — `WriteAllText` / `WriteAllTextAsync`
- `src/Enigma.GitClient.App/Services/InstanceLauncher.cs` — `IInstanceLauncher`, `InstanceLauncher`,
  `BuildStartInfo`

**Modified**

- `src/Enigma.GitClient.Core/Configuration/SettingsService.cs` — writes through `AtomicFile`
- `src/Enigma.GitClient.App/Services/RecentRepositoryStore.cs` — writes through `AtomicFile`
- `src/Enigma.GitClient.App/ViewModels/Pages/RepositoriesPageViewModel.cs` — `OpenInNewWindowCommand`
- `src/Enigma.GitClient.App/Views/Pages/RepositoriesPageView.axaml` — the row's "open in a new window"
  button
- `src/Enigma.GitClient.App/ViewModels/MainWindowViewModel.cs` — `NewWindowCommand`
- `src/Enigma.GitClient.App/Views/MainWindow.axaml` — the strip's "new window" button
- `src/Enigma.GitClient.App/DependencyInjection/ServiceCollectionExtensions.cs` — the launcher

**Tests**

- `tests/Enigma.GitClient.Core.UnitTests/Configuration/AtomicFileTests.cs` (new) — create and replace
  leave only the document; UTF-8 without BOM; a cancelled write keeps the old file and leaves no
  temporary; a reader racing 300 alternating writes of a small and a large document only ever reads
  one of the two whole
- `tests/Enigma.GitClient.App.UnitTests/InstanceTests.cs` (new) — the start information (one argument,
  no shell, the dotnet host case, nothing to start); both commands, including a moved repository and a
  launch that fails
- `tests/Enigma.GitClient.App.UnitTests/RecentRepositoryStoreTests.cs` — two stores over the same
  directory see each other's writes; nothing is left beside the document
- `tests/.../Infrastructure/UiServiceDoubles.cs`, `TestServices.cs` — `RecordingInstanceLauncher`, so
  no test ever starts a process

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| Where the atomic write lives | Core, beside the settings service, used by the App's recent store too | Both writers need the same guarantee, and Core is what both projects see |
| A static helper for file I/O | Yes, `AtomicFile`, mirroring `File.WriteAllText` | The stores already call `File.*` directly; this is the same call with a different guarantee, not a substitutable service |
| A failing launch | An error info bar, "No new window" | The user pressed a button and nothing appeared, so they must be told |
| A recent entry whose folder is gone | The same warning as opening it here | Starting a process that would only land on its start window with a warning is worse |

## Deviations & follow-ups

- The first test run failed one case that was a test problem: a Windows path cannot be split on Linux.
  The test now uses the host platform's own path shape. That was one fix cycle.
- Settings changed in one instance still aren't seen by another that is already running, and the last
  writer wins. This is out of scope per the plan; a file watcher on `settings.json` would be the
  follow-up.
- Recommendation only: line endings were not examined; nothing in this diff showed CRLF churn.

## Build/test evidence

- `dotnet build Enigma.GitClient.slnx`: 0 warnings, 0 errors.
- `dotnet test --solution Enigma.GitClient.slnx`: 1852 passed, 0 failed (19 new).
