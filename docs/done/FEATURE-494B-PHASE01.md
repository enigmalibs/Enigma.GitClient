# FEATURE-494B-PHASE01 — Branch names in full, never trimmed

**Item:** FEATURE-494B — History refs: full names, bigger badges
**Branch:** `feature/feature-494b-phase01-untrimmed-badges`
**Run:** feature/2026-09-21-refs-tags-and-dragging

## Summary

A branch or tag badge in the history's Refs column now draws its whole name.

The badge used to stop at 180 points and draw three dots — a decision the badge was in no position
to take, because the Refs column is the reader's: it has a grip of its own, and how much of a name
fits is theirs to choose. The ellipsis lived in two places that had to move together, the template's
`MaxWidth` + `TextTrimming` on the label and the matching cap inside `RefBadgeMetrics.MeasureBadge`,
which is what seeds the column's width. Both are gone, along with `RefBadge.MaximumTextWidth`, which
had no other purpose.

What bounds a badge now is the strip that holds it, which was already `ClipToBounds="True"`, and the
column's own seed cap `HistoryPageViewModel.MaximumRefColumnWidth` (280) — unchanged, so one absurd
branch name still does not take the subject's room on first paint. Where a badge is wider than the
column, its tooltip still names it in full.

## Files / modules touched

**Modified — App**

- `Themes/Controls.axaml` — the `RefBadge` template's label loses `MaxWidth` and `TextTrimming`; a
  comment on the theme records why
- `Controls/RefBadge.cs` — `MaximumTextWidth` and its `StyledProperty` removed
- `Controls/RefBadgeMetrics.cs` — `MaximumLabelWidth` removed, `MeasureBadge` no longer caps the
  label, and the class remark says what does bound a badge now

**Modified — tests**

- `tests/Enigma.GitClient.App.UnitTests/HistoryPageTests.cs` — a badge measures its whole label
  however long it is; a realised badge's label has no trimming and no finite `MaxWidth`, and the
  tooltip still carries the name; the Refs column still seeds itself and still stops at its maximum
- `tests/Enigma.GitClient.App.UnitTests/CommitGraphCellTests.cs` — `RefBadge_EllipsisesAVeryLongName`
  asserted the behaviour that was just removed; it is replaced by
  `RefBadge_GrowsWithItsNameRatherThanEllipsisingIt`

**Modified — docs**

- `docs/roadmap.md`, `docs/plan/FEATURE-494B.md`

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| The existing ellipsis test | Replaced rather than deleted | The same question — what a very long name does to a badge — now has the opposite answer, and it is worth a test either way |
| How the replacement asserts | Two badges of the same kind, one ten characters and one three hundred, laid out in a 4000 px window | It measures growth rather than an absolute width, so it survives the type-size change PHASE02 makes |
| `MeasureBadge(null)` and `MeasureBadge("")` | Still the chrome, asserted | Dropping the cap touched that expression; the empty cases are what a row with a nameless ref would hit |

## Deviations & follow-ups

- **None from the plan.** All three acceptance criteria are covered.
- `HistoryPageViewModel.MaximumRefColumnWidth` (280) is untouched by design. Now that names are not
  ellipsised, a name past that width is clipped by the strip until the reader widens the column —
  which is the behaviour asked for, and the tooltip covers the gap.
- **Line endings (recommendation only):** no CRLF churn in the touched files. No action taken.

## Build/test evidence

```
dotnet build Enigma.GitClient.slnx
  0 Warning(s)
  0 Error(s)

dotnet test --solution Enigma.GitClient.slnx
  Test run summary: Passed!
  total: 1778  failed: 0  succeeded: 1778  skipped: 0
```

Three tests added and one rewritten (1775 → 1778). No fix cycle: green on the first run.

## Documentation sweep

`README.md` mentions "ref badges" as a feature of the graph and `RELEASENOTES.md` says the branches
sit in a column of their own beside the graph — both still true. Neither describes how a long name
was drawn, so the diff made nothing in them wrong. No edits. There is no `CLAUDE.md`,
`CHANGELOG.md` or `CONTRIBUTING.md` in the repository.
