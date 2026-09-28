# FEATURE-2074-PHASE01 — One line per stash in the history

**Item:** FEATURE-2074 — Stashes like GitKraken
**Branch:** `feature/feature-2074-phase01-one-line-per-stash`
**Run:** bugfix/2026-09-28-history-stash-menus-release

## Summary

git records a stash entry as a merge commit **W** with up to three parents:

- the commit it was made on;
- an *index* commit ("index on main: …");
- an *untracked files* root commit, when untracked files were stashed.

The history walked `--all`, which reaches W only through `refs/stash`, so it drew W with the stash badge
and the helper commits as lines merging into it. Older entries were not drawn at all.

Every stash entry is now one line, as GitKraken draws it:

- **At the entry's own commit**, the SHA `git stash list` names. It carries git's message, the one
  GitKraken shows: `WIP on main: <base> <subject>` or `On main: <message>`. The line has a stash badge
  (Archive icon) named after its entry: `stash@{0}`, `stash@{1}`, and so on.
- **Drawn off the commit it was made on**: it has a single parent in the layout, so it is not a merge.
- **git's helper commits are not lines.** A helper commit that some reference happens to point at is
  kept, so that reference's badge is still drawn.
- **Every entry is walked.** The history reads the stash list with each reload and walks each entry's
  commit too (`CommitLogQuery.IncludedRevisions`, after `--all`).
- **A dropped older entry redraws the history.** That drop moves no reference, so the automatic refresh
  (`RefreshInPlaceAsync`) now also compares the stash list with the one drawn.

## Files / modules touched

**Modified**

- `src/Enigma.GitClient.Core/History/CommitLogQuery.cs` — `IncludedRevisions`.
- `src/Enigma.GitClient.Core/History/CommitLogReader.cs` — included revisions go after `--all`. Only
  4–64 hex digits are accepted, so nothing can be read as an option or a range.
- `src/Enigma.GitClient.App/ViewModels/Pages/HistoryPageViewModel.cs`:
  - takes `IStashService`;
  - `ReadStashesAsync` and `UseStashes`;
  - `FoldStashes`, whose helper set persists across pages and is reset on reload;
  - `WithoutStashRef`;
  - the stash comparison in `RefreshInPlaceAsync`.
- `src/Enigma.GitClient.App/ViewModels/Pages/CommitRowViewModel.cs` — `Stash`, `IsStash`, and the stash
  badge first.

**Created**

- `tests/Enigma.GitClient.App.UnitTests/HistoryStashTests.cs` — 4 tests over real git:
  - one line, with the right SHA, message and badge, drawn off its base, with no helper lines;
  - two entries;
  - an older drop is redrawn;
  - a quiet refresh leaves the lines alone.

**Modified tests**

- `tests/Enigma.GitClient.Core.UnitTests/History/CommitLogArgumentsTests.cs` — placement,
  non-object-names left out (theory of 8), and other scopes.
- `docs/roadmap.md`, `docs/plan/FEATURE-2074.md` — statuses.

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| Where the stash list is read | In `LoadPageAsync`, with the first page (`Skip == 0`), after the working-tree probe and before the cancellation check | Every page of one reading walks from the same entries, and a cancelled load uses nothing it read |
| A helper commit that a reference points at | Kept as a line | Its badge must be drawn somewhere. A branch at git's index commit is exotic, but losing its badge would be worse. |
| The stash's commit given to the row | Rebuilt with its first parent only | The graph layout and anything reading `ParentShas` see the line as it is drawn |

## Deviations & follow-ups

- None from the plan.
- Test note: `SettingsServiceTests.ChangingSomethingRaisesTheEventWithTheNewSettings` (Core, untouched by
  this dev) failed once in the first full run. It passed on three reruns of the Core suite and on the
  next full run. It looks like a pre-existing timing flake, worth a look outside this run.
- Line endings: no CRLF churn.

## Documentation sweep

No README statement became inaccurate. The stash is presented in the history by PHASE03, and the release
notes will describe it. No edit.

## Build/test evidence

- `dotnet build Enigma.GitClient.slnx`: 0 warnings, 0 errors.
- `dotnet test --solution Enigma.GitClient.slnx`: **2382 passed**, 0 failed, 0 skipped (14 new:
  4 App, 10 Core argument cases), with no fix cycle. See the flake note above.
