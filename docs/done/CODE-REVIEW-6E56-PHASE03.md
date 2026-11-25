# CODE-REVIEW-6E56-PHASE03 — [High] EXC-01 Corrupt accounts backed up

## Summary

An accounts file (`host-accounts.json`) that does not parse used to read as "no accounts". This
happened silently, with no log line. The next account added then rewrote the file with that one
account, and every other account was gone for good while its token stayed orphaned in the store.

`HostAccountService` now takes an `ILogger<HostAccountService>`. On a `JsonException` it:

- logs a warning with the exception;
- moves the file aside to `host-accounts.corrupt-<yyyyMMdd-HHmmss>.json`, as `SettingsService` does
  with `settings.json`;
- logs the backup path and exposes it as `BackupPath`;
- returns the empty list.

A backup that cannot be moved is logged as an error and does not throw. The next account added
writes a fresh list, and the backup stays beside it.

## Files/modules touched

- Modified: `src/Enigma.GitClient.Core/Hosting/HostAccountService.cs`
- Modified: `tests/Enigma.GitClient.Core.UnitTests/Hosting/HostAccountServiceTests.cs`
- Modified: `docs/roadmap.md`, `docs/plan/CODE-REVIEW-6E56.md`
- Created: `docs/done/CODE-REVIEW-6E56-PHASE03.md`

## Deviations & follow-ups

- The plan also named `IOException` / `UnauthorizedAccessException` while reading. They are **not**
  treated as corruption. A file that cannot be opened may be held for an instant by another
  instance's atomic replace, and moving it aside would lose a good list. Those failures still reach
  the caller, as they did before.
- The user is not told in the UI. The review's fix stops at the log and the backup. Showing it would
  mean a message from a Core read that every page calls, which the plan did not ask for. Follow-up if
  wanted: the Profiles page could say so once when `BackupPath` is set.

## Build/test evidence

- Baseline: see `docs/done/CODE-REVIEW-6E56-PHASE01.md`.
- **This dev:**
  - build: 0 warnings, 0 errors.
  - Core unit: 1530 run, 1526 passed, 1 failed (the baseline's), 3 skipped.
  - Core integration: 449 run, 447 passed, 0 failed, 2 skipped (8 min 15 s).
  - Desktop: compiled, **not run** (untouched by this dev).
- **New tests (Core, run), in `HostAccountServiceTests`:**
  - an unreadable list lists no account, is kept aside with its contents, and logs a warning (with
    the `JsonException`) and the backup path;
  - an account added afterwards writes a fresh list and leaves the backup;
  - a readable list is never moved aside and logs no warning.
- **Fix budget:** 0 cycles. One compile error (a third construction site in the tests) was fixed
  before the first full run.

## Documentation sweep

- Nothing stale: no user doc describes what happens to an unreadable accounts file.

## Decisions taken at build time

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| Which failures count as corruption | `JsonException` only | Parsing failure is corruption. An open failure can be a transient lock from another instance | Also `IOException` (would move a good file aside) |
| Backup naming | `host-accounts.corrupt-<UTC stamp>.json` beside the file | Same scheme as `settings.corrupt-<stamp>.json` | Overwriting a single `.bak` (a second corruption would lose the first) |
| Testing the log | A small recording `ILogger<T>` in the test class | No logger double exists in the Core tests. It is a dozen lines | A logging package for tests (new dependency) |
