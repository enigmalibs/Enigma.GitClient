# CODE-REVIEW-6E56-PHASE06 — [Medium] SEC-03 Open never runs a file

## Summary

"Open file" on a changed file used to hand whatever a commit or a working tree contained to the
desktop through `UseShellExecute`. For a `.exe`, `.cmd`, `.ps1`, `.lnk` or `.url` somebody else
committed, "open" meant "run", one click away from reading a diff.

- `RunnableFiles` (Desktop, new) classifies a file by extension as something that would run when
  opened.
  - On Windows: `PATHEXT` (with the usual defaults when unset), the script hosts and installers
    (`.js` runs in the Windows Script Host there), and the shortcuts (`.lnk`, `.url`, `.scf`, …).
    Trailing dots and spaces, which Windows ignores, cannot hide an extension.
  - Elsewhere: launchers and scripts (`.desktop`, `.sh`, `.command`, `.AppImage`, …), so a `.js` file
    still opens in an editor on Linux.
- The changed-files row exposes `IsRunnable`. Its menu hides "Open file" for such a file and keeps
  "Show in file manager".
- `ChangedFilesPanelViewModel.OpenAsync` reveals a runnable file instead of opening it, whatever
  invoked the command.
- `SystemInterop.OpenPathAsync` itself reveals a runnable *file* instead of opening it. This is
  defence in depth for every caller; the other callers open directories, which are unaffected.

## Files/modules touched

- Created: `src/Enigma.GitClient.Desktop/Services/RunnableFiles.cs`
- Modified: `src/Enigma.GitClient.Desktop/Services/SystemInterop.cs`
- Modified: `src/Enigma.GitClient.Desktop/ViewModels/Panels/ChangedFilesPanelViewModel.cs`
- Modified: `src/Enigma.GitClient.Desktop/Views/Panels/ChangedFilesPanelView.axaml`
- Created: `tests/Enigma.GitClient.Desktop.UnitTests/RunnableFilesTests.cs`
- Modified: `tests/Enigma.GitClient.Desktop.UnitTests/ChangedFilesPanelTests.cs`
- Modified: `docs/roadmap.md`, `docs/plan/CODE-REVIEW-6E56.md`
- Created: `docs/done/CODE-REVIEW-6E56-PHASE06.md`

## Deviations & follow-ups

- The plan suggested a tooltip on "Open file". The item is hidden for a runnable file instead, so the
  menu offers only what will happen ("Show in file manager"). A tooltip would have said "this does
  not open" on an item labelled "Open".
- The classifier is platform-specific: Windows' list on Windows, the Unix list elsewhere. The plan
  listed one set, which would have made every `.js` file in a web project unopenable on Linux, where
  it runs nothing.
- The gate inside `OpenPathAsync` was not in the plan. It was added as defence in depth: the
  acceptance criterion "no runnable path reaches the shell" now holds for every caller, not just the
  panel.

## Build/test evidence

- Baseline: see `docs/done/CODE-REVIEW-6E56-PHASE01.md`.
- **This dev:**
  - build: 0 warnings, 0 errors.
  - Core unit: 1530 run, 1526 passed, 1 failed (the baseline's), 3 skipped.
  - Core integration: 449 run, 447 passed, 0 failed, 2 skipped (6 min 35 s).
  - Desktop: compiled, **not run**.
- **New tests (Desktop, compiled, not run):**
  - `RunnableFilesTests` (28 cases): Windows programs, scripts and shortcuts; documents and sources
    that are not runnable; trailing dots and spaces; `PATHEXT`; the Unix launchers; Windows-only
    extensions opening as usual elsewhere.
  - `ChangedFilesPanelTests`: "Open" on a committed script reveals it and opens nothing; the sample
    documents are not runnable.
- **Logic spot-check:** the throwaway harness outside the repository ran `RunnableFiles.IsRunnable`
  over the same 30 inputs (path, platform, `PATHEXT`) as the compiled tests: 30/30 as expected.
- **Fix budget:** 0 cycles.

## Documentation sweep

- Nothing stale. The README's description of the changed files' menu does not list "Open file".

## Decisions taken at build time

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| What the menu offers for a runnable file | "Open file" hidden, "Show in file manager" kept | The menu only offers what it will do | A tooltip on an item that does something else than its label; asking every time |
| Where the gate sits | In the panel and in `SystemInterop.OpenPathAsync` | The review's site plus the one place every caller goes through | The panel only (other callers would be unguarded if they ever opened a file) |
| One list or per platform | Per platform | A `.js` runs on Windows and opens in an editor elsewhere | One list (blocks harmless files on Linux) |
