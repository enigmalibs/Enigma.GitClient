# FEATURE-0667-PHASE02 — Arrows, counts and a remote state

**Item:** FEATURE-0667 — Branch rows say where they stand
**Branch:** `feature/feature-0667-phase02-row`
**Run:** feature/2026-09-21-icons-tracking-dragging

## Summary

The branch row now draws what PHASE01 taught it: for a local branch, an up arrow with the number of
commits to push, a down arrow with the number to pull, and one badge for where the branch lives —
`CloudCheck` when it is on a remote, a "local only" pill when it is on none, and the "upstream gone"
pill, now with a `CloudWarning` glyph, when it names an upstream that has been deleted.

Three things changed about the two counters that were already there. They are drawn with the
application's icons instead of the `↑` and `↓` characters, so they follow the theme and the icon scale
rather than whatever the text font happens to carry. They are gated on the row being a local branch, so
the remote-tracking rows stop carrying two counters that were always empty. And each badge names itself
in a tooltip and in its automation name — an arrow and a number beside a branch name means nothing
until someone tells you what it counts.

A counter appears only when it has something to count; the cloud is the badge that is on every local
row. So a branch level with its remote reads as the quiet row it is, and the eye goes to the ones with
work on them.

## Files / modules touched

**Modified — App**

- `Views/Pages/BranchesPageView.axaml` — the branch row's tracking column: the ahead and behind pills
  with `ArrowUp` / `ArrowDown`, the `CloudSlash` "local only" and `CloudWarning` "upstream gone" pills,
  and the `CloudCheck` glyph, each bound to the row's own visibility flag, tooltip and automation name

**Modified — tests**

- `tests/Enigma.GitClient.App.UnitTests/BranchesPageTests.cs` — what each realised row actually draws,
  by icon kind, for a branch drifted both ways, one level with its remote, one on no remote, one whose
  upstream is gone and a remote-tracking row; and that the count is drawn beside the arrow and every
  visible badge carries a tooltip and a name. A `Row(ListBox, string)` helper beside the existing
  `Row(page, name)`

**Modified — docs**

- `README.md`, `RELEASENOTES.md` (documentation sweep, below)
- `docs/roadmap.md`, `docs/plan/FEATURE-0667.md`

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| Where the tooltip text lives | On the row, as `AheadTip` / `BehindTip` / `RemoteStateTip` (PHASE01) | A sentence with a number in it is not markup. It also lets the automation name and the tooltip be the same string without stating it twice |
| The published badge is a bare glyph, the other two are pills | Yes | "On a remote" is the ordinary case and appears on nearly every row; a word repeated down the whole column is width spent on saying nothing. The exceptions are the ones that get a word |
| Icon sizes | `pill` (12 px) inside a pill, `row` (16 px) for the standalone cloud | It is the scale the application already states; a badge glyph and a row glyph are different roles |
| How the tests identify a badge | By `Icon.Kind`, filtered on `IsEffectivelyVisible` | A hidden badge is still in the visual tree, so "what the row draws" has to mean the visible ones — which is also what makes the negative assertions meaningful |

## Deviations & follow-ups

- **None from the plan.** All five acceptance criteria are met.
- The counts are as fresh as the last fetch, which is what every git client shows and what
  `%(upstream:track)` can answer without the network. An automatic fetch would be a different feature,
  with a different set of questions.
- **Line endings (recommendation only):** no CRLF churn in the touched files. No action taken.

## Build/test evidence

```
dotnet build Enigma.GitClient.slnx
  0 Warning(s)
  0 Error(s)

dotnet test --solution Enigma.GitClient.slnx
  Test run summary: Passed!
  total: 1816  failed: 0  succeeded: 1816  skipped: 0
```

Two tests added (1814 → 1816). No fix cycle: green on the first run. Both are new-behaviour tests —
the icon kinds they look for did not exist in the template before this dev.

## Documentation sweep

The branches page gained behaviour a reader can see, and both documents describe that page:

- `README.md` — a line under the feature list: a local branch says what there is to push and to pull,
  and whether it is on a remote.
- `RELEASENOTES.md` — the same, in the "Working with the repository" section, naming the three states
  of the remote badge.

There is no `CLAUDE.md`, `CHANGELOG.md` or `CONTRIBUTING.md` in the repository.
