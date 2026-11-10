# FEATURE-6693-PHASE01 — Read and update submodules in Core

**Item:** FEATURE-6693 — Submodule support
**Branch:** `feature/feature-6693-phase01-submodule-service`
**Run:** feature/2026-11-09-feature-6693

## Summary

Core can now list a repository's submodules (nested ones included) with where each stands against the
commit the repository expects, put them on that commit without ever losing work, and say which pins a
commit range moved. Nothing in the UI uses it yet (PHASE02 onwards).

- **`Core/Submodules/`** — `GitSubmodule` (name, path, full path, redacted URL, expected and
  checked-out commit, `SubmoduleCheckoutState`, uncommitted/untracked flags, parent path),
  `SubmoduleOutputParser` (`.gitmodules` via `config -z --get-regexp`, gitlinks via
  `ls-files --stage -z`), `SubmoduleUpdateResult` (updated / skipped with a reason / failed with git's
  redacted message) and `SubmoduleUpdateProgress`.
- **`ISubmoduleService`** — `ListAsync`, `UpdateAsync`, `ChangedPinsAsync`, `IsOnAnyRefAsync`; registered
  in Core's DI. Without `.gitmodules`, `ListAsync` and `UpdateAsync` return at once, running no git.
- **Rule 1 (never lose work):** a submodule with modified tracked files, or on a commit no branch, tag or
  remote branch contains, is skipped and named. Untracked files never block (a checkout never deletes
  them).
- **`ForbiddenGitOperations`:** refuses `submodule update --rebase` / `-r` and a
  `-c submodule.<name>.update=rebase` override. `GitArgumentVector.FindVerbIndex` added for it.

## Files/modules touched

- Created:
  - `src/Enigma.GitClient.Core/Submodules/GitSubmodule.cs`, `SubmoduleOutputParser.cs`,
    `SubmoduleService.cs`, `SubmoduleUpdateResult.cs`
  - `tests/Enigma.GitClient.Core.UnitTests/Submodules/SubmoduleOutputParserTests.cs`,
    `SubmoduleServiceParsingTests.cs`
  - `tests/Enigma.GitClient.Core.IntegrationTests/Submodules/SubmoduleServiceTests.cs`
  - `tests/Enigma.GitClient.Core.IntegrationTests/Infrastructure/SubmoduleTemplate.cs`
  - `docs/done/FEATURE-6693-PHASE01.md`
