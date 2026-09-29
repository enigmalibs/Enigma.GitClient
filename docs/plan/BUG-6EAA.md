# BUG-6EAA — Test suite fails on Windows

**Status:** ABANDONED — at the user's request, out of the run's scope; unverified work on the unmerged branch `bugfix/bug-6eaa-windows-test-suite`.
**Type:** BUG
**Branch:** `bugfix/bug-6eaa-windows-test-suite`
**Run:** vibe/2026-09-29-dialog-view-type-name

## Objective

The whole suite runs to the end, green, on Windows. Found while verifying BUG-7E5C, and unrelated to
it: on this Windows machine the suite never finishes, and 397 App tests fail even without the
BUG-7E5C change.

## Context & constraints

- **The hang — `AtomicFileTests.AReaderRacingTheWriter_AlwaysReadsAWholeDocument` (Core).** On
  Windows, `File.Move(temporary, path, overwrite: true)` is refused (`UnauthorizedAccessException`)
  while another handle has the target open without sharing delete, which is what `File.ReadAllText`
  does. A reproduction with `AtomicFile`'s exact logic: 189 of 300 writes refused while a reader
  loops. The test's writer dies on the first refusal, so it never cancels `stop`, and the reader loop
  `while (!stop.IsCancellationRequested)` spins forever at 100% CPU.
- **It is a product bug too.** `AtomicFile` writes `settings.json` (`SettingsService`), the identity
  profiles (`IdentityProfileStore`), the recent repositories and the hidden branches. With several
  instances side by side (FEATURE-5431), one instance reading a file while another writes it makes
  the write throw, on Windows only. On Linux, a rename over an open file succeeds.
- **The 397 App failures — `TestServices.Dispose`.** It deletes the test's configuration root with a
  plain `Directory.Delete(recursive: true)`. git marks everything under `.git/objects` read-only, and
  on Windows a recursive delete refuses read-only files (`UnauthorizedAccessException`, checked on
  .NET 10.0.12). Every App test that builds a real repository fails in its teardown. The integration
  tests already solved this: `Core.IntegrationTests/Infrastructure/DirectoryCleanup` clears the
  attribute first.
- A retry sized by measurement: against a reader re-opening the file in a tight loop, 1500 writes with
  up to 50 attempts 10 ms apart all landed; the worst took 16 attempts (175 ms). A real reader (another
  instance loading its settings) holds the file for microseconds.
- No new dependency, no shared test project; Linux behaviour unchanged.

## Steps

1. `Core/Configuration/AtomicFile.cs`: on Windows only, retry the final replace while the target is
   held open — up to 50 attempts, 10 ms apart — for an access-denied, sharing-violation or
   lock-violation refusal. Any other error, and the last refusal, surface as before; the temporary
   file is still deleted. `WriteAllTextAsync` waits with `Task.Delay` under its cancellation token.
2. `AtomicFileTests`:
   - the racing writer releases the reader in a `finally`, so a writer that fails fails the test
     rather than hanging it (the assertions are unchanged);
   - a write made while a reader holds the file lands once the reader lets go;
   - on Windows, a reader that never lets go fails the write, and no temporary file is left behind.
3. `App.UnitTests/Infrastructure/DirectoryCleanup.cs`: the integration tests' helper, for the App
   tests (clear the read-only attribute, delete, retry briefly). `TestServices.Dispose` deletes through
   it.

## Acceptance criteria

- On Windows: the whole suite finishes, green; build clean with zero warnings.
- The racing test cannot hang: a writer failure is a test failure.
- A write while a reader holds the file lands; on Linux the replace is exactly as before.

## Out of scope

- Changing how the readers open the files (another process's reader is out of our hands anyway).
- A shared test-utilities project for the two cleanup helpers.
- Other plain `Directory.Delete` teardowns whose directories hold no git repository.

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| Fix it in this run at all | Yes, as its own item, before BUG-7E5C and the release | Without it no dev of this run can show a green suite on Windows, and the write failure is a real bug for instances side by side. Its own item keeps it reviewable and droppable | Folding it into BUG-7E5C (two concerns in one commit); quarantining a correct BUG-7E5C over an unrelated environment failure; skipping or weakening the racing test |
| How `AtomicFile` survives a reader | A bounded retry of the replace, Windows only | The refusal lasts as long as a reader's handle, microseconds in practice; measured worst case 175 ms against a tight-loop reader | Opening every reader with `FileShare.Delete` (another process's reader is out of reach); `File.Replace` (the same sharing rule) |
| Retry budget | 50 attempts, 10 ms apart (~0.5 s at worst) | Measured: 16 attempts at worst against an adversarial reader; the rest is margin | Unbounded retry; a single retry |
| The App cleanup | A copy of the integration tests' `DirectoryCleanup` | Same problem, same proven answer; the two test projects share no code | A shared test project (new infrastructure) |
| Row order | Inserted above BUG-7E5C | The row order is the build order, and this item must be built first; appending would build it after the release | Appending it as the last row |
