# CODE-REVIEW-6E56-PHASE22 — [Medium] SOLID-03 Split SubmoduleService

## Summary

`SubmoduleService` was 1,477 lines and changed for three reasons: how submodules are read, how a pin
change is described, and how submodules are updated. It also carried the interface and six pure
parsers. Each part now lives on its own, and the service composes them:

| File | Lines | Holds |
|---|---|---|
| `ISubmoduleService.cs` (new) | 214 | The interface, moved out unchanged (text identical). Unused usings dropped |
| `SubmoduleReader.cs` (new, internal) | 474 | Reading: list (level by level, with each submodule's state and dirt), heads, superproject, staged pins, changed pins, is-on-ref, commit counts |
| `SubmodulePins.cs` (new, internal) | 164 | Pins: describe a pin change (direction, commits added and removed), describe one pinned commit |
| `SubmoduleUpdater.cs` (new, internal) | 478 | Updating: update, follow, plan a discard, put back, with the update run and its progress relay |
| `SubmoduleOutputParser.cs` | 396 | Gains the six pure parsers: changed gitlink entries, tree gitlink entries, pin commits, changed gitlinks, gitlinks in a tree, the failure summary |
| `SubmoduleService.cs` | 176 | Builds the three parts and forwards `ISubmoduleService` to them. It keeps `HasGitModules`, `GitModulesFileName`, `PinCommitsShown`, and the six public static helpers under their old names, forwarding to the parser |

The code moved verbatim, by line range, with a script that checked each range's first line before
cutting it. The only edits are the calls across parts and three changes of visibility:
- calls into the reader become `_reader.…`;
- calls to the shared statics become `SubmoduleReader.…`, `SubmoduleService.…` or
  `SubmoduleOutputParser.…`;
- `ListLevelAsync`, `ChangedPinsInAsync`, `CountCommitsAsync`, `ReadHeadAsync(string)`,
  `IsCheckedOut`, `EnsureRevision` and `PathComparison` become `internal` instead of `private`.

Both parts that log take the service's `ILogger<SubmoduleService>`, so the log category is unchanged.

## Files/modules touched

- Created: `src/Enigma.GitClient.Core/Submodules/ISubmoduleService.cs`
- Created: `src/Enigma.GitClient.Core/Submodules/SubmoduleReader.cs`
- Created: `src/Enigma.GitClient.Core/Submodules/SubmodulePins.cs`
- Created: `src/Enigma.GitClient.Core/Submodules/SubmoduleUpdater.cs`
- Modified: `src/Enigma.GitClient.Core/Submodules/SubmoduleOutputParser.cs`
- Modified: `src/Enigma.GitClient.Core/Submodules/SubmoduleService.cs`
- Modified: `docs/roadmap.md`, `docs/plan/CODE-REVIEW-6E56.md`
- Created: `docs/done/CODE-REVIEW-6E56-PHASE22.md`

## Deviations & follow-ups

- **The commit count sits with the reader, and the pins use it from there.** The plan listed it under
  no part. Both the pins (ahead and behind) and the updater (commits a discard leaves behind) need it,
  and it reads.
- **The interface got a file of its own.** The plan did not say. It was 205 of the 1,477 lines, and a
  file per type is the Core convention.
- **`SubmoduleOutputParser` gains public members.** The six parsers were public on the service, and
  keep the same visibility in their new home. The service's copies forward, so no caller changed.
  Adding members does not change the existing surface.
- No test changed: the parsing tests still call `SubmoduleService.Parse…`, which now forwards.

## Build/test evidence

- Baseline: see `docs/done/CODE-REVIEW-6E56-PHASE01.md`.
- **This dev:**
  - `dotnet build Enigma.GitClient.slnx --no-incremental`: 0 warnings, 0 errors.
  - Core unit: 1534 run, 1531 passed, 0 failed, 3 skipped.
  - Core integration: 449 run, 447 passed, 0 failed, 2 skipped (6 min 58 s).
  - Desktop: compiled, **not run** (this machine's standing rule). No Desktop file changed.
- **The submodule tests, unchanged and run:**
  - unit: `SubmoduleServiceParsingTests`, `SubmoduleOutputParserTests`, `PinChangeTests`;
  - integration: `SubmoduleServiceTests`, `SubmoduleFollowTests`, `SubmodulePushTests`,
    `PinChangeTests`, `PinConflictTests`.
- **Public surface:** the old and new `SubmoduleService` both declare 21 public members, matched by
  name and parameters; the only textual difference is a line break inside one signature. The
  interface's text is identical.
- **Fix budget:** 0 cycles.

## Documentation sweep

- Nothing stale: no user doc describes the service's structure.

## Decisions taken at build time

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| How the three parts are made | Built by `SubmoduleService`'s constructor, not registered in the container | They are its implementation, not services of their own. The container, the callers and the tests stay as they were | Registering three new services (a change of the composition root for an internal split) |
| How the code was moved | By line range, by script, checking each range's first line | 1,000 lines retyped by hand invite a slip; moved verbatim, the diff of each method is empty | Rewriting the methods into the new classes |
| Where the shared statics live | `PathComparison`, `IsCheckedOut` and `EnsureRevision` on the reader; `HasGitModules` and the constants on the service | The reader is what every part reads through. The service keeps what callers outside name | A fourth "common" class for three one-liners |
