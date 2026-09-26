# FEATURE-0DB4-PHASE02 — Readable text on a selected row

**Item:** FEATURE-0DB4 — Bigger icons, readable selected rows
**Branch:** `feature/feature-0db4-phase02-selected-row-text`
**Run:** feature/2026-09-20-ui-polish-diff-page

## Summary

A list row's quiet columns — the author, the date, the short hash, the upstream, the counters, the
tip's subject — are drawn in the two dimmed foregrounds, which is right until the row is selected
and that grey lands on the selection's own colour. Now the selected row raises both roles to the
full foreground: the line being read is the line that reads.

The fix is not a new colour, it is **removing what blocked the old one**. Avalonia's value
precedence puts a local value above every style setter, so a `TextBlock` that names its own brush in
markup can never be restyled — which is why `ChangedFilesPanelView` and `IntegrationsPageView` each
carried a `ListBoxItem:selected TextBlock.dim` rule that had never once fired, on rows that also set
`Foreground` locally. Those two rules are gone, said once in `Themes/Styles.axaml`, and the locals
that outranked them with them:

| Class | Brush | Selected |
|---|---|---|
| `dim` | `EnigmaForegroundSecondaryBrush` | `EnigmaForegroundBrush` |
| `faint` | `EnigmaForegroundTertiaryBrush` | `EnigmaForegroundBrush` |

Both exist on `TextBlock` and on `ei|Icon`, and both brighten inside a selected `ListBoxItem` or
`TreeViewItem`, so the history, branches, tags, remotes, repositories, integrations, stash and
changed-files rows all behave the same way without a rule of their own.

## Files / modules touched

**Modified — App**

- `Themes/Styles.axaml` — the two dimmed roles for text and icons, the `:selected` rules that raise
  them, and the comment that says why they are classes and not local brushes
- `Views/Pages/HistoryPageView.axaml` — the author, the date and the short hash of a commit row
- `Views/Pages/BranchesPageView.axaml` — both row templates and the group heading; the seven pill
  texts take `dim`, and the view's own `Border.pill TextBlock` rule is gone with them
- `Views/Pages/RemotesPageView.axaml`, `RepositoriesPageView.axaml`, `IntegrationsPageView.axaml`,
  `ChangesPageView.axaml` (the stash rows) — the same migration; `IntegrationsPageView` also loses
  its dead `:selected` rule
- `Views/Panels/ChangedFilesPanelView.axaml` — the rename label, the line counts and the directory
  summary, plus the folder glyph; its two dead `:selected` rules are gone

**Modified — tests**

- `tests/Enigma.GitClient.App.UnitTests/ShellRenderTests.cs` — a "dimmed text on a selection"
  section: each role's brush, a selected list row reading in full while the rest of the list does
  not, deselection putting it back, and the same in a `TreeView`
- `tests/Enigma.GitClient.App.UnitTests/BranchesPageTests.cs` — the real page: selecting a branch
  row brightens every quiet column on it and only on it

**Modified — docs**

- `docs/roadmap.md`, `docs/plan/FEATURE-0DB4.md`

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| Class names | `dim` (secondary) and `faint` (tertiary) | `dim` was already the house name in two views; `faint` is one step further down and reads as such |
| The branches page's pill text | Classed `dim` in markup, the view's `Border.pill TextBlock` rule deleted | A style on a control-level `Styles` collection outranks the application's, so the house `:selected` rule could never have won against it |
| Rows that cannot be selected | Migrated anyway (group headings, the recent-repository cards) | One rule for a line's quiet text is easier to hold than a rule plus a list of exceptions; nothing about them changes |
| The commit-message counter | Migrated too | It carried the same blocked pattern — a local tertiary brush under a `Classes.overlong` style — so its warning colour had never shown either |
| Page headers, toolbars, settings | Left with their local brushes | They are never drawn on a selection, and a class there would say nothing |
| What proves it | Both a synthetic list and the real branches page | The synthetic list pins the rule; the page proves no markup still outranks it |

## Deviations & follow-ups

- **One addition beyond the plan's list:** the commit box's subject-length counter in
  `ChangesPageView`. It is not a row, but it carried the identical defect — `Classes.overlong` could
  never colour it because the element named its own brush — and it was one line inside the same
  migration. Its "past the guide" warning colour now actually appears.
- The class migration deliberately stops at row templates. A later sweep could give the page headers
  the same roles, but nothing there is wrong today.
- **Line endings (recommendation only):** no CRLF churn in the touched files. No action taken.

## Build/test evidence

```
dotnet build Enigma.GitClient.slnx
  0 Warning(s)
  0 Error(s)

dotnet test --solution Enigma.GitClient.slnx
  Test run summary: Passed!
  total: 1746  failed: 0  succeeded: 1746  skipped: 0
```

Six tests added (1740 → 1746). One fix cycle, in the tests only: `FuncTreeDataTemplate`'s child
selector must return a sequence rather than `null` under the solution's nullable settings.

## Documentation sweep

Scanned `README.md` and `RELEASENOTES.md`: neither describes the colour of a selected row, so
nothing the diff touched made them wrong. No edits. There is no `CLAUDE.md`, `CHANGELOG.md` or
`CONTRIBUTING.md` in the repository.
