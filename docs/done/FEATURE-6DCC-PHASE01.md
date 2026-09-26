# FEATURE-6DCC-PHASE01 — Merge engine & conflict model

**Item:** FEATURE-6DCC — Merge & conflict resolution
**Branch:** `feature/feature-6dcc-phase01-merge-engine`
**Run:** feature/2026-09-15-enigma-git-client

## Summary

Merging works, and what it did is reported rather than guessed at. Every outcome git can produce —
already up to date, a fast-forward, a merge commit, a squash, a stop before committing, a conflict,
a refusal — is classified from what git actually wrote, and a conflicted merge comes back with the
list of files to work through. Abandoning a merge puts the repository back exactly as it was, and
committing one is refused while anything is still conflicted.

Beside it sits the conflict model: every file left to resolve, classified into the seven states git
distinguishes, and its three versions read from the index stages rather than from the markers in the
work-tree file. That is what gives a real merge base, and it is what will make PHASE02's preview
exact.

Merging is reachable from the branches page and from the graph's row menu, and the shell's banner
now offers the way out of a merge that went wrong.

## Files / modules touched

**Created — Core**

- `Merging/MergeService.cs` — `FastForwardMode`, `MergeRequest`, `MergeResultKind`, `MergeOutcome`,
  `MergeHeads` and `IMergeService`
- `Merging/ConflictService.cs` — `ConflictKind`, `ConflictFile`, `ConflictSides` and
  `IConflictService`

**Created — App**

- `Services/MergeOperations.cs` — `IMergeOperations`: the merge, the sentence, the confirmation
  before abandoning one

**Modified**

- `ViewModels/Pages/BranchesPageViewModel.cs` + its view — merging a branch into the current one
- `ViewModels/Pages/CommitRowViewModel.cs`, `HistoryPageViewModel.cs` + the history view — the same
  from the graph's row menu
- `ViewModels/MainWindowViewModel.cs`, `Views/MainWindow.axaml` — "Abandon the merge" on the banner
- `DependencyInjection/ServiceCollectionExtensions.cs` (both projects)
- `docs/roadmap.md`, `docs/plan/FEATURE-6DCC.md`

**Created — tests**

- `tests/Enigma.GitClient.Core.IntegrationTests/Merging/MergeServiceTests.cs` — 37 cases against a
  real repository: every outcome, every conflict kind, the three stages, binary and non-ASCII
- `tests/Enigma.GitClient.App.UnitTests/MergeOperationTests.cs` — 10 cases on the two entry points,
  the conflict report and the banner

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| Reading the outcome | git's exit code **and** what it wrote | An exit code alone cannot tell a conflicted merge from a refused one: both are non-zero, and one of them has already changed the repository |
| The conflict list | Read back from the real status, not from git's message | The message names what git noticed; the status is what the user has to work through, and the two are not always the same list |
| The three sides | Index stages 1, 2 and 3 through `git show` | Markers in the work-tree file carry no merge base and break on content that looks like a marker. Stages give git's own bytes, which is what makes an exact preview possible later |
| Reading those stages | Raw bytes, decoded without a byte-order mark | The bytes have to come back exactly as they went in, or a preview of the resolution is not a preview of what will be written. A test pins accented content through the round trip |
| A missing stage | An answer, not a failure | An add/add conflict has no base and a delete/modify has one side. `null` says so; an exception would make the ordinary case look broken |
| Binary detection | A NUL byte in the first 8 000, which is git's own rule | Agreeing with git matters more than being clever: a file git will not merge is one this client must not pretend to |
| `--squash` with `--no-commit` | Never both | A squash already stops before committing; passing both makes git complain about an option the user never chose |
| Committing a merge | Refused while anything is conflicted | git would let a half-resolved merge through, and the message says how many files are left |
| Abandoning one | Confirmed, with the question saying what survives | "Abandon" sounds like it might lose commits; it does not, and saying so is the difference between a usable escape hatch and one nobody dares press |

## Deviations & follow-ups

- **Deviation:** the shell's banner offers "Abandon the merge" but not "Resolve…", because the page
  it would open is PHASE03's. The conflicted files are already visible on the changes page in the
  meantime.
- **Deviation:** `MergeRequest.NoCommit` and `Squash` are modelled and tested but no UI offers them.
  They belong beside a merge dialog, which the plan puts in PHASE03 with the rest of the merge UI.
- **Follow-up:** `GetMergeHeadsAsync` names both sides through `git name-rev`, which answers with the
  first ref that reaches a commit. For a merge in progress that is always the right answer; for an
  arbitrary commit it can be surprising, and nothing else uses it yet.
- **Line endings (recommendation only):** no CRLF churn observed. No action taken.

## Build/test evidence

```
dotnet build Enigma.GitClient.slnx --no-incremental
  0 Warning(s)
  0 Error(s)

dotnet test --solution Enigma.GitClient.slnx
  Test run summary: Passed!
  total: 1212  failed: 0  succeeded: 1212  skipped: 0
```

47 tests are new in this dev, and both suites passed on their first run. The integration suite builds
each conflict kind from a real pair of branches rather than asserting against a captured string, so
the classification is measured against what git actually produced.
