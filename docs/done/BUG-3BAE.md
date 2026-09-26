# BUG-3BAE — Search matches are not highlighted

**Item:** BUG-3BAE — Search matches are not highlighted
**Branch:** `bugfix/bug-3bae-search-highlight`
**Run:** feature/2026-09-20-ui-polish-diff-page

## Summary

The history search now shows what it found: every matching row is washed with the search colour,
and the count beside the box agrees with what is on screen.

Nothing about the search itself was wrong. `MarkMatches` had always been marking the rows, the row
template had always been binding `Classes.match`, and `Themes/Styles.axaml` had always been giving
`Grid.match` the `SearchMatchBrush`. The wash was invisible for one reason: the row's `Grid` also
set `Background="Transparent"` **as a local value in markup**, and in Avalonia a local value
outranks every style setter. The class was applied, the style matched, and the background never
moved — since the day FEATURE-3030 PHASE05 wrote it.

The transparent background could not simply be deleted: it is the row's hit area, and the row's menu
opens from anywhere on the line (FEATURE-3030 PHASE01). So it moved to where it can be beaten —
`Grid.commitrow` says transparent, `Grid.commitrow.match` says found, both at style precedence, and
the more specific one wins.

## Files / modules touched

**Modified — App**

- `Views/Pages/HistoryPageView.axaml` — the row `Grid` carries `commitrow` and no longer names its
  own background; the comment now says why the hit area is a style rather than a local value
- `Themes/Styles.axaml` — `Grid.match` becomes `Grid.commitrow` plus `Grid.commitrow.match`

**Modified — tests**

- `tests/Enigma.GitClient.App.UnitTests/HistoryPageTests.cs` — `Search_WashesTheRowsItFound`: with
  no search every row is painted transparent, a search paints exactly the matching rows in the
  search colour, every row agrees with its own `IsSearchMatch`, and clearing the search puts them
  all back

**Modified — docs**

- `docs/roadmap.md`, `docs/plan/BUG-3BAE.md`

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| How the wash is made to win | The hit area became a style too | Both values then sit at style precedence, where the more specific class wins — which is what the markup already read as |
| The class name | `commitrow` | It names the row, the way `branchrow` already names the branches list's |
| What the test asserts | The painted colour, not the class | The class was already being set correctly and the bug shipped anyway; only the brush proves the reader can see it |
| The transparent assertion | Kept explicit | It is the hit area the row's menu depends on, and a future refactor that drops it would otherwise pass |

## Deviations & follow-ups

- **None from the plan.** All five acceptance criteria are covered.
- The same precedence trap was fixed in two other places by `FEATURE-0DB4-PHASE02` earlier in this
  run. Worth remembering as a house rule: **a value written in markup cannot be restyled** — if a
  class is ever to change it, it belongs in `Themes/Styles.axaml`.
- **Line endings (recommendation only):** no CRLF churn in the touched files. No action taken.

## Build/test evidence

```
dotnet build Enigma.GitClient.slnx
  0 Warning(s)
  0 Error(s)

dotnet test --solution Enigma.GitClient.slnx
  Test run summary: Passed!
  total: 1747  failed: 0  succeeded: 1747  skipped: 0
```

One test added (1746 → 1747). No fix cycle: green on the first run.

## Documentation sweep

`README.md` ("a search that highlights what it found instead of hiding everything else") and
`RELEASENOTES.md` ("marks the commits it finds and hides nothing") both already describe the
behaviour this dev delivers — the diff made them true rather than making them wrong, so neither
needed an edit. There is no `CLAUDE.md`, `CHANGELOG.md` or `CONTRIBUTING.md` in the repository.
