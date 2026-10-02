# FEATURE-551C-PHASE01 — One badge for a branch and its remote

**Item:** FEATURE-551C — Branches: grouping, reset, double-click
**Phase:** PHASE01 — One badge for a branch and its remote
**Branch:** `feature/feature-551c-phase01-grouped-badge`
**Run:** feature/2026-10-02-tags-branches-release

## Summary

In the history, a local branch and its configured upstream on the same commit are now **one badge**,
as GitKraken draws them:

- **What it shows:** the local branch's pill (the head colour when it is checked out), with its branch
  icon followed by the remote's cloud icon, and the local branch's name. Its tooltip and accessible
  name say both: *main and origin/main*.
- **Its menu:** the local branch's menu, with *Delete "origin/main"…* (cloud-x icon) right after
  *Delete "main"…*. Checking out, merging, pulling and pushing act on the local branch, which on the
  same commit is what the remote would do too.
- **Which remote joins:** only the configured upstream. A remote branch nothing tracks, another
  remote's branch of the same name, and an upstream on another commit all keep a badge of their own.
  A remote branch that two local branches track joins the first of them only.
- **Unchanged:** the line's own menu still names both branches (*Set "origin/main" as merge source*),
  and the grouped badge is ringed when either of its two branches is the merge source. Dragging the
  badge drags the local branch, and dropping onto it targets the local branch.
- **Column width:** the Refs column is measured with the second icon (`RefBadgeMetrics`), so it fits
  the grouped badge.

## Files / modules touched

**Modified**

- `src/Enigma.GitClient.Desktop/ViewModels/Pages/CommitRowViewModel.cs`:
  - `RefBadgeItem.Upstream`/`HasUpstream`;
  - `Project` pairs a local branch with its upstream on the line (`JoinUpstreams`);
  - `BuildBranches` gives the grouped badge its `Remote` and keeps both in `Branches`.
- `src/Enigma.GitClient.Desktop/ViewModels/Pages/HistoryBranchViewModel.cs`: `Remote`, `HasRemote`,
  `DeleteRemoteHeader`, `IsDrawnAsMergeSource`.
- `src/Enigma.GitClient.Desktop/Controls/RefBadge.cs`: the `Upstream` property and the derived
  `HasUpstream`/`Description` properties.
- `src/Enigma.GitClient.Desktop/Themes/Controls.axaml`: the second icon; the tooltip and the
  automation name are the description.
- `src/Enigma.GitClient.Desktop/Controls/RefBadgeMetrics.cs`: `MeasureBadge(label, withUpstream)`;
  `Measure` counts the second icon.
- `src/Enigma.GitClient.Desktop/Views/Pages/HistoryPageView.axaml`: the branch badge binds `Upstream`
  and is ringed from `IsDrawnAsMergeSource`; its menu adds the remote's delete.
- `tests/Enigma.GitClient.Desktop.UnitTests/AutoRefreshTests.cs`: three assertions that expected
  `origin/main`'s own badge on the pushed line now expect the grouped badge.
- `docs/roadmap.md`, `docs/plan/FEATURE-551C.md`: statuses.

**Created**

- `tests/Enigma.GitClient.Desktop.UnitTests/HistoryGroupedBadgeTests.cs` (9 tests)
- `docs/done/FEATURE-551C-PHASE01.md`

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| Where the pairing happens | `CommitRowViewModel.Project`, over the line's own refs | Every ref on a line points at its commit, so "same commit" is "same line". `Refs` then describes what is drawn, which is what the column measurement reads |
| A remote two locals track | Joins the first local branch only | A remote branch is drawn once, never twice |
| The tooltip's wording | *main and origin/main* | Names both, and reads as a sentence to a screen reader |
| The merge-source ring | Ringed when either branch is the source | The line's menu can still make the remote the source, and the ring must not vanish when it does |
| The remote's delete | The same `Commands.Delete`, with the remote as its parameter | One delete path, so the remote branch's usual question ("This changes the remote for everyone…") is asked |

## Deviations & follow-ups

- None from the plan.
- The roadmap diff re-pads the whole table: FEATURE-551C stays `IN PROGRESS` across its phases, and
  that keyword is wider than the Status column's other values. The table narrows back when the item is
  `DONE`.
- Line endings: no CRLF churn; the touched files are LF.

## Documentation sweep

Nothing became wrong. README's feature list says "ref badges" and "a branch from its badge's menu",
and both are still true. The grouped badge is described in FEATURE-4D5A's release notes. No
`CLAUDE.md` or `AGENTS.md` exists.

## Build/test evidence

- `dotnet build Enigma.GitClient.slnx`: 0 warnings, 0 errors.
- `dotnet test --solution Enigma.GitClient.slnx`: **2664 passed**, 0 failed, 0 skipped (9 new).
- New tests:
  - which badges join: level and tracking; an upstream elsewhere; an untracked remote; another
    remote's branch of the same name;
  - the merge-source ring;
  - the realised badge: both icons, the head look, and the tooltip and automation name;
  - the metrics agree with the template's extra icon width;
  - the merged menu (both deletes side by side, wired to the right branch);
  - the remote's delete removes the remote branch and keeps the local one.
- Fix budget: 1 cycle used. `AutoRefreshTests` expected `origin/main`'s own badge after a push; it is
  grouped with `main` now, and the assertions were updated to that.
