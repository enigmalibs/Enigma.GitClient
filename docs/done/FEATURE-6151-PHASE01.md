# FEATURE-6151-PHASE01 — Read and write the git identity

**Item:** FEATURE-6151 — Git identity: global, profiles, local
**Branch:** `feature/feature-6151-phase01-identity-engine`
**Run:** feature/2026-09-26-git-identity-profiles

## Summary

The engine side of the identity page: `IGitIdentityService` in Core reads, writes and removes the
name and email git records on a commit, in the global scope and in a repository's local scope, by
driving `git config` — git finds the global file (`GIT_CONFIG_GLOBAL`, XDG, `~/.gitconfig`), keeps
the rest of it as it was, and locks it while writing.

- A read is one `git config -z --global|--local --get-regexp ^user\.(name|email)$`; exit 1 means
  nothing is set. The last value of a key set twice wins, as in git.
- A write validates first (`GitIdentityRules`), refuses an incomplete or invalid identity with an
  `ArgumentException` before git runs, and then sets `user.name` and `user.email`, trimmed.
- Removing the local identity runs `git config --local --unset-all` on both keys; a key that is not
  set (exit 5) is not an error.
- `GitIdentity` (record) carries the pair, with `IsEmpty`, `IsComplete`, `Normalised()` and
  `IsSameAs` (name exactly, email regardless of case) for the later phases.

Nothing is visible in the application yet.

## Files / modules touched

**Created — Core**

- `Identity/GitIdentity.cs` — `GitIdentity`, `GitIdentityRules` (name/email validation, 256-character
  limit)
- `Identity/GitIdentityService.cs` — `GitConfigScope`, `IGitIdentityService`, `GitIdentityService`
  (with static `BuildReadArguments`, `BuildWriteArguments`, `BuildRemoveArguments`, `Parse`)

**Modified — Core**

- `DependencyInjection/ServiceCollectionExtensions.cs` — registers `IGitIdentityService`

**Tests**

- `Core.UnitTests/Identity/GitIdentityRulesTests.cs` — the rules, emptiness/completeness, trimming,
  comparison, display
- `Core.UnitTests/Identity/GitIdentityArgumentsTests.cs` — the argument vectors (and that the command
  factory accepts them), parsing `-z` output
- `Core.IntegrationTests/Identity/GitIdentityServiceTests.cs` — against real git in an isolated
  workspace: empty global read, global round trip into the workspace's `GIT_CONFIG_GLOBAL` file, the
  rest of the file kept, only the two keys read, invalid identities refused before git runs, local
  read ignores inherited values, local overrides what git resolves, remove falls back to global,
  removing nothing succeeds, a key set twice is removed, a local read outside a repository fails

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| Where global commands run | `AppContext.BaseDirectory`, as `GitEnvironment` does | A directory that always exists; the global scope never reads the repository around it |
| Validation of a write | Inside the service as well as (later) in the UI | The service is the last line before git writes a file; a caller that forgot to validate must not produce an identity git refuses at commit time |
| An email containing a line break | Its own sentence ("must fit on one line") before the whitespace rule | A line break is whitespace, and "cannot contain spaces" would describe it wrongly |
| A key with no value (`[user] name`) | Read as not set | git prints it without a value; it identifies nobody |
| Exit codes other than "nothing found" / "nothing to unset" | Thrown as `GitCommandException` | A broken configuration file or an unset `HOME` is a real failure the page must report |

## Deviations & follow-ups

- None from the plan.
- Follow-up (existing behaviour, not changed here): `GitProcessRunner` logs every command line at
  Debug level, so a name or email written through `git config` shows up there when Debug logging is
  on. The page (PHASE02 onwards) logs its failures without the values.
- Recommendation only: line endings were not examined; nothing in this diff showed CRLF churn.

## Documentation sweep

- Nothing to change: this phase adds no visible feature and writes no new file, so neither the README
  nor the release notes became wrong.

## Build/test evidence

- `dotnet build Enigma.GitClient.slnx --no-incremental`: 0 warnings, 0 errors.
- `dotnet test --solution Enigma.GitClient.slnx`: 2042 passed, 0 failed (58 new), first run.
