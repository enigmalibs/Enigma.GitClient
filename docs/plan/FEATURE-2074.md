# FEATURE-2074 — Stashes like GitKraken

**Status:** IN PROGRESS
**Type:** FEATURE
**Branch:** one per phase, see below
**Run:** bugfix/2026-09-28-history-stash-menus-release

## Objective

Handle stashes from the history, the way GitKraken does:

- create a stash from the uncommitted files;
- pop a stash: when it applies without conflicts its files become uncommitted changes and the stash is
  gone; otherwise the stash is kept;
- apply a stash: its files become uncommitted changes and the stash is always kept;
- delete a stash, after a confirmation.

And draw each stash as GitKraken does: **one** line, carrying the stash icon.

## Context & constraints

- **The engine exists.** `Core/Stashes/StashService.cs`: `ListAsync` (`stash list` with a unit-separated
  format), `PushAsync` (`--include-untracked` by default, optional `--message`), `ApplyAsync`,
  `PopAsync`, `DropAsync` (all `throwOnError: true`), `ShowAsync`, `BranchAsync`.
- **The only UI is on the Changes page** (`ChangesPageViewModel`: `StashAllCommand`, `ApplyStashCommand`,
  `PopStashCommand`, `DropStashCommand` with a confirmation), in an expander that lives inside the grid
  shown only while the tree is dirty — so the stash list disappears exactly when the tree is clean.
