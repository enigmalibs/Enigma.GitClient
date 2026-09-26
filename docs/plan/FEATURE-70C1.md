# FEATURE-70C1 — Hide branches from the history

**Status:** DONE
**Type:** FEATURE
**Branch:** one per phase, see below
**Run:** feature/2026-09-26-release-1-0-0

## Objective

From the Branches dialog, let the user hide a branch from the History page — and show it again —
so the lines only that branch brings stop crowding the graph. The choice is remembered for the
repository.

## Context & constraints

- The History reads its commits with `git log … --all` (`CommitLogReader.BuildArguments`, shared with
  the `rev-list --count` query). git's `--exclude=<pattern>` leaves out the refs it matches from the
  **next** `--all`, `--branches`, `--remotes` or `--glob`, so it must come before `--all`. HEAD is not
  a ref under `refs/`, so `--all` always keeps HEAD's history whatever is excluded. git refuses `*`,
  `?` and `[` in ref names, so a full ref name used as the pattern only ever matches itself.
- A commit reachable from a branch that stays visible stays visible: hiding a branch hides what only
  it reaches. Tags are refs `--all` walks too, so a commit a tag points at stays in the graph.
- The Branches dialog (`BranchesPageViewModel`, `Views/Pages/BranchesPageView.axaml`) is one flat list:
  a *Local* group and one group per remote, each row a `BranchRowViewModel` over a `GitBranch`
  (`FullName` is `refs/heads/…` or `refs/remotes/…`), with action buttons (check out, delete) and a
  row menu.
- The History's rows take their badges from `RepositoryContext.Decorations.GetRefs(sha)`; the page
  reloads keeping its place with `ReloadKeepingPlaceAsync`, and defers a refresh while the diff view
  has the page (`_refreshPending`). The toolbar already shows a chip with a clear button for the merge
  source.
- One repository per process. Per-user state lives in the configuration directory as versioned JSON
  written with `AtomicFile`, re-read before every write because several instances share the files
  (`RecentRepositoryStore`, keyed by work-tree path, lives in `App/Services`; `IdentityProfileStore`
  moves a corrupt file aside). The README lists every file the client writes there.

## Decisions

| Decision | Rationale |
|---|---|
| Hidden refs are left out with `--exclude=<full ref>` before `--all` | git does the walk; the lines that disappear are exactly the ones only that branch reaches |
| One toggle per row, local and remote-tracking alike | They are separate refs: hiding `feature` while `origin/feature` is visible leaves the same commits, which is what git would draw too |
| The checked-out branch cannot be hidden | `--all` keeps HEAD, so a toggle there would do nothing; it is disabled and says why |
| A hidden branch's badge is not drawn either | Otherwise hiding a merged branch changes nothing visible |
| Remembered per repository, in `hidden-branches.json` | Hiding a noisy branch is not a one-session wish; the file joins the README's table |
| A "N branches hidden" chip on the History toolbar, whose clear button shows them all again | The graph is filtered; saying so, with a way out, is the merge-source chip's rule |

## PHASE01 — Leaving refs out of the walk

**Status:** DONE — see `docs/done/FEATURE-70C1-PHASE01.md`
**Branch:** `feature/feature-70c1-phase01-excluded-refs`

### Steps

1. `Core/History/CommitLogQuery.cs` — `ExcludedRefs` (`IReadOnlyList<string>`, empty by default):
   full ref names left out of an `AllRefs` walk; counted by `IsFiltered`.
2. `Core/History/CommitLogReader.cs` — for `AllRefs`, one `--exclude=<ref>` per entry that starts with
   `refs/` (blank or other entries skipped), placed immediately before `--all`; the count query gets
   the same arguments. Other scopes ignore the list.
3. Tests — unit: the argument vector (order, skipped entries, other scopes, the count query);
   integration against real git: a branch's own commits disappear when it is excluded, the commits it
   shares with a visible branch stay, excluding the checked-out branch changes nothing, and the count
   agrees with the page.

### Acceptance criteria

- A query can leave named refs out of the walk, and git's own semantics decide what disappears.
- Build clean with zero warnings; the whole suite green.

## PHASE02 — Remembering hidden branches

**Status:** DONE — see `docs/done/FEATURE-70C1-PHASE02.md`
**Branch:** `feature/feature-70c1-phase02-hidden-branch-store`

### Steps

1. `App/Services/HiddenBranchStore.cs` — `IHiddenBranchStore` with `GetAsync(workTreePath)` and
   `SetHiddenAsync(workTreePath, refName, hidden, existingRefs)`; `hidden-branches.json`
   (`{ "version": 1, "repositories": { "<path>": ["refs/…", …] } }`), gated, re-read before every write,
   written atomically; a corrupt file moved aside as `hidden-branches.corrupt-<stamp>.json`; entries
   that are blank or not under `refs/` ignored; entries for refs that no longer exist dropped when the
   repository's set is written.
