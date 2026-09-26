# FEATURE-14E8 — History: diffs, badges and dragging

**Status:** DONE
**Type:** FEATURE
**Branch:** one per phase, see below
**Run:** feature/2026-09-18-history-and-diffs

## Objective

Make the history view behave the way a graph is expected to: selecting a line only selects it,
double-clicking or the row menu opens what it changed, checking out is the menu's job alone, the ref
badges stand in a column of their own beside the graph, and dragging one branch onto another offers
the operations that join them.

## Context & constraints

- `FEATURE-2288` moved the diffs into a `ContentDialog` and wired it to the selection:
  `HistoryPageViewModel.SelectedRow`'s setter sets `IsDiffDialogOpen = value is not null`. That is
  what this item takes back — the request is explicit that it was asked for and then thought better
  of.
- Double-click is `HistoryRowCommands.Activate`, today an `AsyncRelayCommand` that checks out the
  row's branch, or the commit itself when no branch points there. The uncommitted pseudo-row uses
  the same command to raise `WorkingDirectoryRequested`, which is the one activation that must
  survive: there is nothing to check out on that row, and going to the Changes page is what
  activating it means.
- The row menu already carries `Check out this commit (detaches HEAD)` and `Check out "<branch>"`,
  so removing checkout from the double-click removes no capability.
- The badges are `ItemsControl` in column 1 of the row's `Grid`, and that column is `Auto`. Every
  row's `Grid` measures on its own, so an `Auto` column is a *per-row* width: the subject, the
  author, the date and the sha therefore start at a different x on every line that carries a badge.
  The graph column already solved this — `HistoryPageViewModel.GraphColumnWidth` is one width for
  the whole page, handed to each row's `CommitGraphCell` as an explicit `Width` — and the same shape
  is what the badge column needs.
- Merging is `IMergeOperations.MergeAsync(source, FastForwardMode)`, which always merges **into the
  current branch**; `FastForwardMode` already offers `WhenPossible`, `Never` and `Only`. Moving onto
  a branch is `IBranchOperations.CheckoutAsync(name, isRemote)`, which handles the remote-tracking
  case and the dirty-tree questions. A drop is therefore a composition of the two, not a new git
  verb.
- **No rebase.** The README lists it as a permanent, structural non-goal and `ForbiddenGitOperations`
  refuses the verb, so a drop offers merge and fast-forward and nothing else.
- The app's operations services (`BranchOperations`, `MergeOperations`, `CheckoutOperations`,
  `TagOperations`) each own the whole user-facing flow — dialog, git under the repository lock, info
  bar. A drop is a flow of that kind and belongs beside them rather than inside the page.
- A `ContextMenu` opens in its own popup tree, which is why rows carry `HistoryRowCommands` rather
  than reaching the page through a visual ancestor. Anything new a row's menu or gesture needs
  follows the same route.

## PHASE01 — Open the diffs on demand

**Branch:** `feature/feature-14e8-phase01-diffs-on-demand`
**Status:** DONE — see `docs/done/FEATURE-14E8-PHASE01.md`

### Steps

1. `ViewModels/Pages/HistoryPageViewModel.cs`: `SelectedRow`'s setter stops opening the dialog. It
   still closes it when the selection is cleared — a reload after a checkout drops the selection,
   and a dialog left over a commit that is no longer selected is a dialog describing nothing.
2. Same file: `OnActivateAsync` becomes `OnActivate`, a synchronous handler that shows what the row
   changed (`OnShowChanges`) and, for the uncommitted row, still raises `WorkingDirectoryRequested`.
   `HistoryRowCommands.Activate` becomes `RelayCommand<CommitRowViewModel>` — nothing it does is
   asynchronous any more, and an `AsyncRelayCommand` that never awaits misleads its caller.
3. `ViewModels/Pages/CommitRowViewModel.cs`: the `Activate` and `ShowChanges` parameter docs say
   what the two now mean — double-click shows the changes, the menu entry is the way back to them
   once the dialog has been dismissed with the selection unchanged.
4. `Views/Pages/HistoryPageView.axaml.cs`: `OnCommitDoubleTapped` keeps running `Activate`; its
   remark stops saying "checks out".
5. `Views/Pages/HistoryPageView.axaml`: the menu's first entry becomes `Show what it changed…` and
   the checkout entries stay where they are.
6. Tests — `tests/.../HistoryPageTests.cs`: selecting a row leaves the dialog closed; activating one
   opens it; `ShowChanges` on an unselected row still selects it and opens it; the existing
   "closing keeps the selection" and "clearing the selection closes it" tests stay green.
7. Tests — `tests/.../TagsAndCheckoutTests.cs`: the two tests that drove a checkout through
   `Activate` drive it through `CheckoutBranch` / `CheckoutCommit`, which is now the only way a user
   has; one new test asserts that activating a row with a branch on it checks nothing out.
