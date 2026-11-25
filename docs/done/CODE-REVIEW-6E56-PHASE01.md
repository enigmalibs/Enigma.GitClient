# CODE-REVIEW-6E56-PHASE01 — [High] SEC-01 System programs by path

## Summary

The client no longer starts a system program by its bare name, so a program a repository plants at
its root can no longer stand in for a system one.

- On Windows, "Open terminal" fell back to `cmd.exe` through the shell, with the repository as the
  working directory. Through the shell, that is also where a bare name is looked for.
- `wt.exe`, `explorer.exe`, macOS `open`, the Linux terminals and the bare `git` fallback all went
  through a lookup (`CreateProcess`, or .NET's own on Unix) that tries the current directory before
  the `PATH`.

What changed:

- `ExecutableSearch.FindOnPath` (Core, new) searches only the fully qualified entries of a `PATH`, and
  answers with a rooted path or nothing. `.` and relative entries are skipped.
- `GitExecutable.Resolve` (now a public static with the environment and the file system passed in)
  returns `null` instead of the bare `git` when nothing is found. `TryResolve` is then `false`, and
  `Path` throws `GitNotFoundException`. `GitEnvironment` already turns that into an unusable
  `GitAvailability` with the "install git" message. The usual Windows installation folders are used
  only when the variable naming them is fully qualified.
- `SystemInterop.Locate` (Desktop, new) finds a program's full path. `cmd.exe` comes from the system
  folder, `explorer.exe` from the Windows folder and `wt.exe` from the per-user `WindowsApps`
  aliases. Everything else, `$TERMINAL` included, comes from the fully qualified `PATH` entries. A
  rooted `$TERMINAL` is taken as it is when it exists.
- `TerminalCandidates` takes the locator. A program that is not installed is no longer a candidate,
  and every candidate's file name is a full path. `RevealPathAsync` starts `explorer.exe` and `open`
  by their full paths, and returns `false` when they are not there.

## Files/modules touched

- Created: `src/Enigma.GitClient.Core/Git/ExecutableSearch.cs`
- Created: `tests/Enigma.GitClient.Core.UnitTests/Git/ExecutableSearchTests.cs`
- Modified: `src/Enigma.GitClient.Core/Git/GitExecutable.cs`
- Modified: `src/Enigma.GitClient.Desktop/Services/SystemInterop.cs`
- Modified: `tests/Enigma.GitClient.Desktop.UnitTests/RepositoryFolderTests.cs`
- Modified: `docs/roadmap.md`, `docs/plan/CODE-REVIEW-6E56.md`
- Created: `docs/done/CODE-REVIEW-6E56-PHASE01.md`

## Deviations & follow-ups

- Wider than the plan's Windows-only steps. The macOS `open` and the Linux terminals are located the
  same way, because .NET's process start on Unix also looks in the current directory before the
  `PATH`. The finding's rule, "never start a system program by bare name", covers them too.
- The review's "confirm it with a planted `cmd.exe` first" was not done. Reproducing it means
  launching a shell from a test, which a unit test must not do. The fix removes the bare name
  whether or not the shell lookup reproduces.
- A configured git path (`settings.json` → `GitExecutablePath`) is still honoured verbatim, as
  documented: it is the user's own choice, not repository content.
- Pre-existing (baseline), left alone until PHASE07: `TokenStoreTests.AfterTheResetOfAnUnreadableStore_SecretsAreStoredAndReadAgain`
  fails on Windows (DPAPI).
- The `!` in `GitExecutable.Path` is one of STYLE-01's sites (PHASE21). It is left as it was.

## Build/test evidence

- **Baseline of the run**: the run branch `review/2026-11-19-solution-review-fixes` at `a3ef321` (the
  starting `review/solution-review` @ `b2d93c5` plus the planning commit, docs only), Debug, on
  Windows:
  - `dotnet build Enigma.GitClient.slnx --no-incremental`: 0 warnings, 0 errors.
  - `dotnet test --project tests/Enigma.GitClient.Core.UnitTests`: 1508 run, 1505 passed, **1 failed**
    (`TokenStoreTests.AfterTheResetOfAnUnreadableStore_SecretsAreStoredAndReadAgain`), 2 skipped.
  - `dotnet test --project tests/Enigma.GitClient.Core.IntegrationTests`: 449 run, 447 passed,
    0 failed, 2 skipped (8 min 01 s).
  - `Enigma.GitClient.Desktop.UnitTests`: compiled, **not run** (this machine's standing rule).
- **This dev:**
  - build: 0 warnings, 0 errors.
  - Core unit: 1522 run, 1519 passed, 1 failed (the baseline's), 2 skipped.
  - Core integration: 449 run, 447 passed, 0 failed, 2 skipped (9 min 30 s).
  - Desktop: compiled, **not run**.
- **New tests:**
  - Core, run: `ExecutableSearchTests` (14 cases). These cover the first matching directory, relative
    entries never searched, a quoted entry, no `PATH`, `Resolve` never returning a bare name,
    ignoring a relative `PATH` entry, preferring the Windows installation, and honouring a
    configured path.
  - Desktop, compiled and not run: in `RepositoryFolderTests`, every terminal on every platform is
    started by its full path, Windows system programs come from their folders, a program that is not
    installed is not a candidate, and relative `PATH` entries and relative paths with a folder are
    never located. The existing candidate tests were adapted to the locator.
- **Fix budget:** 0 cycles.

## Documentation sweep

- Nothing stale. The README's "git 2.20 or newer on the `PATH`" is still what the client needs.
  `SECURITY.md` does not describe how programs are started.

## Decisions taken at build time

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| Where the `PATH` search lives | A public static `ExecutableSearch` in Core, used by `GitExecutable` and by the Desktop locator | One rule for both. A public static pure helper is the Core convention for what tests reach (the submodule parsers) | A copy in each project; `InternalsVisibleTo` on Core for the tests |
| How `GitExecutable` is tested | `Resolve` made a public static taking the environment and `File.Exists` | Process-wide environment variables cannot be changed safely in parallel tests | Mutating `PATH` in a test; an injected environment abstraction (a new seam for one class) |
| Missing candidates | Skipped when listed, not left to fail at launch | The review's fix. Without it, a missing `cmd.exe` at its full path is still a `ShellExecute` call that fails | Leaving `Launch` to fail and try the next |
| Unix programs | Located on the fully qualified `PATH` too | .NET's Unix process start searches the current directory first, the same exposure | Windows only (the review's wording) |
