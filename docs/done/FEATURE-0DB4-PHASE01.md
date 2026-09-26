# FEATURE-0DB4-PHASE01 — One icon scale for the whole app

**Item:** FEATURE-0DB4 — Bigger icons, readable selected rows
**Branch:** `feature/feature-0db4-phase01-icon-scale`
**Run:** feature/2026-09-20-ui-polish-diff-page

## Summary

Icons are bigger where the request said they were too small — the toolbars, and the lines of the
branches, remotes and other lists — and they got bigger by acquiring a **scale** rather than by a
hundred numbers being nudged.

Four roles now exist as classes on `ei|Icon` in `Themes/Styles.axaml`, and an icon says which one it
plays instead of how many pixels it wants:

| Class | Size | Where |
|---|---|---|
| `header` | 20 | the icon beside a page's title, and beside the open repository's name |
| `toolbar` | 18 | an icon-only button or a segmented toggle on a toolbar strip |
| `row` | 16 | an icon on a line: a row's leading glyph, its own action buttons, a banner |
| `pill` | 12 | an icon inside a badge, a counter or a group heading |

That is the whole point of the change: "a bit bigger" was one edit in one file, and the next view to
be written picks a role rather than inventing a number. The step is about a quarter — toolbar
buttons 14/15/16 → 18, row glyphs and row actions 13/14 → 16, page titles 18 → 20 — which changes a
hit target without changing a row's height.

`Button.toolbar` and `ToggleButton.segment` were re-padded (8,5 → 7,6 and 7,4 → 6,5) so the hovered
plate stays a square around the larger glyph instead of stretching into a bar.

## Files / modules touched

**Modified — App**

- `Themes/Styles.axaml` — the `ei` namespace, the four size classes with the comment that states
  what each role is, and the two padding changes
- `Views/MainWindow.axaml` (8 icons), `Views/Pages/BranchesPageView.axaml` (13),
  `ChangesPageView.axaml` (9), `RemotesPageView.axaml` (7), `RepositoriesPageView.axaml` (6),
  `IntegrationsPageView.axaml` (6), `HistoryPageView.axaml` (4),
  `ConflictResolutionPageView.axaml` (2), `SettingsPageView.axaml` (1),
  `Views/Panels/ChangedFilesPanelView.axaml` (4), `Views/Panels/DiffViewerView.axaml` (7) — each
  icon's `Size` replaced by its role

**Modified — tests**

- `tests/Enigma.GitClient.App.UnitTests/ShellRenderTests.cs` — an "icon scale" section: the size of
  each role, the order of the four roles, every role-carrying icon on all six pages drawn at its
  role's size, and the branches toolbar's four icons at the toolbar size

**Modified — docs**

- `docs/roadmap.md`, `docs/plan/FEATURE-0DB4.md`

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| Role names | `header`, `toolbar`, `row`, `pill` | They name where the icon is, which is what the author of a view knows; `row` is also the user's own word ("the icons on the lines") |
| The repository strip's folder | `header` (16 → 20) | It labels the open repository the way a page title labels a page |
| A banner's warning glyph | `row` (unchanged at 16) | It sits on a line of text, not on a toolbar |
| A group heading's glyph | `pill` (13 → 12) | It is a marker beside an 11–12 px label, not a row's own icon |
| The recent-repository cards | Left at 20, no class | They are cards, not lines; giving them `row` would have made them smaller, which nobody asked for |
| The dialogs | Left alone | Their warning glyphs and browse buttons are neither toolbars nor rows, and the request was about both |
| Control templates | Left alone | `RefBadge`'s 11 px glyph and `EmptyState`'s 44 px one belong to those controls' own themes |
| How the scale is tested | Standalone icons for the sizes, realised pages for the wiring | The sizes are the contract; the pages prove no local `Size` in markup outranks the style |

## Deviations & follow-ups

- **None from the plan.** All four acceptance criteria are covered.
- The `pill` class name is shared with `Border.pill`; the selectors are `ei|Icon.pill` and
  `Border.pill`, so they never meet. Deliberate: a badge's icon and a badge are the same role.
- Follow-up, not taken: `ChangesPageView` and `ConflictResolutionPageView` still hold a few icons
  inside section strips that were classed `toolbar` although their buttons carry text. They look
  right at 18; if a later view wants a text-button role, that is a fifth class rather than a change
  to these.
- **Line endings (recommendation only):** no CRLF churn in the touched files. No action taken.

## Build/test evidence

```
dotnet build Enigma.GitClient.slnx
  0 Warning(s)
  0 Error(s)

dotnet test --solution Enigma.GitClient.slnx
  Test run summary: Passed!
  total: 1740  failed: 0  succeeded: 1740  skipped: 0
```

Twelve tests added (1728 → 1740). No fix cycle: the build and the whole suite were green on the
first run.

## Documentation sweep

Scanned `README.md` and `RELEASENOTES.md`: neither states an icon size or a toolbar metric, so
nothing the diff touched made them wrong. No edits. There is no `CLAUDE.md`, `CHANGELOG.md` or
`CONTRIBUTING.md` in the repository.