2. `App/Services/HiddenBranches.cs` — `IHiddenBranches` for the open repository: `IsHidden(fullName)`,
   `Hidden` (a read-only set), `SetHiddenAsync(fullName, hidden)`, `ShowAllAsync()`, and a `Changed`
   event; it reloads when `IRepositoryContext` opens another repository and empties when it closes; a
   store failure is logged and read as "nothing hidden".
3. Register both in `AddGitClientApp`; the README's *Where your things are kept* table gains the file.
4. Tests — the store against a temporary configuration directory (round trip, per repository, show
   again, pruning, a corrupt file, two stores writing in turn); the service with a fake store (reload
   on repository change, `Changed` raised once per real change, a failing store).

### Acceptance criteria

- Which branches are hidden is kept per repository across restarts and across instances.
- Build clean with zero warnings; the whole suite green.

## PHASE03 — Show and hide from the branches

**Status:** DONE — see `docs/done/FEATURE-70C1-PHASE03.md`
**Branch:** `feature/feature-70c1-phase03-branch-visibility`

### Steps

1. `BranchRowViewModel` — `IsHiddenInHistory`, `CanChangeVisibility` (not for the checked-out branch),
   `VisibilityTip`, and the page's `ToggleVisibilityCommand`; the page rebuilds its rows when
   `IHiddenBranches.Changed` fires.
2. `BranchesPageView.axaml` — an eye / eye-slash button first among a row's actions, a matching item in
   the row's menu, and a hidden row drawn dimmed.
3. `HistoryPageViewModel` — the query's `ExcludedRefs` is the hidden set that still exists, less the
   checked-out branch; a hidden branch's badge is left off its row; `Changed` reloads the history
   keeping its place (deferred while the diff view has the page); `HiddenBranchCount`,
   `HiddenBranchesSummary` and a `ShowHiddenBranchesCommand`.
4. `HistoryPageView.axaml` — the "N branches hidden" chip with its clear button.
5. Tests — the row toggle hides and shows, the checked-out branch's toggle is disabled, hidden rows are
   marked; the history's query excludes the hidden refs and not the current branch, its rows carry no
   badge for a hidden branch, the chip counts and clears, and a change reloads the page keeping the
   selection.
6. README feature list and release notes (sweep).

### Acceptance criteria

- Hiding a branch in the Branches dialog removes its own lines and its badge from the History;
  showing it again brings them back; the choice survives a restart.
- The checked-out branch cannot be hidden.
- The History says how many branches are hidden and can show them all again.
- Build clean with zero warnings; the whole suite green.

## Out of scope

- Hiding tags, stashes or HEAD.
- Hiding from the History's own badge menu.
- Following a branch through a rename.

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| How the history leaves a branch out | `git log --exclude=<ref>… --all` | git walks exactly what stays visible; nothing to reimplement in the graph | Listing every visible ref on the command line (long argv on big repositories); filtering rows after the walk (lanes between commits that are not adjacent — a graph of a history that does not exist) |
| Persisted or for the session | Persisted per repository | A branch someone wants out of the way is still in the way tomorrow | Session only |
| Where it is stored | A new `hidden-branches.json` in the configuration directory, beside `recent-repositories.json` | The client's own view preference, keyed by work tree like the recents; kept out of the repository | The repository's `.git/config` (writes git's configuration for a view preference); `settings.json` (preferences, not per-repository lists); `recent-repositories.json` (forgetting a recent would lose them) |
| Per ref or per branch name | Per ref, one toggle per row | What git itself distinguishes, and what the user sees listed | Hiding a local branch and its upstream together (surprising when they have diverged) |
| The checked-out branch | Its toggle is disabled with a tooltip | HEAD is always walked; a toggle that does nothing is a lie | Allowing it and switching `--all` for an explicit list (hides the commit you are on) |
| The badge of a hidden branch | Not drawn | Otherwise hiding a merged branch has no visible effect at all | Keeping it |
| Saying the graph is filtered | A chip with a count and a clear button on the History toolbar | Same rule as the merge source: a choice that outlives the dialog it was made in is visible and reversible | Nothing (commits vanish with no explanation) |
| Stale entries | Dropped when the repository's set is next written; ignored until then | A deleted branch costs nothing; no refresh-time bookkeeping | Pruning on every refresh; keeping them for ever |
| Three phases | Engine, store, UI | Each a small reviewable commit with its own tests | One commit |