8. Tests — `tests/.../ChangesPageTests.cs`: the uncommitted row's activation is executed
   synchronously and still asks for the working directory.

### Acceptance criteria

- Setting `SelectedRow` to a row leaves `IsDiffDialogOpen` false and the `ContentDialog` closed.
- Setting `SelectedRow` to `null` closes an open dialog.
- `Activate` on a commit row opens the dialog on that row and moves the selection there; on the
  uncommitted row it raises `WorkingDirectoryRequested` and opens nothing.
- `Activate` never moves `HEAD`: a repository whose current branch is `main` is still on `main`
  afterwards.
- The row menu still checks out a branch and a commit, and still offers "show what it changed".
- Build clean with zero warnings; the whole suite green.

## PHASE02 — Branch badges in their own column

**Branch:** `feature/feature-14e8-phase02-badge-column`
**Status:** DONE — see `docs/done/FEATURE-14E8-PHASE02.md`

### Steps

1. New `Controls/RefBadgeMetrics.cs`: measures how wide a row's badge strip is — the badge text in
   the badge's own face and size, plus its icon, spacing and padding, summed over the row's badges
   with the strip's spacing between them. Measured through `FormattedText` like `DiffTypography`,
   with the same `try`/`catch` fallback to an estimate so a ViewModel built before there is a font
   manager cannot throw.
2. `ViewModels/Pages/HistoryPageViewModel.cs`: a `RefColumnWidth` property computed over the loaded
   rows — 0 when no row carries a badge, clamped to `MaximumRefColumnWidth` so one very long branch
   name cannot take the subject's room. Recomputed wherever `GraphColumnWidth` is (`AppendPage`,
   and cleared with the rows on reload).
3. `Views/Pages/HistoryPageView.axaml`: column 1 keeps `Width="Auto"` in the `ColumnDefinitions` and
   the badge `ItemsControl` takes an explicit `Width` bound to `RefColumnWidth`, exactly as
   `CommitGraphCell` takes `GraphColumnWidth` — a `ColumnDefinition` cannot bind a plain double, and
   a failed `GridLength` binding silently falls back to star.
4. The badge strip clips to that width, so a name longer than the column is ellipsised by the
   badge's own `MaximumTextWidth` rather than pushing the subject sideways.
5. Tests — `tests/.../HistoryPageTests.cs`: `RefColumnWidth` is 0 for a history with no refs, grows
   when a branch with a longer name is added, never exceeds the maximum; and, at view level, two
   rows' subject `TextBlock`s start at the same x whether or not the row carries a badge.

### Acceptance criteria

- With badges on some rows only, every row's subject, author, date and sha start at the same x.
- `RefColumnWidth` is 0 when nothing is decorated, so an undecorated history wastes no width.
- A 200-character branch name does not widen the column past its maximum and does not push the
  subject out of view.
- Build clean with zero warnings; the whole suite green.

## PHASE03 — Merging by dropping a branch

**Branch:** `feature/feature-14e8-phase03-branch-drop`
**Status:** DONE — see `docs/done/FEATURE-14E8-PHASE03.md`

### Steps

1. New `Services/BranchDropOperations.cs`: `BranchDropRequest(string Source, bool SourceIsRemote,
   string Target, bool TargetIsRemote, bool TargetIsCurrent)` and
   `IBranchDropOperations.DropAsync(BranchDropRequest)`, returning `true` when the repository
   changed.
2. The flow: refuse a drop on itself and a drop onto a remote branch (nothing local can be merged
   *into* a remote-tracking ref — say so in the info bar rather than letting git refuse it in its
   own words); ask which operation with a `ContentDialog` — primary `Merge`, secondary
   `Fast-forward only`, close `Cancel`; check the target out first when it is not the current branch
   and stop if that checkout does not happen; then `MergeAsync(source, WhenPossible | Only)`.
3. `DependencyInjection/ServiceCollectionExtensions.cs`: register `IBranchDropOperations` as a
   singleton beside the other operations services.
4. Tests — new `tests/.../BranchDropTests.cs` against a real repository: a merge drop onto the
   current branch merges without a checkout; a drop onto another branch checks it out first and
   merges there; the fast-forward-only answer refuses a merge that is not a fast-forward and says
   so; cancelling does nothing at all; a drop onto a remote branch and a drop onto itself are
   refused before any git runs.
5. Tests — `tests/.../CompositionRootTests.cs`: the new service resolves from the real container.

### Acceptance criteria

- Dropping `feature` onto `main` while on `main` records a merge and leaves `HEAD` on `main`.
- Dropping `feature` onto `main` while on `feature` checks `main` out, then merges — and reports
  both.
- The fast-forward-only answer on a branch that has diverged leaves the repository untouched and
  reports why.
