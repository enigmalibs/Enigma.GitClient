# BUG-6EA3 — Open folder falsely fails on Windows

**Status:** DONE — see `docs/done/BUG-6EA3.md`
**Type:** BUG
**Branch:** `bugfix/bug-6ea3-windows-open-folder`
**Run:** vibe/2026-10-06-diff-search-refs-release

## Objective

On Windows, *Open the repository's folder in the file manager* opens the folder **and** no longer
reports "The folder did not open: Nothing on this desktop opened '<path>'." Linux is unchanged.

## Context & constraints

- `SystemInterop.OpenPathAsync` starts the path with `UseShellExecute = true`, and `Launch` reports
  success only when `Process.Start` returns a process.
- On Windows, ShellExecute hands a folder to the Explorer process already running: no new process
  starts, and `Process.Start` returns **`null`** — documented as "no process resource is started (for
  example, an existing process is reused)". Failures (no association, missing file) throw
  `Win32Exception`. On Linux the opener (`xdg-open`) is a new process, hence the difference.
- The same false failure applies to every shell-execute launch on Windows: `OpenUrlAsync` (a browser
  already running), opening a file from the changed-files list, and Linux's `RevealPathAsync` path.

## Steps

1. `SystemInterop.Launch`: a start that returned no process counts as launched — the shell handed the
   request to a process already running; only an exception means nothing opened. Keep it testable
   through an internal overload taking the start function.
2. Tests: `null` from the start is a launch; a `Win32Exception` / `InvalidOperationException` /
   `PlatformNotSupportedException` is not; a returned process is.

## Acceptance criteria

- On Windows, opening the repository's folder (toolbar and, after FEATURE-630E, the home list) shows no
  warning when Explorer opened it.
- A start that throws still reports that nothing opened.
- Release build clean with zero warnings; the whole suite green.

## Out of scope

- Selecting the folder in Explorer (`/select`) — the action opens the folder itself.

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| Where to fix | `Launch`: `null` from a start is success | The root cause, and it fixes URLs and files on Windows too | Launching `explorer.exe <path>` for folders only (leaves URLs and files broken) |
| Testability | An internal `Launch(startInfo, start)` overload | No real process in a unit test; the repo already tests internals (`TerminalCandidates`) | An injectable process-starter service |
