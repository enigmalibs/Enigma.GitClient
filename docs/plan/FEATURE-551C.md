# FEATURE-551C — Branches: grouping, reset, double-click

**Status:** TODO
**Type:** FEATURE
**Branch:** one per phase, see below
**Run:** feature/2026-10-02-tags-branches-release

## Objective

Three GitKraken behaviours for the history's branch badges:

1. A local branch and its remote on the **same** commit are **one** badge, with both icons, and one
   menu holding what both offer.
2. Checking out a remote branch whose local branch is on **another** commit offers to **reset the
   local branch to here**, in a content dialog.
3. A **double-click** on a branch badge checks the branch out.

## Context & constraints

- **The badges.** `ViewModels/Pages/CommitRowViewModel.cs`:
  - `Project` turns the row's `GitRef`s into `RefBadgeItem(Kind, Name, IsCurrent)`;
  - `BuildBranches` wraps every branch badge in a `HistoryBranchViewModel` (its menu's state and the
    shared `HistoryBranchCommands`), every tag in a `HistoryTagViewModel`. `Branches` (every branch on
    the line, used by the line's own menu) and `Badges` (what the strip draws) come out of it.
  - A row's refs all point at its commit, so "same commit" is "same row".
- **The badge.** `Controls/RefBadge.cs` (a `TemplatedControl`, `Kind`/`Text`/`IsCurrent`, an icon
  derived from the kind: `GitBranch` for a local, `CloudArrowDown` for a remote) and its template in
  `Themes/Controls.axaml`. `Controls/RefBadgeMetrics.cs` mirrors the template's numbers to measure the
  Refs column; a test reads them back off a realised badge.
- **The upstream.** `GitBranch.UpstreamShortName` (`origin/main`) and `Tracking.IsUpstreamGone`.
- **The badge menu.** `HistoryPageView.axaml`, `DataTemplate DataType="vm:HistoryBranchViewModel"`:
  *Check out* · merge source items · merge items · *Pull*/*Push* (local only) · *Delete…* · *Copy
  branch name*.
- **Checking out a remote.** `Services/BranchOperations.CheckoutAsync(name, isRemote: true)` calls
  `IBranchService.CheckoutRemoteAsync`, which runs `git checkout -b <local> --track <remote>` and
  refuses with "A branch called "x" already exists…" when the local exists. The history's badge and
  the Branches dialog both come through here.
- **The pointer.** `Views/Pages/HistoryPageView.axaml.cs` handles presses on the page in a tunnelling
  `OnPointerPressed` (badge drags, line toggles) and leaves a second press (`ClickCount != 1`) alone.
- Tests: `HistoryDragMergeTests` (headless window, real git, badges found by name), `HistoryPageTests`,
  `BranchesPageTests`; Core `BranchServiceTests` against a real repository and a bare remote.

## PHASE01 — One badge for a branch and its remote

**Branch:** `feature/feature-551c-phase01-grouped-badge`
**Status:** TODO

### Steps

1. `RefBadgeItem` gains `string? Upstream` (the remote-tracking branch drawn with the local), with
   `HasUpstream`.
2. `CommitRowViewModel.Project`: a local branch whose upstream (not gone) is a remote branch **on the
   same row** becomes one item carrying `Upstream`; that remote's own item is not emitted.
3. `BuildBranches`: the grouped item's `HistoryBranchViewModel` gets `Remote`, a
   `HistoryBranchViewModel` for the remote branch. `Branches` keeps both (the line's menu still names
   each); `Badges` draws the local one only.
4. `HistoryBranchViewModel`: `Remote`, `HasRemote`, `DeleteRemoteHeader` (`Delete "origin/main"…`),
   and `Description` (`main` / `main and origin/main`) for the tooltip and the automation name.
5. `RefBadge`: an `Upstream` property; the template draws a second icon (`CloudArrowDown`) after the
   branch icon when it is set; the tooltip names both. `RefBadgeMetrics.Measure` counts that icon.
6. The badge menu: the local's menu, plus *Delete "origin/main"…* (the remote, through the same
   `Delete` command) beside *Delete "main"…* when grouped. *Check out* acts on the local.
7. Tests: grouped when tracking and level; separate when the remote is elsewhere, when there is no
   upstream, or when the upstream is another remote branch; the current branch keeps its head look;
   the menu holds both deletes; the measured strip matches the realised grouped badge.

### Acceptance criteria

- A local branch and its upstream on the same commit draw one badge with both icons and the local's
  name, and its menu offers the remote's delete too.
- Every other case draws as before.
- The Refs column fits the grouped badge.

## PHASE02 — Reset local to the remote's commit

**Branch:** `feature/feature-551c-phase02-reset-local-to-here`
**Status:** TODO

### Steps

1. Core: `IBranchService.ResetAndCheckoutAsync(repository, name, startPoint)` —
   `git checkout --track -B <name> <startPoint>`: moves the branch to the start point and checks it
   out in one step, carrying uncommitted changes as any checkout does, and failing (git's message)
   rather than overwriting one. The name is validated; a start point starting with a dash is refused.
2. `BranchOperations.CheckoutAsync(remote)`, when the local branch of the same name (`LocalNameFor`)
   exists:
   - on the remote's commit: check the local out, or say it is already checked out;
   - on another commit: a content dialog, *Reset "main" to "origin/main"?*, naming both commits and
     listing the commits only the local has (they would be left behind):
     - **Reset local to here** (primary) → `ResetAndCheckoutAsync`;
     - **Check out "main"** (secondary, not offered when `main` is already checked out) → the local,
       where it is;
     - **Cancel**.
     The default is *Reset local to here* when nothing is left behind, *Cancel* otherwise.
   - With no local branch: unchanged (creates the tracking branch).
3. Tests: Core — the reset moves and checks out, keeps an uncommitted change, refuses an overwriting
   one, sets the upstream. Desktop — each of the three answers, the default button in both cases, the
   listed commits, the level case without a question, and the Branches dialog's remote checkout.

### Acceptance criteria

- Checking out a remote branch whose local is elsewhere asks, and each answer does what it says.
- Nothing uncommitted is ever thrown away.
- Level branches and the no-local case behave as described.

## PHASE03 — Double-click a badge to check out

**Branch:** `feature/feature-551c-phase03-double-click-checkout`
**Status:** TODO

### Steps

1. `HistoryPageView.axaml.cs`: a left double-click (the second press, `ClickCount == 2`) on a branch
   badge runs that badge's `Commands.Checkout` when it can execute — posted, so the list finishes the
   gesture first. A local, a remote (through PHASE02's question) and a grouped badge (its local) alike;
   the checked-out branch does nothing.
2. A double-click elsewhere on the line and a single click are unchanged; no drag starts.
3. Tests (headless, real input): a double-click checks a local branch out; on a remote whose local is
   elsewhere it asks; on the current branch nothing happens; a single click checks nothing out.

### Acceptance criteria

- A double-click on a branch badge checks that branch out exactly as its menu's *Check out* does.
- Nothing else about clicks, selection and drags changes.

## Out of scope

- Grouping in the Branches dialog (it lists locals and remotes in sections of their own).
- Double-click on tag badges or on the line.
- A reset for a local branch whose name differs from the remote's.

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| Which remote joins a local | Its configured upstream, when both are on the same commit | GitKraken's rule; it is the pairing git itself records, so a stray `upstream/main` never joins | Any remote branch of the same name; every remote on the commit |
| The grouped badge's look | The local's pill (the head colour when checked out), branch icon then cloud icon, the local's name; the tooltip names both | The local is what a reader acts on; the second icon says the remote is level | Showing `main · origin`; the remote's colour |
| The grouped badge's menu | The local's menu, plus *Delete "origin/main"…* | Every other remote action is the same as the local's on the same commit; deleting the remote is the one that differs | Two submenus; every item of both |
| The line's own menu | Unchanged: it still names every branch on the line | It is the line's, not the badge's, and offers merge sources by name | Dropping the grouped remote |
| When "reset local to here" is offered | A remote checkout whose same-named local exists on another commit | That is the case that used to be refused, and the one GitKraken asks about | Always asking; only when strictly behind |
| How the reset runs | `git checkout --track -B <local> <remote>` | One step that never throws uncommitted work away: git carries it or refuses | `checkout` then `reset --hard` (loses work); `reset --keep` (two steps, two failure points) |
| The dialog's buttons and default | *Reset local to here* / *Check out "main"* / *Cancel*; Reset by default only when nothing is left behind | The left-behind commits are the one risk, and they are listed | Cancel always the default; no "as it is" choice |
| Level local and remote | Check the local out without asking | Nothing to reset | Asking anyway |
| Double-click target | Branch badges only, through the badge's own *Check out* | As drafted; one path means one set of questions | Tags too; the whole line |
| Breakdown | Three phases | Three independent behaviours, each one reviewable commit | One phase |