- Modified:
  - `src/Enigma.GitClient.Core/Git/ForbiddenGitOperations.cs`, `GitArgumentVector.cs`
  - `src/Enigma.GitClient.Core/DependencyInjection/ServiceCollectionExtensions.cs`
  - `tests/Enigma.GitClient.Core.UnitTests/Git/ForbiddenGitOperationsTests.cs`
  - `tests/Enigma.GitClient.Core.IntegrationTests/Infrastructure/GitWorkspace.cs`
    (`AllowLocalSubmodules`), `HistoryFixture.cs` (`CopyDirectory` made `internal`)
  - `docs/roadmap.md`, `docs/plan/FEATURE-6693.md`: statuses.

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| How to read the states | `.gitmodules` + `ls-files --stage` + each submodule's `rev-parse HEAD`, one repository level at a time | `git submodule status` runs `git describe` up to 4× per submodule: measured 1.6 s for 4 submodules on this machine, vs ~50 ms per call here — and PHASE03's band reads states on refresh |
| How to update | One `submodule update --init --checkout --progress -- <path>` per submodule, then its nested ones listed again and updated | Rule 1 must hold at every level; `--recursive` would move a nested submodule git-side without the check, and the nested pins are only known after the parent moved |
| "Uncommitted changes" | The submodule's own `status --porcelain=v2 --ignore-submodules=all`: tracked changes only | The parent's porcelain `m` flag is also set when a nested submodule is merely behind, which would block every update of its parent |
| An expected commit the submodule does not have | `Behind` | It can only come from the remote (a colleague's bump not fetched yet); the update fetches it, and that is exactly the band-worthy case |
| A conflicted pin | Extra state `Conflicted`; never updated (PHASE07) | git reports it; it fits none of the plan's five |
| `Parent` | `ParentPath` (string), plus `FullPath` and `HasUntrackedFiles` | A plain record with no object graph; the UI needs the folder and the untracked flag |
| Credentials for a download | The repository's own profile logins (`ForRepositoryAsync`) | `-c` and the environment reach git's submodule children; a submodule on the same host signs in, others keep git's helper |
| A path that climbs out of the work tree | Ignored and logged | Rule 6; git refuses such paths anyway |

## Deviations & follow-ups

- **Plan step 2 (`ListAsync`)** used `git submodule status --recursive`; replaced for speed as above. The
  unit acceptance "`submodule status` parsing" became `ls-files --stage` parsing (stages, paths with
  spaces/tabs/parentheses, SHA-256), with `.gitmodules` parsing as planned.
- A folder recorded as a gitlink but not declared in `.gitmodules` is not listed (git cannot update it
  either); `git submodule status` would have failed outright on it.
- `UpdateAsync` with several paths, one nested in another, updates the outer one and lets it handle the
  nested one (its listed state would be stale after the outer moves).
- Test speed: the fixture is built once per class (`SubmoduleTemplate`, ~12 s on Windows) and copied per
  test; the 14 integration tests take ~1 min here (they clone and check out for real).
- Follow-up for PHASE05: an explicit *Discard* must be able to move a submodule off a commit no ref
  contains (after its question) — `UpdateAsync` has no such opt-in yet.

## Build/test evidence

- **Baseline** for the whole run — the run branch at `85f0c7e` (the starting `develop` @ `6aacbf3` plus
  the claim commit), Debug, on Windows:
  - `dotnet build Enigma.GitClient.slnx --no-incremental`: 0 warnings, 0 errors.
  - `dotnet test --project tests/Enigma.GitClient.Core.UnitTests`: 1401 run, 1398 passed, **1 failed**,
    2 skipped — the failure is pre-existing and Windows-only:
    `TokenStoreTests.AfterTheResetOfAnUnreadableStore_SecretsAreStoredAndReadAgain` (DPAPI).
  - `dotnet test --project tests/Enigma.GitClient.Core.IntegrationTests`: 417 run, 415 passed, 0 failed,
    2 skipped.
  - `Enigma.GitClient.Desktop.UnitTests`: compiled, **not run** — this machine's standing rule (the
    suite is broken on Windows, BUG-6EAA). Run it on Linux before tagging.
- **This dev:**
  - build: 0 warnings, 0 errors.
  - Core unit: 1458 run, 1455 passed, 1 failed (the baseline's), 2 skipped.
  - Core integration: 431 run, 429 passed, 0 failed, 2 skipped (5 min 58 s; the baseline 4 min 50 s).
- **New tests:** unit — `SubmoduleOutputParserTests` (gitlinks, conflict stages, odd paths, SHA-256,
  `.gitmodules` names with dots and spaces), `SubmoduleServiceParsingTests` (changed pins, tree
  gitlinks, failure summary with redaction, skip messages, progress text), 3 new
  `ForbiddenGitOperationsTests` theories; integration — `SubmoduleServiceTests` (14): every state with a
  nested submodule; uncommitted/untracked flags (a nested one behind is not its parent's dirt);
  update of the missing and behind ones leaving the ahead one; update all; skip on uncommitted changes;
  skip on a commit no ref contains; nested download; nested move; `submodule.<name>.update=rebase`
  checks out detached and leaves the branch alone; unknown path; `ChangedPinsAsync` exact; refs check.
- **Fix budget:** 0 cycles.

## Documentation sweep

- Core-only, nothing user-facing: README, RELEASENOTES and SECURITY say nothing about submodules; no
  edit. The README *Features* bullet is PHASE02's.
