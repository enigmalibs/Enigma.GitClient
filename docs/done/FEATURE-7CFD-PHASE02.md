# FEATURE-7CFD-PHASE02 — Git process runner & repository discovery

**Item:** FEATURE-7CFD — Solution foundation & git engine
**Branch:** `feature/feature-7cfd-phase02-process-runner`
**Run:** feature/2026-09-15-enigma-git-client

## Summary

Built the layer every other git operation in the product will go through: executable resolution, an
immutable command value type, the forbidden-operation gate that makes the rebase ban structural, the
process runner (text, raw bytes and record-split flavours), the version/availability probe, the
credential redactor, and repository discovery. Added the integration-test infrastructure — an
isolated workspace with real temporary repositories — that the remaining 24 devs will build on.

## Files / modules touched

**Created — `src/Enigma.GitClient.Core/Git/`**

- `GitVersion.cs` — parsed, comparable version with the supported minimum (2.20.0)
- `ArgumentRedactor.cs` — strips credentials from URLs, query strings, headers and option values
- `GitArgumentVector.cs` — tells git's global options apart from the sub-command
- `GitCommand.cs` — an immutable invocation (working directory, argv, environment, stdin)
- `ForbiddenGitOperations.cs` — rejects the `rebase` verb, `pull --rebase` and rebase-enabling `-c`
- `GitCommandFactory.cs` / `IGitCommandFactory.cs` — prepends `--no-pager`, `core.quotePath=false`
  and `color.ui=false`, and runs the forbidden-operation gate
- `GitResult.cs`, `GitRawResult.cs` — text and binary outcomes, with record splitting
- `GitCommandException.cs`, `GitNotFoundException.cs`
- `GitExecutableOptions.cs`, `IGitExecutable.cs`, `GitExecutable.cs` — override, `PATH` and the
  usual Windows install locations
- `GitProcessRunner.cs` / `IGitProcessRunner.cs` — argv-only process execution, concurrent stdout
  and stderr reads, cancellation that kills the process tree, normalised child environment
- `GitEnvironment.cs` / `IGitEnvironment.cs` / `GitAvailability.cs` — cached version probe

**Created — `src/Enigma.GitClient.Core/Repositories/`**

- `RepositoryHandle.cs` — work tree, git directory, display name, git-path helper
- `RepositoryDiscoveryResult.cs` — `Found` / `NotARepository` / `BareRepository`
- `IRepositoryLocator.cs`, `RepositoryLocator.cs`

**Created — `src/Enigma.GitClient.Core/DependencyInjection/ServiceCollectionExtensions.cs`**

- `AddGitClientCore()`, written as a C# 14 extension block

**Created — tests**

- Unit: `GitVersionTests`, `ArgumentRedactorTests`, `GitArgumentVectorTests`,
  `ForbiddenGitOperationsTests`, `GitCommandFactoryTests`, `GitResultTests`, `RepositoryHandleTests`
- Integration infrastructure: `GitCli`, `GitWorkspace`, `TemporaryRepository`, `DirectoryCleanup`,
  `CoreTestHost`, `CoreTestHostFactory`
- Integration: `GitProcessRunnerTests`, `GitEnvironmentTests`, `RepositoryLocatorTests`

**Modified**

- `docs/roadmap.md`, `docs/plan/FEATURE-7CFD.md` — status updates

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| Where the rebase ban lives | `ForbiddenGitOperations`, called by both the factory **and** the runner | Two independent gates: a caller that hand-builds a `GitCommand` bypassing the factory still cannot rebase. The test class asserts the ban from seven angles, including `-c pull.rebase=true` and the `-r` short flag |
| Discovering a bare repository | Two `rev-parse` calls, not one | `--show-toplevel` fails outright inside a bare repository, so a single combined call would misreport "bare" as "not a repository". The bare flag and the git directory (which a bare repository does answer) are read first |
| Environment overrides | Added `GitExecutableOptions.EnvironmentOverrides`, applied by the runner | Needed so integration tests can isolate themselves from the developer's global git config; it is equally the production hook for a custom `GIT_SSH_COMMAND` |
| `GIT_TERMINAL_PROMPT=0` but no interference with credential helpers | Only the terminal prompt is disabled | A GUI must never block on a prompt it cannot show, but GUI credential helpers raise their own window and must keep working — that is the whole reason for driving the real git |
| Bare-token URL redaction threshold | Only redact a bare user-info segment of 20 characters or more | `ssh://git@host` must stay readable in a log; a 40-character token must not. The paired `user:secret@` form is always redacted regardless of length |
| Version probe working directory | `AppContext.BaseDirectory` | A broken or locked repository can never make the "is git usable?" check fail |
| Fixture git runner | A separate minimal `GitCli`, not the production runner | A bug in the code under test can never quietly corrupt the fixture it is being measured against |
| Test isolation | `HOME`, `GIT_CONFIG_GLOBAL`, `GIT_CONFIG_SYSTEM` and the author/committer identity are pinned per workspace | Otherwise a developer's own `pull.rebase`, `core.autocrlf` or commit signing configuration would leak into the suite |
| Initial branch in fixtures | `git init` then `symbolic-ref HEAD` | `git init -b` only exists from git 2.28; this keeps the fixture honest about the 2.20 floor the product claims |
| `xUnit1051` in fixture helpers | The helpers take no `CancellationToken` and use `TestContext.Current.CancellationToken` internally | Keeps every call site free of ceremony while still honouring test cancellation |

## Deviations & follow-ups

- **Deviation (additive):** the plan did not call for `GitRawResult` / `RunRawAsync`. It was added
  now because blob and patch reads later in the run must not pass through a text decoder, and
  retrofitting it into the runner afterwards would have meant touching this file again.
- **Follow-up:** `GitExecutable` falls back to the bare name `git` when probing finds nothing, so a
  genuinely missing git surfaces as `GitNotFoundException` at first use rather than at resolution.
  `IGitEnvironment.GetAvailabilityAsync` is the check the shell will run at startup (FEATURE-52FB).
- **Follow-up:** the runner buffers all output in memory. That is right for everything planned so
  far; streaming progress for clone/fetch/push arrives with FEATURE-52FB and FEATURE-06FE.
- **Line endings (recommendation only):** no CRLF churn observed; `.gitattributes` already declares
  `* text=auto eol=lf`. No action taken.

## Build/test evidence

```
dotnet build Enigma.GitClient.slnx
  0 Warning(s)
  0 Error(s)

dotnet test --solution Enigma.GitClient.slnx
  Test run summary: Passed!
  total: 114  failed: 0  succeeded: 114  skipped: 0
```

110 of those tests are new in this dev (90 unit, 23 integration, minus the 4 pre-existing smoke
tests). The warning count was read explicitly (0).
