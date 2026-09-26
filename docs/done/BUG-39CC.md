# BUG-39CC — Merge fast-forwards instead of merging

**Item:** BUG-39CC — Merge fast-forwards instead of merging
**Branch:** `bugfix/bug-39cc-merge-no-ff`
**Run:** bugfix/2026-09-26-standard-merge-no-ff

## Summary

A "Merge" — the manual merge's **Merge**, the history's **Merge "X" into "Y"** after setting a merge
source — fast-forwarded the destination whenever it was simply behind, so it recorded no merge commit
and did exactly what the fast-forward-only item beside it does. Git was doing what it was asked:
every "Merge" entry point passed `FastForwardMode.WhenPossible`, which is a bare `git merge`, and
git's default is to fast-forward when it can. Only `FastForwardMode.Never` adds `--no-ff`, and
nothing in the UI used it.

Every "Merge" the app offers now asks for `FastForwardMode.Never`, so it always records a merge
commit whose second parent is the source:

- the branches view: the manual merge's **Merge**, the drop menu's merge and its reverse, and the
  row menu's "Merge into the current branch";
- the history: **Merge "X" into "Y"** from the merge source, and "Merge "X" into "<current>"".

The fast-forward-only items are unchanged: they move the branch without a merge commit and refuse a
diverged pair. The meaning lives in the App layer — `IMergeOperations.MergeAsync` and
`IBranchDropOperations.DropAsync` now default to `Never`, and the ViewModels pass it explicitly —
while Core's `MergeRequest` keeps git's default, because Core mirrors git. Diverged merges,
conflicts and an already-contained source behave and report exactly as before
(`MergeService.Classify` already read a `--no-ff` merge as `Merged`).

## Files / modules touched

**Modified — App**

- `Services/MergeOperations.cs` — `MergeAsync` defaults to `FastForwardMode.Never`, with the reason
  in the interface's remarks
- `Services/BranchDropOperations.cs` — `DropAsync` defaults to `FastForwardMode.Never`
- `ViewModels/Pages/BranchesPageViewModel.cs` — `MergeDropCommand`, `MergeReversedDropCommand`,
  `ManualMergeCommand` and the row menu's `MergeCommand` pass `Never`
- `ViewModels/Pages/HistoryPageViewModel.cs` — `MergeInto` and `MergeIntoCurrent` pass `Never`
- `Views/Pages/BranchesPageView.axaml` — the manual merge buttons' tooltips say what each does

**Docs**

- `README.md` — a feature line: every merge records a merge commit; the fast-forward-only merges move
  a branch without one
- `RELEASENOTES.md` — the same, under *Merge*

**Tests**

- `App.UnitTests/Infrastructure/GitProbe.cs` — new: a commit's parent count and a revision's hash,
  read with git itself
- `App.UnitTests/ManualMergeTests.cs` — **Merge** asks for `Never`; the recording double's default
  matches the interface
- `App.UnitTests/BranchesPageTests.cs` — on a destination simply behind the source: the drop menu's
  merge, its reverse and the manual **Merge** record a two-parent commit; the drop menu's and the
  manual fast-forward move the branch onto the source with no merge commit
- `App.UnitTests/HistoryMergeSourceTests.cs` — merge-into records a merge commit whose second parent
  is the source; fast-forward-into moves the branch with none; merge-into-current records one
- `App.UnitTests/MergeOperationTests.cs` — the row menu's merge into the current branch records a
  merge commit; `IMergeOperations.MergeAsync` records one unless a fast-forward is asked for
- `App.UnitTests/BranchDropTests.cs` — `DropAsync` records a merge commit unless a fast-forward is
  asked for; the fast-forward-only drop now also asserts a single parent

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| Rely on the new defaults, or pass the mode at each call site | Both: defaults changed, and every ViewModel call site passes `Never` explicitly | A menu item's meaning should be readable where it is wired; the default covers any future caller |
| How tests tell a merge from a fast-forward | Ask git for the commit's parent count (`rev-list --parents`) | It is a fact about the graph, independent of what the client reported |
| Where that probe lives | One shared `Infrastructure/GitProbe` instead of a copy per test class | Four classes need it; the per-class git helpers only run commands |
| Proving the tests catch the bug | Ran the new tests against the App code with `WhenPossible` put back (not committed) | The 9 merge-commit tests failed and the fast-forward-only ones passed — exactly the reported behaviour |
| Doc wording | "the fast-forward-only merges" rather than "the item beside it" | The row menu's "Merge into the current branch" has no fast-forward item beside it |

## Deviations & follow-ups

- None from the plan.
- Follow-up, not done: the manual merge's buttons and the menus keep their labels; if "Merge" still
  reads as ambiguous in use, a "(merge commit)" suffix is a one-line change per header.
- Recommendation only: no CRLF churn in this diff — the touched files are LF, as `.gitattributes`
  asks.

## Documentation sweep

- `README.md` — the new feature line on what a merge records.
- `RELEASENOTES.md` — the same sentence under *Merge*.
- Nothing else user-facing described the merge style.

## Build/test evidence

- Baseline: `dotnet build Enigma.GitClient.slnx --no-incremental` 0 warnings, 0 errors;
  `dotnet test --solution Enigma.GitClient.slnx` 2103 passed.
- After the change: `dotnet build Enigma.GitClient.slnx --no-incremental` 0 warnings, 0 errors;
  `dotnet test --solution Enigma.GitClient.slnx` 2114 passed, 0 failed (11 new), with no fix cycle.
