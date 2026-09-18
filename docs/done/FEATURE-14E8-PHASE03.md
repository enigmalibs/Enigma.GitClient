# FEATURE-14E8-PHASE03 — Merging by dropping a branch

**Item:** FEATURE-14E8 — History: diffs, badges and dragging
**Branch:** `feature/feature-14e8-phase03-branch-drop`
**Run:** feature/2026-09-18-history-and-diffs

## Summary

What dropping one branch onto another *does*, ready for the gesture that will do it in PHASE04.

`IBranchDropOperations.DropAsync` takes a `BranchDropRequest` — the two branches, whether either is
remote, and whether the target is already checked out — and composes the two operations services
that already exist. It asks which of the two joins was meant (`Merge`, or `Fast-forward only`),
checks the target out when it is not the branch HEAD is on, and merges the source into it with
`FastForwardMode.WhenPossible` or `.Only`. Nothing new reaches git: `IBranchOperations.CheckoutAsync`
still asks the dirty-tree questions and `IMergeOperations.MergeAsync` still reports the outcome,
conflicts included.

The checkout is not incidental. `git merge` merges into `HEAD`, so "merge A into B" is only
meaningful while standing on B — which is why the drop moves there first, and why a cancelled drop
must not move anything at all. `DropAsync` returns "the repository changed" rather than "the merge
did something", because a checkout on its own has already moved HEAD and the graph has to be
re-read for it.

`CanDrop` is a static, side-effect-free predicate: the drag in PHASE04 needs the same answer on
every pointer move to know whether it may land, and that question cannot cost a dialog or a git
call. It refuses a branch dropped on itself and any drop whose **target** is remote — a
remote-tracking ref is changed by pushing to it, not by merging into it here — and `DropAsync`
turns the same refusal into a sentence in the info bar rather than letting git refuse in words
naming a ref the reader never typed. A remote branch as the *source* is fine: merging
`origin/main` into a local branch is an ordinary thing to want.

Rebase is not offered and never will be — the README lists it as a permanent, structural non-goal
and `ForbiddenGitOperations` refuses the verb.

## Files / modules touched

**Created — App**

- `Services/BranchDropOperations.cs` — `BranchDropRequest`, `IBranchDropOperations`, the default
  implementation and the static `CanDrop` policy

**Modified — App**

- `DependencyInjection/ServiceCollectionExtensions.cs` — the new service registered beside the other
  operations services

**Created — tests**

- `tests/Enigma.GitClient.App.UnitTests/BranchDropTests.cs` — the policy as a table, and six flows
  against real repositories: a merge onto the current branch, a merge that checks the target out
  first, a fast-forward that works, a fast-forward that is refused with a reason, a cancellation
  that runs nothing (not even the checkout), and the two refusals that happen before anything is
  asked

**Modified — tests**

- `tests/Enigma.GitClient.App.UnitTests/CompositionRootTests.cs` — a theory covering every
  operations service, the new one included

**Modified — docs**

- `docs/roadmap.md`, `docs/plan/FEATURE-14E8.md`

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| Where the drop policy lives | A static `CanDrop` on the service | The drag asks it on every pointer move; a predicate that could show a dialog or run git is not one to call from a `DragOver` |
| A remote branch as the source | Allowed | Merging `origin/main` into a local branch is ordinary; only the *target* has to be local, because that is the side being written |
| What `DropAsync` returns | Whether the repository changed, not whether the merge did | The checkout alone moves HEAD, so "already up to date" after a checkout still leaves the graph to re-read |
| The dialog's wording | Names both branches in the title and says whether a checkout comes first | The drop is two operations when the target is not current, and a reader agreeing to one should not be surprised by the other |
| `DefaultButton` | `Primary` | A drag onto a branch is a deliberate act; the ordinary merge is what it usually means, and Escape still cancels |
| Reporting a refusal | The info bar, before the dialog | Raising a dialog only to explain that its two buttons would both fail is a question with no answer |

## Deviations & follow-ups

- **None from the plan.** All six acceptance criteria are covered.
- **Nothing is wired to this yet.** The service has no caller until PHASE04 adds the gesture; that is
  the phase split, not an omission.
- **Follow-up.** The branches page could offer the same drop between its rows, which would need
  nothing new — it is the same request record. Not asked for, so not built.
- **Line endings (recommendation only):** no CRLF churn in the touched files. No action taken.

## Build/test evidence

```
dotnet build Enigma.GitClient.slnx
  0 Warning(s)
  0 Error(s)

dotnet test --solution Enigma.GitClient.slnx
  Test run summary: Passed!
  total: 1663  failed: 0  succeeded: 1663  skipped: 0
```

Nineteen tests are new (thirteen in `BranchDropTests`, six theory cases added to the composition
root's operations-service coverage). No fix cycle was needed: the suite was green on its first run.

## Documentation sweep

Scanned `README.md` and `RELEASENOTES.md`. Both describe merging as something started from a branch
or a row menu, which is still true and still complete — the drop gesture does not exist until
PHASE04, so claiming it now would describe something a user cannot do. The documentation for this
capability lands with that phase. There is no `CLAUDE.md`, `CHANGELOG.md` or `CONTRIBUTING.md` in
the repository. Nothing edited.
