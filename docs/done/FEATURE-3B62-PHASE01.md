# FEATURE-3B62-PHASE01 — Selectable branch and tag rows

**Item:** FEATURE-3B62 — Selectable rows and branch drops
**Branch:** `feature/feature-3b62-phase01-selectable-branches`
**Run:** feature/2026-09-18-columns-selection-minimap

## Summary

A branch or a tag can be selected, by pointer or by keyboard, and it stays selected while the page
goes on rebuilding itself underneath.

Both lists were `ItemsControl`s in `ScrollViewer`s — no selection, no focus, no keyboard. They are
`ListBox`es now, which brings the theme's own selection and hover with them, so the hand-rolled
`:pointerover` background on a row could go.

The branches were the awkward one, because they are grouped: local, then one group per remote. A
`ListBox` per group would keep a selection per group, and two rows would be highlighted at once with
no way to say which of them an action is about. So the view binds one flat `Items` — each group's
heading followed by its rows — built from `Groups`, which stays the model of the grouping. Avalonia
picks a template by the item's type, so a heading and a branch share the list without a selector,
and the heading's container is disabled to make it unselectable — restored to full opacity, because
it is not disabled in the sense the theme means.

Selection is restored **by name** after every rebuild, and the page rebuilds constantly: on a
refresh, after every operation, and on each keystroke in the filter box. A selection that did not
survive that would be a selection nobody could keep. A branch that is gone — deleted, renamed or
filtered out — takes the selection with it, and putting the filter back does not guess that the
reader still wanted it.

## Files / modules touched

**Modified — App**

- `ViewModels/Pages/BranchesPageViewModel.cs` — the `IBranchListItem` contract and the
  `BranchGroupHeaderViewModel` item; `Items`, `SelectedItem`, `SelectedBranch` and `SelectedTag`;
  `Rebuild` flattens the groups and calls `RestoreSelection`, which re-selects by name
- `Views/Pages/BranchesPageView.axaml` — the tag `ItemsControl` becomes `TagList`, the nested branch
  `ItemsControl`s become one `BranchList` with a template per item type; the item styles (padding,
  stretch), the heading's disabled container, and its opacity restored

**Modified — tests**

- `tests/Enigma.GitClient.App.UnitTests/BranchesPageTests.cs` — a "selection" section: the flat
  list's shape against the groups, headings not selectable, the selected branch, selection surviving
  a refresh and following the name rather than the instance, a filter and a delete both clearing it,
  a tag's selection being its own, and the rendered page's `ListBox` selection agreeing with the
  page — including a heading container that is disabled and fully opaque

**Modified — docs**

- `docs/roadmap.md`, `docs/plan/FEATURE-3B62.md`

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| `Groups` or `Items` | Both: `Groups` is the model, `Items` is it flattened for the view | The grouping is real and the tests that describe it stay honest; flattening is a presentation concern, rebuilt from the groups every time |
| Headings in the list | Items with a disabled container | Keeps a heading scrolling with its branches — the reason it is in the list at all — while nothing can select it |
| A disabled heading's look | Opacity restored to 1 | It is not unavailable; it is a label. The theme's dimming would say the wrong thing |
| The tag list's selection | Its own property | The two lists are alternatives, and a tag selected while the branches are on screen is a selection nobody can see |
| Restoring a selection | By name, after the rebuild | Every row is a new object, and the list is cleared before it is refilled — which nulls the selection through the two-way binding, so the name has to be captured first |
| A filtered-out selection | Cleared | Anything else means a selected row nobody can see, and an action running against a branch the reader has scrolled away from |
| The row's own hover | Removed in favour of the list's | Two hover backgrounds on one row is one too many, and the list's is the one that agrees with its selection |

## Deviations & follow-ups

- **None from the plan.** All five acceptance criteria are covered.
- The rendered snapshot `branches-page.png` was inspected: both group headings read normally, the
  rows keep their pills, counts, actions and separators.
- Nothing yet acts on the selection — it is what PHASE03's drop menu and any future details pane
  will use. That is the phase order, not an omission.
- **Line endings (recommendation only):** no CRLF churn in the touched files. No action taken.

## Build/test evidence

```
dotnet build Enigma.GitClient.slnx
  0 Warning(s)
  0 Error(s)

dotnet test --solution Enigma.GitClient.slnx
  Test run summary: Passed!
  total: 1719  failed: 0  succeeded: 1719  skipped: 0
```

Seven tests added. No fix cycle was needed: the existing branch and tag tests drive the ViewModel's
groups and commands, which this dev leaves exactly as they were.

## Documentation sweep

Scanned `README.md` and `RELEASENOTES.md`. Both describe what the branches and tags page *does*
(create, rename, delete, set upstream, check out), not how a row is picked; selecting a row adds no
capability to state. There is no `CLAUDE.md`, `CHANGELOG.md` or `CONTRIBUTING.md` in the repository.
Nothing edited.
