# FEATURE-0667 — Branch rows say where they stand

**Status:** IN PROGRESS
**Type:** FEATURE
**Branch:** one per phase, see below
**Run:** feature/2026-09-21-icons-tracking-dragging

## Objective

A local branch's row says, at a glance, how far it is from the remote — an arrow and a number of
commits ahead and behind — and whether it is on a remote at all.

## Context & constraints

- The data is already read. `RefParser.FormatTemplate` asks `for-each-ref` for `%(upstream:short)` and
  `%(upstream:track)`, and `RefParser.ParseTracking` turns `[ahead 2, behind 1]` and `[gone]` into
  `BranchTracking(Ahead, Behind, IsUpstreamGone)`. Nothing new has to be run against git.
- `BranchRowViewModel` already exposes `Ahead`, `Behind`, `IsAhead`, `IsBehind`, `IsUpstreamGone`, and
  the row template already draws two pills with the text characters `↑` and `↓` in the quiet
  foreground. What is missing is that they are text glyphs rather than the application's icons, that
  they say nothing when a branch is level with its remote, that they are drawn for remote rows too —
  where they are always empty — and above all that **nothing on the row says whether the branch exists
  on a remote**.
- `%(upstream:track)` is empty for a branch with no configured upstream. That is the common case for a
  branch pushed with plain `git push origin main`: it is on the remote, and git reports no tracking.
  So "is it on the remote" cannot be answered from the upstream alone — but `RefCollection.RemoteBranches`
  is right there, and a remote-tracking ref whose `NameWithoutRemote` equals the local branch's short
  name answers it exactly.
- The published lookup must be built from the **unfiltered** ref collection: the search box filters the
  rows, and a branch does not stop being on origin because the reader typed something in a box.
- Counts are read by people: they keep `CultureInfo.CurrentCulture`, as the existing properties do.
- Every glyph gets a tooltip, because an arrow with a number beside a branch name is only obvious once
  you already know what it means.
- **Baseline:** clean build, 1803 tests green.

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| Where ahead/behind comes from | `%(upstream:track)`, already parsed | It costs nothing: the page runs one `for-each-ref` and the answer is in it | `git rev-list --left-right --count` per branch (one git process per row, on every refresh, to re-derive what git already said) |
| What "on the remote" means | An upstream that still exists, **or** a remote-tracking ref of the same short name | It is what the reader means by the question. A branch pushed without `-u` has no upstream and is plainly on the remote; calling it "local only" would be a lie the page tells every time | Upstream-configured only (wrong for `git push origin main`); running `git ls-remote` (a network call to answer a question about refs we already have) |
| What a branch with no remote shows | A `CloudSlash` pill reading "local only" | It is the state worth naming: it is the branch that would be lost with the machine | Nothing at all (the absence of a badge is not information); a red warning (it is a normal state, not a fault) |
| What a published branch shows | A `CloudCheck` glyph with the remote branch named in its tooltip | The row already prints the upstream when there is one; a glyph answers "is it up there" without a second line of text | A "published" text pill on every row (the same word on nearly every line, for the width of a column) |
| Whether ahead/behind shows when both are zero | No pill, but the cloud glyph says it is published | Two zeroes on every row is noise; the reader's question is "is there anything to push or pull", and silence answers it | An explicit "in sync" pill on every row |
| Remote rows | No tracking and no remote-state badges | A remote-tracking branch *is* the remote; ahead/behind of what? Today those pills are drawn and always empty | Showing the badges for every row (empty ones everywhere) |
| The arrows | `ei:Icon` `ArrowUp` / `ArrowDown` at the pill size | They are the application's icon set, they follow the theme and the icon scale, and they do not depend on the text font carrying `↑` | Keeping the `↑`/`↓` characters (font-dependent, and out of step with every other glyph in the app) |
| Where the published lookup is computed | Once per rebuild in `BranchesPageViewModel.Rebuild`, passed to the row | The row is not in a position to ask about other refs, and building the set once is one pass over the remote branches | A property on the row that walks the ref collection (a scan per row, per rebuild) |

## PHASE01 — What a local branch knows of its remote

**Branch:** `feature/feature-0667-phase01-tracking`
**Status:** DONE — see `docs/done/FEATURE-0667-PHASE01.md`

### Steps

1. `ViewModels/Pages/BranchesPageViewModel.cs`: `BranchRowViewModel` gains the remote-tracking branch
   it is published as — a constructor argument defaulting to none — and the properties the row is
   drawn from: `IsLocal`, `IsPublished`, `PublishedOn` (the remote ref's short name), `ShowsTracking`
   (local, published, upstream not gone), `ShowsLocalOnly`, `ShowsUpstreamGone`, and the tooltips
   `AheadTip` / `BehindTip` / `RemoteStateTip`. `IsAhead` / `IsBehind` are gated on the row being a
   local one, so a remote row draws no tracking at all.
2. Same file: `Rebuild` builds the published lookup from the **unfiltered** `RefCollection.RemoteBranches`
   — remote short name by branch name, preferring the branch's own upstream when it names one — and
   passes each local row the remote branch it is published as.
3. Tests — `tests/.../BranchesPageTests.cs`: against the real repository with a real remote — a branch
   pushed to origin is published and names the ref it is published as; one that is not is "local only";
   a branch whose upstream was deleted says the upstream is gone rather than that it is published; a
   branch with commits its upstream does not have is ahead by that many, and behind by as many the
   other way; a remote row shows no tracking and no remote state; and the lookup ignores the search box,
   so filtering the list does not turn a published branch into a local-only one.

### Acceptance criteria

- A local branch row can say whether it is on a remote, which remote ref it is, and how many commits it
  is ahead and behind.
- The answer is right for a branch pushed without `--set-upstream`.
- Remote-tracking rows expose no tracking or remote state.
- No new git command is run: the page still reads the refs it already had.
- Build clean with zero warnings; the whole suite green.

## PHASE02 — Arrows, counts and a remote state

**Branch:** `feature/feature-0667-phase02-row`
**Status:** TODO

### Steps

1. `Views/Pages/BranchesPageView.axaml`: the tracking column of the branch row becomes — for local rows
   only — an ahead pill (`ArrowUp` icon plus the count), a behind pill (`ArrowDown` plus the count), and
   one remote-state badge: `CloudCheck` when the branch is on a remote, a `CloudSlash` "local only" pill
   when it is not, and the existing "upstream gone" pill, now with a `CloudWarning` glyph. Each carries
   `ToolTip.Tip` and the arrows carry an `AutomationProperties.Name`, so the row is readable by someone
   who does not already know what an arrow means.
2. Same file: the pills read as counters rather than labels — the arrow and its number in the same pill,
   the number at the row's small size, the whole group vertically centred as it is today.
3. Tests — `tests/.../BranchesPageTests.cs`: the realised row of a branch that is ahead shows the ahead
   pill and not the behind pill; a branch with no remote shows the local-only badge and no arrows; a
   published, level branch shows the cloud glyph and neither arrow; a remote row shows none of them; and
   every badge that is visible has a tooltip.

### Acceptance criteria

- A local branch that is ahead or behind shows an up or down arrow with the number of commits.
- Every local row says whether the branch is on a remote.
- A remote row is unchanged.
- The row still fits its columns with the longest realistic content (counts in the hundreds, a long
  upstream name).
- Build clean with zero warnings; the whole suite green.

## Out of scope

- Pushing or pulling from the row (the sync actions live on the shell's toolbar).
- Ahead/behind against anything but the branch's own upstream or same-named remote ref.
- Fetching to make the counts fresher, or any automatic fetch.
- The tags and remotes pages.
