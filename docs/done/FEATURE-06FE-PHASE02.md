# FEATURE-06FE-PHASE02 — Stash management

**Item:** FEATURE-06FE — Remotes & synchronisation
**Branch:** `feature/feature-06fe-phase02-stash`
**Run:** feature/2026-09-15-enigma-git-client

## Summary

Work can be put aside and picked back up. The changes page gains a stash section listing every
entry with its message, the branch it was made on and when — applying it and keeping it, restoring
it and removing it, or dropping it behind a confirmation that says the work cannot be recovered.
Selecting an entry shows what it holds in the same file panel and the same diff viewer the rest of
the client uses, without restoring anything.

"Stash all" sits beside "Stage all", and the stash reference already appears among the repository's
references, so the graph decorates it like any other.

With this phase FEATURE-06FE is complete.

## Files / modules touched

**Created — Core**

- `Stashes/StashService.cs` — `StashEntry`, `IStashService`: list, push, apply, pop, drop, show and
  branch, with the listing's format and its parser

**Modified — App**

- `ViewModels/Panels/DiffViewerViewModel.cs` — `ShowPatch`, for a patch that came from somewhere
  other than a comparison
- `ViewModels/Panels/ChangedFilesPanelViewModel.cs` — the empty state's wording belongs to the host
- `ViewModels/Pages/ChangesPageViewModel.cs` — `StashRowViewModel`, the stash list, its files and
  its four commands
- `Views/Pages/ChangesPageView.axaml` — the stash section and the stash-all button
- `Views/Panels/ChangedFilesPanelView.axaml` — the bound empty state
- `src/Enigma.GitClient.Core/DependencyInjection/ServiceCollectionExtensions.cs`
- `docs/roadmap.md`, `docs/plan/FEATURE-06FE.md`

**Created — tests**

- `tests/Enigma.GitClient.Core.UnitTests/Stashes/StashParsingTests.cs` — 23 cases on the listing's
  format, including a message full of colons
- `tests/Enigma.GitClient.Core.IntegrationTests/Stashes/StashServiceTests.cs` — 18 cases against a
  real repository: pushing with and without untracked files, keeping the index, partial paths,
  applying, popping, dropping by position, a conflicting pop, showing and branching
- `tests/Enigma.GitClient.App.UnitTests/StashPanelTests.cs` — 11 cases on the page's stash section
  and a rendered snapshot

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| Reading the listing | A format template separated by the unit separator | A stash message is free text a user typed and routinely contains colons — which is exactly what the human-readable listing separates on. A test pins a message with two of them |
| The message shown | The branch split off it, the rest kept | git writes `WIP on main: 1234567 Subject` or `On main: what they typed`. The branch belongs in its own column; what is left reads as a message |
| Stashing | `--include-untracked` by default | Leaving untracked files behind makes "your work is stashed" a half-truth |
| Showing an entry | `stash show --patch --include-untracked` | Untracked files live in a third parent of the stash commit; without asking for them the entry looks empty, which is the opposite of the truth |
| Where a shown patch goes | `DiffViewerViewModel.ShowPatch` | The entry's patch came from `stash show`, not from a comparison, so there is no target to re-read and the context options have nothing to act on. Saying so in one method is better than pretending otherwise |
| Which stash actions are confirmed | Drop only | Applying and popping put work back; only dropping loses it. Confirming everything teaches people to click through confirmations |
| Where the stash lives on the page | Its own collapsed section, not in the two lists | It is work that is not in the tree any more, and mixing it in would say otherwise |
| An empty panel's wording | Set by the host | "Select a commit to see the files it changed" is the history's sentence, and it read as nonsense on the staged panel — found by looking at the rendered page |

## Deviations & follow-ups

- **Deviation:** the stash section lives on the changes page rather than in a shell-wide side panel.
  The stash is working-directory state, and that is the page that already owns it; a second home for
  it would be a second place to keep in step.
- **Deviation:** `IStashService.PushAsync` takes a path list and the keep-index flag, both tested,
  but the page's button stashes everything. Partial stashing is the same "which files?" question the
  staging panel already answers, and wiring it to the selection belongs with hunk-level staging.
- **Follow-up:** a stash that conflicts on pop leaves the conflict visible in the status, which
  FEATURE-6DCC's screen will pick up; until then it is reported as a git failure with git's own
  words.
- **Line endings (recommendation only):** no CRLF churn observed. No action taken.

## Build/test evidence

```
dotnet build Enigma.GitClient.slnx --no-incremental
  0 Warning(s)
  0 Error(s)

dotnet test --solution Enigma.GitClient.slnx
  Test run summary: Passed!
  total: 1165  failed: 0  succeeded: 1165  skipped: 0
```

52 tests are new in this dev, and all three suites passed on their first run. One defect was found by
looking at the rendered page: the staged panel's empty state read "Select a commit to see the files
it changed", which is the history's sentence and nonsense on a page with no commit in sight. The
section is written to `snapshots/changes-page-stash.png`.

The recurring source-file hazard appeared once more — writing a unicode escape produced a real
control byte in `StashService.cs` — and was caught by the same scan as last time before it reached a
commit.
