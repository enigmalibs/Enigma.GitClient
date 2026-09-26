# FEATURE-3B62-PHASE03 — Dropping one branch onto another

**Item:** FEATURE-3B62 — Selectable rows and branch drops
**Branch:** `feature/feature-3b62-phase03-branch-drop-menu`
**Run:** feature/2026-09-18-columns-selection-minimap

## Summary

A branch row can be dragged onto another branch row, and the drop opens a menu of what that pair can
do: merge the dragged branch into the one it landed on, do it fast-forward only, or merge the other
way round.

The gesture is the one the history page gave up two devs ago, moved to where its two ends are rows a
reader can see, hover and select rather than badges scattered down a graph. What it does is
unchanged — `BranchDropOperations` still checks the target out when it is not the current branch and
then merges, because `git merge` merges into `HEAD` — but **the question moved**: the service used
to raise a dialog offering Merge / Fast-forward only / Cancel, and now takes the mode it was given.
A menu that already names both branches and then a dialog asking the same thing is asking twice.

The reverse merge is in the menu because dragging the pair the wrong way round is the mistake this
gesture invites, and the menu is already naming both ends; it is offered only when the reversed pair
is one the service would carry out, which a remote source is not. A drop the policy refuses — a
branch on itself, or anything onto a remote branch — never opens a menu at all: the drag says so
first, by not marking the row as a target.

## Files / modules touched

**Modified — App**

- `Services/BranchDropOperations.cs` — `DropAsync` takes a `FastForwardMode`; the `ContentDialog`
  and the service's dependency on the dialog service are gone, and the refusal keeps its InfoBar
  sentence. Two summaries now say "the branches list" rather than "the graph"
- `ViewModels/Pages/BranchesPageViewModel.cs` — the `BranchDrop` pair (its in-process drag format,
  the `BranchDropRequest` it stands for, `Reversed`, and the three menu headers that name both
  ends); `MergeDropCommand`, `FastForwardDropCommand` and `MergeReversedDropCommand`, each running
  through the page's existing `Run` helper so the page reloads when the repository changed; the
  static `CanDrop` the drag asks on every pointer move
- `Views/Pages/BranchesPageView.axaml.cs` — the drag: the press that starts it, the drag-over that
  marks the row under the pointer, and the drop that opens the menu at the pointer
- `Views/Pages/BranchesPageView.axaml` — the list accepts drops, and a `droptarget` row wears a ring

**Modified — tests**

- `tests/Enigma.GitClient.App.UnitTests/BranchDropTests.cs` — the mode is passed rather than
  answered through a dialog; the flow tests assert no dialog is raised at all, and the test that
  cancelled one is gone with it — dismissing the menu runs nothing, which is the view's behaviour
  and is covered where the menu is built
- `tests/Enigma.GitClient.App.UnitTests/BranchesPageTests.cs` — a "dropping one branch on another"
  section: what the pair says about its two rows and its three headers, which pairs are offered,
  the merge, the checkout-first case, the reverse merge, a refused remote target, and the rendered
  list accepting drops with a ring on the marked row

**Modified — docs**

- `README.md`, `RELEASENOTES.md`, `docs/roadmap.md`, `docs/plan/FEATURE-3B62.md`

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| Where the question is asked | The menu, once | The pair allows more than one thing, and a dialog after a menu asks the same question twice |
| What the menu offers | Merge, fast-forward-only, and the reverse | Those are this pair's actions; rebase is refused at the process boundary and push is a different flow with its own reporting |
| The reverse item's availability | Only when the reversed pair is valid | A remote branch is never merged into, so offering it would be offering an error |
| What the drag carries | The live row, in process | A branch name alone would not say whether it is remote or checked out, and an in-process format never reaches the platform's clipboard |
| A refused pair | No mark, no menu | The drag says "not here" before the reader lets go, which is better than a menu that explains itself afterwards |
| The drop mark | A ring on the row container | The row underneath may be selected or hovered, and a fill would be a third background competing with both |
| Where the gesture's code lives | The view's code-behind | A drag is a pointer, a drop target and a menu — none of which a binding expresses — and every decision it takes is asked of the ViewModel |

## Deviations & follow-ups

- **None from the plan.** All four acceptance criteria are covered.
- `BranchDropTests.Drop_CancellingRunsNothingAtAll` was deleted rather than rewritten: the service
  has nothing left to cancel. Dismissing the menu runs no command at all, which is a property of the
  menu the view builds, not of the service.
- Setting a branch's upstream by dropping a remote branch onto a local one stays out of scope, as
  planned: `SetUpstreamAsync` asks its own question through a dialog, and pre-selecting the dropped
  branch in it is a change to that flow rather than to this one.
- **Line endings (recommendation only):** no CRLF churn in the touched files. No action taken.

## Build/test evidence

```
dotnet build Enigma.GitClient.slnx
  0 Warning(s)
  0 Error(s)

dotnet test --solution Enigma.GitClient.slnx
  Test run summary: Passed!
  total: 1728  failed: 0  succeeded: 1728  skipped: 0
```

Seven tests added, one deleted, five rewritten around the mode parameter. One fix cycle: the test
file needed `Avalonia.Input` for `DragDrop`.

## Documentation sweep

- `README.md` — the branches bullet gains selection and the drag-to-merge, which FEATURE-3030's
  sweep removed when the gesture left the history page. It now names where it lives.
- `RELEASENOTES.md` — the same under "Working with the repository": selectable rows in all three
  lists, and the drop menu with its three actions and its checkout-first behaviour.

There is no `CLAUDE.md`, `CHANGELOG.md` or `CONTRIBUTING.md` in the repository.
