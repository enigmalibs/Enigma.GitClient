# BUG-36A9 — A found row cannot be hovered

**Item:** BUG-36A9 — A found row cannot be hovered
**Branch:** `bugfix/bug-36a9-match-hover-and-selection`
**Run:** feature/2026-09-21-refs-tags-and-dragging

## Summary

A history row the search found now shows that it is hovered, and shows that it is selected.

The search wash is painted by the **row's own `Grid`**, not by its `ListBoxItem`, and it is opaque.
The container's `:pointerover` and `:selected` plates are drawn behind that Grid, so on a found row
they were covered completely: the pointer changed nothing, and selecting the row changed nothing
either. Every row that was *not* found is transparent, so those rows were unaffected — which is why
the defect looked like the highlight rather than the list.

The row now answers for all three states itself. `Grid.commitrow.match` keeps the wash;
`ListBoxItem:pointerover Grid.commitrow.match` and `ListBoxItem:selected Grid.commitrow.match` carry
the same hue further, so a found row stays recognisably found while the pointer is on it and while
it is the row being acted on. The three colours are theme resources, tuned separately for Dark and
for Light, beside `SearchMatchColor` where the original wash already lived.

## Files / modules touched

**Modified — App**

- `Themes/Graph.axaml` — `SearchMatchHoverColor` and `SearchMatchSelectedColor` for the Dark variant
  (`#4C4520`, `#635A29`) and for the Light one (`#F3E3A0`, `#E8D178`), and the two brushes over them
- `Themes/Styles.axaml` — the two new selectors after the wash, with the reason they sit on the row
  and not on the container

**Modified — tests**

- `tests/Enigma.GitClient.App.UnitTests/HistoryPageTests.cs` — a found row is washed, hovered,
  selected, and selected-while-hovered, each a different colour; a row the search did not find stays
  transparent in every one of those states so its container keeps painting them; clearing the search
  puts every row back; and both theme variants carry all three brushes

**Modified — docs**

- `docs/roadmap.md`, `docs/plan/BUG-36A9.md`

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| How the test hovers a row | `((IPseudoClasses)container.Classes).Set(":pointerover", true)` | It is the state the style selects on, and the mechanism the branches page already uses to clear a hover the platform never delivered an exit for; a synthetic pointer would be testing the input system |
| A row both found and hovered *and* selected | Selected wins | Declared last, so the setter taken last is the selection's — and the row the reader is acting on is the more important of the two facts |
| The colours | The match hue carried further rather than the theme's own hover and selection plates | The plates would make a found row stop looking found for as long as the pointer is on it, which is the same class of defect one state along |
| Where the light values came from | The light wash `#FBF0C2` darkened in two steps | Light needs to go down where dark goes up; a single computed blend could not do both |

## Deviations & follow-ups

- **None from the plan.** All five acceptance criteria are covered.
- The same pattern would be needed by any other list that paints a row-level wash under a
  `ListBoxItem`. Today only the history does.
- **Line endings (recommendation only):** no CRLF churn in the touched files. No action taken.

## Build/test evidence

```
dotnet build Enigma.GitClient.slnx
  0 Warning(s)
  0 Error(s)

dotnet test --solution Enigma.GitClient.slnx
  Test run summary: Passed!
  total: 1783  failed: 0  succeeded: 1783  skipped: 0
```

Two tests added (1781 → 1783). No fix cycle: green on the first run. Both would have failed before
the change — the first on the hovered colour, the second on the missing resources.

## Documentation sweep

`RELEASENOTES.md` says the search box "marks the commits it finds and hides nothing", which is still
exactly what it does; neither it nor `README.md` describes what a marked row does under the pointer.
The diff made nothing in them wrong, so no edits. There is no `CLAUDE.md`, `CHANGELOG.md` or
`CONTRIBUTING.md` in the repository.
