# FEATURE-478C-PHASE01 — Branch management

**Item:** FEATURE-478C — Branches, tags & checkout
**Branch:** `feature/feature-478c-phase01-branch-management`
**Run:** feature/2026-09-15-enigma-git-client

## Summary

Branches are now something the client manages rather than something it displays. A new branches page
lists the local branches and each remote's, with the checked-out one marked, the upstream and how
far ahead or behind it is, the tip commit's subject, author and age, and a filter box. Every branch
can be created, checked out, renamed, given or cleared of an upstream, and deleted — from the page's
own rows or from the graph's context menu, which offers the same operations on the commit under the
pointer.

Nothing that could lose work happens without being asked first, and the question names what is at
risk: deleting an unmerged branch lists the commits that exist nowhere else, and deleting a branch
on a remote says it changes the remote for everyone.

## Files / modules touched

**Created — Core**

- `Refs/RefNameValidator.cs` — `RefNameValidation` and git's own name rules, applied per path
  component, with a message naming the rule that was broken
- `Git/GitOperationRefusedException.cs` — the client declining before git is asked, kept apart from
  a git failure so the UI can show a sentence rather than standard error
- `Branches/BranchService.cs` — `IBranchService`: create, rename, delete, delete on a remote, set
  upstream, check out, check out a remote branch as a tracking branch, and the two questions the
  delete confirmation needs (`IsMergedAsync`, `GetUnmergedCommitsAsync`)

**Created — App**

- `Services/BranchOperations.cs` — `IBranchOperations`: each operation as a user performs it — the
  form, the confirmation, the write under the repository lock, the sentence afterwards
- `ViewModels/Dialogs/BranchDialogViewModels.cs` — the create, rename and set-upstream dialogs
- `Views/Dialogs/{CreateBranch,RenameBranch,SetUpstream}DialogView.axaml` (+ code-behind)

**Rewritten**

- `ViewModels/Pages/BranchesPageViewModel.cs` — `BranchRowViewModel`, `BranchGroupViewModel` and the
  page, which shows and filters and delegates every write
- `Views/Pages/BranchesPageView.axaml` — the toolbar, the grouped list and the per-row menu

**Modified**

- `src/Enigma.GitClient.Core/DependencyInjection/ServiceCollectionExtensions.cs`
- `src/Enigma.GitClient.App/DependencyInjection/ServiceCollectionExtensions.cs`
- `src/Enigma.GitClient.App/ViewModels/Pages/CommitRowViewModel.cs` — `HistoryRowCommands`, and what
  a row knows about the branch pointing at it
- `src/Enigma.GitClient.App/ViewModels/Pages/HistoryPageViewModel.cs` — the row menu's handlers
- `src/Enigma.GitClient.App/Views/Pages/HistoryPageView.axaml` — the graph row's context menu
- `tests/.../Infrastructure/UiServiceDoubles.cs` — the scripted dialog can now answer a sequence and
  fill a form before answering
- `docs/roadmap.md`, `docs/plan/FEATURE-478C.md`

**Created — tests**

- `tests/Enigma.GitClient.Core.UnitTests/Refs/RefNameValidatorTests.cs` — 44 cases over the accepted
  and rejected names, each rejection checked for naming its rule
- `tests/Enigma.GitClient.Core.IntegrationTests/Branches/BranchServiceTests.cs` — 25 cases against a
  real repository, including a bare one standing in as a remote
- `tests/Enigma.GitClient.App.UnitTests/BranchesPageTests.cs` — 25 cases on the page, the dialogs,
  every destructive path answered both ways, the graph's menu, and a rendered snapshot

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| Where the operation flow lives | One `IBranchOperations` service, called by both the page and the graph menu | Two places offering the same operation have to ask the same question. A delete that names the commits at risk in one place and not the other is a delete that will lose work in the other |
| What a refusal is | A distinct `GitOperationRefusedException`, thrown before git runs | git was never asked, so there is no standard error to show. The distinction is what lets the UI print "that branch is checked out" instead of a git diagnostic |
| Name validation | A local implementation of git's rules, per path component, run on every keystroke | `git check-ref-format` is the authority and still runs behind every write, but it cannot answer per keystroke and answers in one terse line. The local check says which rule broke, while the name is being typed |
| `IsMergedAsync` | `git merge-base --is-ancestor`, reading the exit status | The exit status is the answer, not an error. Parsing `branch --merged` output would mean matching names, which is the part that goes wrong |
| Deleting an unmerged branch | Refused by the service; the UI asks with the commit subjects listed, then passes `force` | The refusal cannot be bypassed by a second caller, and the confirmation is specific enough to decide on |
| Every confirmation's default button | The close button | A stray Enter must never delete a branch. The destructive button is always the one that has to be aimed at |
| The branch list's shape | Groups rendered as plain item lists, not one selectable list | Actions live on the row, so no cross-group selection has to be maintained, and a group header can never be selected by accident |
| Checking out a remote branch | Creates the tracking branch and says so in the info bar | That is what git does, and the user should know a local branch now exists — silently creating refs is how a repository fills up with branches nobody made deliberately |

## Deviations & follow-ups

- **Deviation:** the graph's context menu names one branch per row — the local branch pointing at the
  commit, or the remote one when there is no local. A commit carrying several branches is rare, and
  a submenu generated per badge would have to rebuild its command bindings inside a popup tree. The
  branches page reaches every branch by name. Recorded as a refinement.
- **Deviation:** "merge into current" is listed in the plan's per-row actions but is not here: the
  merge itself is FEATURE-6DCC's, and an action that cannot complete is worse than one that is not
  offered yet.
- **Deviation:** `DeleteRemoteAsync` is implemented and tested against a local bare repository, but
  the push it performs will only reach a real remote once FEATURE-06FE brings credentials and
  progress with it.
- **Follow-up:** the page has no keyboard shortcuts yet (Enter to check out, Delete to delete).
  Keyboard handling is worth doing once across the whole shell rather than page by page.
- **Line endings (recommendation only):** no CRLF churn observed. No action taken.

## Build/test evidence

```
dotnet build Enigma.GitClient.slnx --no-incremental
  0 Warning(s)
  0 Error(s)

dotnet test --solution Enigma.GitClient.slnx
  Test run summary: Passed!
  total: 889  failed: 0  succeeded: 889  skipped: 0
```

94 tests are new in this dev. The Core suites passed on their first run; one App test failed because
every commit in its own fixture happened to carry a branch, which was the fixture's fault rather than
the code's. Two cosmetic defects were found by looking at the rendered page — a badge with no plate
behind it, and rows with no separator, which turned three two-line entries into one block of text.
The page is written to `snapshots/branches-page.png`.
