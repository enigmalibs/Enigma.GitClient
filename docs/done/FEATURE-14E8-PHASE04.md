# FEATURE-14E8-PHASE04 — Dragging branches in the graph

**Item:** FEATURE-14E8 — History: diffs, badges and dragging
**Branch:** `feature/feature-14e8-phase04-branch-drag`
**Run:** feature/2026-09-18-history-and-diffs

## Summary

A branch badge can now be dragged onto another one, which runs the operation PHASE03 built: the
question, the checkout when the target is not the branch HEAD is on, and the merge.

The gesture lives on the page rather than in the row template, because the rows are a virtualised
list — a handler attached inside the template would be attached and detached again for every row
that scrolls past. A tunnelling `PointerPressed` notices a press that landed on a branch badge and
starts the drag; `DragOver`, `DragLeave` and `Drop` walk from the event's source up to the
`RefBadge` it landed on and ask the page whether that pair means anything.

`HistoryPageViewModel.CanDropBranch` is that question, and it is static and free of side effects
because `DragOver` asks it on every pointer move: it refuses anything that is not a branch at both
ends — a tag or the stash names a commit, and there is nothing to merge out of one — and then defers
to `BranchDropOperations.CanDrop` for the rest of the policy rather than restating it.
`HistoryPageViewModel.Request` turns the two badges into the request, so the mapping from "these two
pills" to "these two branch names" exists once.

The badge under a valid drag wears a `droptarget` ring in the accent colour. A ring rather than a
fill: the badge's own colour is what says which kind of reference it is, and a drag must not take
that away while it is being read. The template gained a 1 px transparent border for it to colour,
with the padding reduced by the same pixel so no badge changed size.

## Files / modules touched

**Modified — App**

- `Views/Pages/HistoryPageView.axaml.cs` — the drag: a tunnelling `PointerPressed` that starts it,
  `DragOver`/`DragLeave`/`Drop` that accept it, badge hit-testing from the event source, and the
  drop-target highlight
- `Views/Pages/HistoryPageView.axaml` — `DragDrop.AllowDrop` on the commit list, and a tooltip on
  the badge strip saying the gesture exists
- `ViewModels/Pages/HistoryPageViewModel.cs` — `BranchDrop`, `DropBranchCommand`, the static
  `CanDropBranch` and `Request`, and `IBranchDropOperations` injected
- `ViewModels/Pages/CommitRowViewModel.cs` — `RefBadgeItem.DragFormat`, an in-process
  `DataFormat<RefBadgeItem>`
- `Themes/Controls.axaml` — the badge template's border, and the `droptarget` style

**Modified — tests**

- `tests/Enigma.GitClient.App.UnitTests/HistoryPageTests.cs` — the policy as a table (seven pairs),
  a missing end, the request mapping, a drop that merges and re-reads the history, a drop the
  command refuses without asking anything, `AllowDrop` on the shown list, and the badge's
  drop-target ring appearing and going away with the class

**Modified — docs**

- `docs/roadmap.md`, `docs/plan/FEATURE-14E8.md`, `README.md`, `RELEASENOTES.md`

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| Where the drag is started | From `PointerPressed`, not from a move past a threshold | Avalonia 12's `DragDrop.DoDragDropAsync` takes the `PointerPressedEventArgs` — it tracks that pointer — and holding those arguments back to a later move would hand it an event already dispatched. The platform's drag session is what decides a pointer that never moved was a click |
| Not marking the press handled | Deliberate | The list still selects the row under the badge, which is what a user expects from a click that turns out not to be a drag |
| Only branches start a drag | Yes | A tag or the stash has nothing to merge out of it; refusing at the source means no pointless drag session ever begins |
| Where the badge is found | Walking up from the event's source | The hit lands on the template's `Border`, not on the `RefBadge`, because that is what carries the background |
| How the target is marked | A class toggled from the code-behind, styled in the theme | The highlight is transient view state, not something the row's ViewModel should carry through a virtualised list |
| The ring's pixel | Border thickness 1 with the padding reduced to `4,0` | The badge keeps the size it had, so the column measured by `RefBadgeMetrics` is still right |

## Deviations & follow-ups

- **The Avalonia 12 drag API is not the one the plan assumed.** `DataObject` is obsolete,
  `DragDrop.DoDragDrop` is gone, and `DragEventArgs.Data` is now `DragEventArgs.DataTransfer`. The
  replacements are `DataFormat.CreateInProcessFormat<T>`, `DataTransferItem.Create`, `DataTransfer`
  and `DragDrop.DoDragDropAsync`, which is what this phase uses. The in-process format is a better
  fit than the old string-keyed one: the payload is the live `RefBadgeItem` and is never handed to
  the platform clipboard.
- **The gesture itself is not unit-tested, and cannot honestly be.** Avalonia's headless platform
  has no drag session, so what is covered is everything the gesture decides with — the policy, the
  request mapping, the command, `AllowDrop` on the real list and the highlight class's effect. The
  pointer plumbing between them is four handlers with no branching of their own.
- **Follow-up.** A drag that is dropped on nothing gives no feedback beyond the cursor. A "drop a
  branch here" affordance was not asked for and is not built.
- **Line endings (recommendation only):** no CRLF churn in the touched files. No action taken.

## Build/test evidence

```
dotnet build Enigma.GitClient.slnx
  0 Warning(s)
  0 Error(s)

dotnet test --solution Enigma.GitClient.slnx
  Test run summary: Passed!
  total: 1676  failed: 0  succeeded: 1676  skipped: 0
```

Thirteen tests are new. One fix cycle was needed, for the Avalonia 12 drag API above.

## Documentation sweep

Scanned `README.md` and `RELEASENOTES.md`. Both list merging as something started from a branch or a
row menu, and this phase adds a way neither mentions. A line is added to each — the README's feature
list and the release notes' graph section — naming the gesture and the two operations it offers.
There is no `CLAUDE.md`, `CHANGELOG.md` or `CONTRIBUTING.md` in the repository.