- Cancelling the dialog runs no git command at all.
- A drop whose target is a remote branch, or whose source and target are the same, is refused with
  an explanation and no git command.
- Build clean with zero warnings; the whole suite green.

## PHASE04 — Dragging branches in the graph

**Branch:** `feature/feature-14e8-phase04-branch-drag`
**Status:** DONE — see `docs/done/FEATURE-14E8-PHASE04.md`

### Steps

1. `ViewModels/Pages/HistoryPageViewModel.cs`: `CanDropBranch(RefBadgeItem source, RefBadgeItem
   target)` — the policy the gesture asks before it accepts a drop — and
   `DropBranchCommand`, which builds the `BranchDropRequest`, runs `IBranchDropOperations` and
   reloads the history when anything changed.
2. `Views/Pages/HistoryPageView.axaml.cs`: the gesture. A press over a `RefBadge` standing for a
   branch arms a drag; a move past the platform threshold starts it with a `DataObject` carrying the
   badge; `DragOver` accepts only what `CanDropBranch` allows and sets the effect accordingly; a
   drop runs `DropBranchCommand`. Handled on the page rather than in the row template: a handler
   attached inside a `DataTemplate` is attached once per realised row, and the badges are in a
   virtualised list.
3. `Views/Pages/HistoryPageView.axaml`: `DragDrop.AllowDrop` on the commit list.
4. `Controls/RefBadge.cs` + `Themes/Controls.axaml`: a `droptarget` visual state — a ring around the
   badge the pointer is over — so a drag says where it would land.
5. Tests — `tests/.../HistoryPageTests.cs`: `CanDropBranch` refuses a badge dropped on itself, a tag
   as either end and a remote target, and accepts a local target; `DropBranchCommand` runs the
   operation and reloads; the commit list allows drops.

### Acceptance criteria

- `CanDropBranch` accepts only branch-to-local-branch pairs that are not the same badge.
- `DropBranchCommand` hands the operation exactly what the two badges say, and reloads the history
  when the operation reports a change.
- `DragDrop.GetAllowDrop` is true on the commit list of a shown page.
- A badge under a valid drag carries the `droptarget` class and loses it when the drag leaves.
- Build clean with zero warnings; the whole suite green.

## Out of scope

- Rebase, cherry-pick and reset as drop actions — rebase is a permanent non-goal, and the other two
  are not asked for.
- Dragging a commit row (as opposed to a badge), and dragging a badge onto a commit rather than onto
  another badge.
- Pushing or pulling as a result of a drop; a remote branch may be dragged, never dropped onto.
- Reordering, filtering or grouping the badge column's contents.
- Touch and pen drag gestures: the platform's own drag threshold is honoured, but nothing is tuned
  for a finger.

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| What a single click does now | Selects, and nothing else | It is what the request asks, and it makes the selection usable again as "where I am" for create-branch-here and the row highlight | Opening a non-modal panel on selection (the panel is what `FEATURE-2288` removed) |
| What closes the dialog | The reader's own dismissal, and the selection being cleared | A reload after a checkout drops the selection, and a dialog describing a commit nobody selected is stale | Leaving it open across a reload |
| `Activate`'s shape | `RelayCommand<CommitRowViewModel>` | Nothing it does is asynchronous once checkout leaves it; an async command that never awaits misstates its own contract | Keeping `AsyncRelayCommand` to avoid touching three tests |
| Where the badge column's width is decided | One page-level width, like `GraphColumnWidth` | A per-row `Auto` column is precisely why the columns beside it do not line up | A fixed width (clips short names' room, wastes it on undecorated histories); measuring in a converter (runs per row per frame) |
| Measuring the strip | `FormattedText`, cached, with an estimated fallback | `DiffTypography` already establishes this shape for exactly this problem, fallback included | Guessing from character counts (wrong for proportional faces) |
| Which drops are allowed | Branch onto local branch, not itself | A merge writes to the target, and nothing local writes to a remote-tracking ref | Allowing a remote target and letting git refuse (the error names a ref the user never typed) |
| What a drop offers | Merge, or fast-forward only | The two ways to join two branches in an app that structurally refuses rebase | A single implicit merge (hides fast-forward, which is what a reader usually wants when a branch is simply ahead) |
| Checking the target out | Yes, when it is not current, through `IBranchOperations` | `git merge` merges into `HEAD`, so "merge A into B" means being on B; the existing operation already asks the dirty-tree questions | Refusing a drop unless the target is already checked out (halves the gesture's use) |
| Where the flow lives | A new operations service | Dialog + checkout + merge + info bar is exactly what the other four operations services own | The page ViewModel (would need the dialog service and would not be reachable from the branches page later) |
| Where the gesture lives | The page's code-behind, handled on the page | Handlers inside a `DataTemplate` attach per realised row in a virtualised list | An attached behaviour class (more machinery for one page) |
