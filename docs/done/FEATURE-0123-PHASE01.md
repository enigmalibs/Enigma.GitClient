# FEATURE-0123-PHASE01 — Read a remote's tags in Core

**Item:** FEATURE-0123 — Remote mark on tag badges
**Branch:** `feature/feature-0123-phase01-remote-tags-core`
**Run:** feature/2026-11-05-history-badges-release

## Summary

Core can now say which tags a remote has, and on which commits. git keeps no remote-tracking tags, so
this is the one read that tells whether a tag was pushed.

- `ITagService.ListRemoteAsync(repository, remote)` runs `git ls-remote --tags -- <remote>`, signed in
  with the repository's credentials as a tag push is. It returns each tag's name with its commit. A
  failure (offline, refused, an unknown remote) throws `GitCommandException`, as the other remote
  calls do.
- `RemoteTagParser.Parse` reads the output:
  - an annotated tag's peeled `^{}` line gives its commit, whichever order its two lines come in;
  - non-tag refs, lines without a tab, non-hex or wrong-length shas and empty names are skipped.

## Files/modules touched

- Created:
  - `src/Enigma.GitClient.Core/Tags/RemoteTagParser.cs`
  - `tests/Enigma.GitClient.Core.UnitTests/Tags/RemoteTagParserTests.cs`
  - `docs/done/FEATURE-0123-PHASE01.md`
- Modified:
  - `src/Enigma.GitClient.Core/Tags/TagService.cs`: `ListRemoteAsync`; `RunOnRemoteAsync` returns the
    result.
  - `tests/Enigma.GitClient.Core.IntegrationTests/Tags/TagAndCheckoutServiceTests.cs`
  - `tests/Enigma.GitClient.Core.UnitTests/Hosting/SignedInOperationsTests.cs`
  - `docs/roadmap.md`, `docs/plan/FEATURE-0123.md`: statuses.

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| `--` before the remote | `ls-remote --tags -- <remote>` | The remote's name is never read as an option (`--upload-pack=…`), whatever it is called; checked with git 2.56 |
| Return type | `IReadOnlyDictionary<string, string>` (name → commit) | What the badge needs, and nothing more |
| Sha check | 40 or 64 hex characters | SHA-1 and SHA-256 repositories |
| Test doubles | None to update | `TagService` is the only `ITagService` |

## Deviations & follow-ups

- Two Desktop tests failed in this dev's first full run, then passed in the second:
  `HistoryDragMergeTests.DroppingOnARemoteBranchOrOnItself_OffersNothing` and
  `RepositoriesPageTests.OpeningARepository_PublishesItRemembersItAndShowsTheHistory`. This dev
  changed no Desktop code. Like the baseline's flake (`docs/done/FEATURE-D7D5-PHASE01.md`), this looks
  like async work outliving its test on the shared headless dispatcher. Worth a dedicated follow-up.

## Build/test evidence

- Baseline: see `docs/done/FEATURE-D7D5-PHASE01.md`.
- `dotnet build Enigma.GitClient.slnx`: **0 warnings, 0 errors**.
- `dotnet test --solution Enigma.GitClient.slnx`: 3,616 total, 3,613 passed, 3 skipped, **0 failed**.
  This was the second run; the first had the 2 flakes above.
- New tests (10):
  - `RemoteTagParserTests` (6): lightweight, annotated, peeled-first order, skipped lines, empty,
    slashed names;
  - `TagAndCheckoutServiceTests` (+3, real git, bare `origin`): pushed lightweight and annotated tags
    are listed at their commits and an unpushed one is not; an empty remote; an unknown remote throws;
  - `SignedInOperationsTests` (+1): the read signs in, with `--tags -- origin`.
- Fix budget: 0 cycles.

## Documentation sweep

- Core only; no user-facing document describes it. No edit.
