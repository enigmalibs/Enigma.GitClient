# FEATURE-0FBE-PHASE01 — The tags page and its rail item

**Item:** FEATURE-0FBE — Tags get their own page
**Branch:** `feature/feature-0fbe-phase01-tags-page`
**Run:** feature/2026-09-21-refs-tags-and-dragging

## Summary

Tags have a page of their own, reached from a **Tags** item directly under **Branches** in the
navigation rail.

`TagsPageViewModel` lists, filters and selects the repository's tags and offers create, check out and
delete through the same `ITagOperations` and `ICheckoutOperations` the graph's context menu already
calls — so the two places asking for a tag to be deleted ask the same question. `TagsPageView` is
deliberately the branches page's twin: the same header (icon, title, filter box, a "New tag" button
and refresh), the same row, the same pills, the same empty state, the same `ListBox` conventions.
What it does not share is the drag gesture, because dropping one reference onto another means a
merge and a tag is not something to merge.

`TagRowViewModel` moved out of `BranchesPageViewModel.cs` into the new file on the way, and stopped
holding a back-pointer to the page that built it: it now takes the two commands it offers. That is
what lets two pages build tag rows from the same type, which is what makes this phase additive — the
branches page still shows tags, and loses them in PHASE02.

## Files / modules touched

**Created — App**

- `ViewModels/Pages/TagsPageViewModel.cs` — the page, and `TagRowViewModel` moved into it
- `Views/Pages/TagsPageView.axaml` (+ `.axaml.cs`) — the page's view

**Modified — App**

- `ViewModels/Pages/BranchesPageViewModel.cs` — `TagRowViewModel` removed from this file; its rows
  are built with the page's two tag commands
- `DependencyInjection/ServiceCollectionExtensions.cs` — `TagsPageView` transient,
  `TagsPageViewModel` singleton, like every other page
- `Navigation/ShellNavigation.cs` — `ShellPage.Tags`, and the rail item between Branches and Remotes

**Created — tests**

- `tests/Enigma.GitClient.App.UnitTests/TagsPageTests.cs` — the page with no repository open; the
  tags it lists with their kind; filtering, clearing the filter and the empty message; a selection
  that survives a refresh and goes with a tag the filter hides; create, cancelled create, the delete
  confirmation and the delete; the detach warning and the checkout; the row carrying the page's
  commands; and the view showing one row per tag with its own empty state

**Modified — tests**

- `MainWindowShellTests.cs` — the rail has six items, Tags between Branches and Remotes
- `CompositionRootTests.cs` — the new ViewModel resolves and observes the one repository context
- `ShellRenderTests.cs` — the new view builds, lays out and sizes its icons from the scale

**Modified — docs**

- `docs/roadmap.md`, `docs/plan/FEATURE-0FBE.md`

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| How `TagRowViewModel` moves without breaking the branches page | It takes its two commands instead of an owner page | The plan said the branches page would "hold a reference to the moved type", which is not possible while the type names one page as its owner. Passing the commands is both the smaller change and the better design: a row does not need to know which page built it |
| Command names on the new page | `CreateCommand`, `CheckoutCommand`, `DeleteCommand` | On a page that is only about tags, `CreateTagCommand` says "tag" twice; the branches page keeps its `…Tag…` names until PHASE02 removes them |
| Whether the tags page takes drops | No, and no `DragDrop.AllowDrop` anywhere on it | Nothing on that page has a meaning for a drop; an accepting list that does nothing is worse than one that refuses |
| Where the existing tag tests stay | `TagsAndCheckoutTests` is untouched | It still drives the branches page, which still works; retargeting it belongs with the removal in PHASE02 |

## Deviations & follow-ups

- **One deviation, recorded above:** PHASE01 step 2 of the plan described leaving `TagRowViewModel`
  usable by the branches page while it moved. As written that is impossible — the type named
  `BranchesPageViewModel` as its owner — so the row was changed to carry its commands instead. The
  outcome the step asked for is unchanged: the branches page still compiles and still works.
- Tags are reachable two ways until PHASE02: the new rail item, and the branches page's switch. That
  is deliberate and lasts one commit.
- **Line endings (recommendation only):** no CRLF churn in the touched files. No action taken.

## Build/test evidence

```
dotnet build Enigma.GitClient.slnx
  0 Warning(s)
  0 Error(s)

dotnet test --solution Enigma.GitClient.slnx
  Test run summary: Passed!
  total: 1798  failed: 0  succeeded: 1798  skipped: 0
```

Fifteen tests added (1783 → 1798). No fix cycle: green on the first run.

## Documentation sweep

`README.md` lists "Tag management — create (lightweight or annotated) and delete" and
`RELEASENOTES.md` says the same under *Working with the repository*; neither says where tags live in
the application, so nothing in them became wrong. No edits. There is no `CLAUDE.md`, `CHANGELOG.md`
or `CONTRIBUTING.md` in the repository.