- **Conflicts today.** `git stash pop`/`apply` with conflicts exits 1, prints `CONFLICT (…)` on
  standard output, leaves unmerged entries in the index and keeps the entry ("The stash entry is kept in
  case you need it again."). A refusal ("Your local changes … would be overwritten", "already exists,
  no checkout") also exits 1 but changes nothing. Both currently surface as "Could not restore the
  stash" — the conflicted case wrongly, since the changes did arrive.
- **The history's stash lines.** The log walks `--all`, which includes `refs/stash` = the stash commit
  **W** of `stash@{0}` only. W is a merge of the base commit **B**, git's *index* commit **I**
  ("index on main: …") and, with untracked files, a root *untracked* commit **U**. So the graph draws
  W with the stash badge on top and I (and U) as extra lines merging into it; older entries
  (`stash@{1}`…) are not drawn at all. GitKraken draws each entry once, off its base commit, with the
  stash icon and git's own message (`WIP on main: 1a2b3c4 subject` / `On main: message`), which is W's.
- `RefBadge` already knows `GitRefKind.Stash` (Archive icon, `stash` class).
- The history reads the log in pages (`CommitLogQuery`, `--date-order`): a child always precedes its
  parents, so W is always read before I and U.

## PHASE01 — One line per stash in the history

**Branch:** `feature/feature-2074-phase01-one-line-per-stash`
**Status:** DONE — see `docs/done/FEATURE-2074-PHASE01.md`

### Steps

1. `CommitLogQuery.IncludedRevisions` — extra commits to walk from, after `--all`; `BuildArguments`
   accepts only full or abbreviated hexadecimal object names there (never an option).
2. `HistoryPageViewModel`:
   - reads the stash list (`IStashService.ListAsync`) at the start of each reload and walks every entry
     (`IncludedRevisions`);
   - in `AppendPage`, for each stash commit W: its parents after the first are remembered as the
     entry's helper commits and W is laid out with its first parent only; a helper commit that carries
     no reference is left out of the rows (across pages, reset on reload);
   - each stash line carries a stash badge named after its entry (`stash@{n}`) instead of the raw
     `refs/stash` decoration;
   - `RefreshInPlaceAsync` also redraws when the stash list differs from the one drawn (a drop of an
     older entry moves no reference).
3. `CommitRowViewModel`: the stash entry it stands for (`Stash`, `IsStash`).
4. Tests:
   - Core: `BuildArguments` places included revisions after `--all` and refuses non-hex values;
   - App (real git): one stash with untracked files → exactly one line for it, W's SHA, the stash
     badge, no "index on"/"untracked files on" line, the lane joins its base commit; two stashes →
     two stash lines; dropping the older entry redraws on the next refresh.

### Acceptance criteria

- Every stash entry is one line in the history, with the stash icon, at the commit `git stash list`
  names, drawn off the commit it was made on; git's index and untracked helper commits never appear.
- The rest of the graph is unchanged.
- Build clean with zero warnings; the whole suite green.

## PHASE02 — Stash operations that know about conflicts

**Branch:** `feature/feature-2074-phase02-stash-operations`
**Status:** TODO

### Steps

1. Core `StashService`: `ApplyAsync` and `PopAsync` return a `StashApplyResult` (`Applied`,
   `Conflicted`): exit code 1 with `CONFLICT (` on standard output is `Conflicted` (the entry is kept by
   git); any other failure still throws.
2. App `Services/StashOperations.cs` (`IStashOperations`, singleton), in the `*Operations` pattern:
   - `StashAsync()` — a dialog (`StashDialogView`/`StashDialogViewModel`) with an optional message;
     stashes everything, untracked files included; "nothing to stash" is a warning;
   - `ApplyAsync(entry)` / `PopAsync(entry)` — success info; conflicted → a warning that the stash was
     kept and the conflicted files are on the Changes page; refused → an error, nothing changed;
   - `DropAsync(entry)` — a confirmation naming the stash, the harmless button as default.
   All run under `RunExclusiveAsync`.
3. `ChangesPageViewModel`: Stash all, Apply, Pop and Drop go through `IStashOperations`, so the two
   places behave the same.
4. Tests: Core integration (conflicted pop keeps the entry and reports `Conflicted`; clean pop removes
   it; refused apply throws); App (dialog message used; drop asks and a cancel keeps the entry; a
   conflicted pop reports a warning and keeps the stash; the Changes page still works).

### Acceptance criteria

- Pop without conflicts: the changes are uncommitted files and the stash is gone. Pop with conflicts:
  the stash is kept and the reader is told so. Apply always keeps the stash.
- Delete asks first; cancelling keeps the stash.
- A stash can be given a message; without one git's own `WIP on …` is used.
- Build clean with zero warnings; the whole suite green.

## PHASE03 — Stashing from the history

**Branch:** `feature/feature-2074-phase03-stash-in-history`
**Status:** TODO

### Steps

1. `HistoryPageViewModel` takes `IStashOperations`:
   - `StashCommand` (toolbar button, Archive icon, enabled while the history shows uncommitted
     changes) and a "Stash all changes…" item on the uncommitted line's menu;
   - on a stash line, the line menu offers only **Apply stash**, **Pop stash** and **Delete stash…**
     (plus showing what it changed); the commit actions (branch, tag, checkout, reset) are not offered
     on a stash;
   - every successful stash operation reloads the history.
2. `HistoryPageView.axaml`: the Stash button in the toolbar.
3. Tests (headless, real git): stash from the toolbar and from the uncommitted line; apply/pop/delete
   from a stash line's menu, with the expected rows afterwards; a stash line's menu has no commit
   actions.

### Acceptance criteria

- The four operations are reachable from the history and behave as PHASE02 defines.
- The history is redrawn after each of them.
- Build clean with zero warnings; the whole suite green.

## Out of scope

- Editing a stash's message; hiding stashes from the graph; `stash branch` from the history.
- Resolving stash conflicts on the conflicts page (it serves merges; conflicted files stay on the
  Changes page).
- A stash's untracked files in the history's diff view (they are on the Changes page's stash list).

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| Which line stays | The stash commit W (the SHA `git stash list` names), with the stash icon; I and U are hidden | W is what git calls the stash, what every stash command takes, and carries the message GitKraken shows (`WIP on main: <base> …`, the base SHA the reader recognised); I and U are git's internal bookkeeping | Keeping I with the icon (not the stash; its SHA is not the entry's) |
| Older entries | Every entry is drawn | GitKraken draws them all, and the history is where they are handled now | Only `stash@{0}` (what `--all` gives) |
| Badge text | `stash@{n}` | The name every stash command uses; tells two stashes apart | A bare "stash" |
| Conflict detection | Exit 1 + `CONFLICT (` on stdout → `Conflicted`, no exception | The changes did arrive and git kept the entry; that is a warning, not a failure | Parsing the index afterwards |
| Stash message | Optional, in a small dialog | Several stashes are indistinguishable without one | One-click with git's default only |
| Untracked files | Always included | The existing Stash-all behaviour; "uncommitted files" includes new ones | A checkbox |
| Where to stash from | Toolbar button + uncommitted line menu; the Changes page button uses the same flow | GitKraken has both; one flow app-wide | Main window toolbar (the history is where stashes now live) |
| Stash line menu | Apply / Pop / Delete…, show changes; no commit actions | GitKraken's stash menu; branching or resetting to a stash commit is a trap | The full commit menu |
| Phasing | Display, engine + service, history UI | One reviewable commit each | One large dev |
