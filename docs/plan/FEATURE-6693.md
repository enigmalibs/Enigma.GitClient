# FEATURE-6693 — Submodule support

**Status:** IN PROGRESS
**Type:** FEATURE
**Branch:** one per phase, see below
**Run:** feature/2026-11-09-feature-6693

## Objective

A colleague works in repositories with submodules. Today the app clones them only when an unticked
box in the Clone dialog's advanced section is ticked, shows a submodule as a plain line, says only
"the recorded commit changed", and never moves a submodule's folder — so the three classic accidents
are all possible:

1. **empty submodule folders** after a clone;
2. **a stale folder** after a pull or checkout that moved the pin, whose old commit a later
   *Stage all + Commit* silently records again, undoing a colleague's bump;
3. **a pin pushed before its commit**: the parent is pushed, the submodule's commit is not.

This item makes the app follow submodules safely and lets the user work inside one:

- submodules are downloaded at clone and follow every pull, checkout, merge, reset, revert and squash
  the app performs (a setting turns it off);
- a *Submodules* dialog lists them with their state and opens one in its own window;
- a band says when a submodule is missing or behind the commit the repository expects;
- a window on a submodule says whose it is, and offers a branch when it is on none;
- a pin change shows the commits it brings in or takes back; committing a pin that goes back asks
  first; *Discard* puts a submodule back on its expected commit;
- a push never publishes a pin whose commit is not on the submodule's server, and offers to push the
  submodule first;
- a conflict on a pin is resolved by picking one of the two commits.

**Out of scope:** adding, removing or re-pointing (URL) a submodule; `git submodule foreach`;
shallow/sparse submodule options; a release item (the user releases separately).

## Vocabulary

- **Pin** (plan only) — git's *gitlink*: the mode-`160000` entry a commit or the index records for a
  submodule's path. The UI says "the commit this repository expects" / "expected commit".
- **States** (UI words): *Up to date* · *Behind* — checked out on an ancestor of the expected commit
  (stale; the accident) · *New commits* — checked out on a descendant (work in progress, not recorded
  yet) · *Elsewhere* — on a commit on another line · *Not downloaded* · plus the flag *Has uncommitted
  changes*.

## Context & constraints

- **Git is the CLI** (`IGitCommandFactory` / `IGitProcessRunner`), minimum git 2.20.0. Every
  `git submodule` subcommand used here exists in 2.20.
- **What exists:** `CloneRequest.RecurseSubmodules` (`--recurse-submodules`) and its checkbox
  (`CloneRepositoryDialogView.axaml:73`, unticked, in the advanced expander); `StatusService` runs
  `--ignore-submodules=none` and `PorcelainV2Parser` fills `ChangedFile.Submodule` (`SubmoduleState`:
  commit changed / modified / untracked), which no view shows; `DiffModel.IsSubmodule` and the message
  at `DiffViewerViewModel.cs:801`; `RepositoryLayout` knows a submodule's git directory is
  `<super>/.git/modules/<name>`; `RepositoryLocator` already resolves a submodule folder as a
  repository of its own (git does it).
- **Rebase is forbidden** (`Git/ForbiddenGitOperations.cs`). `git submodule update` honours
  `submodule.<name>.update` (`rebase`, `merge`, `!command`) unless a mode is passed, so every update
  the app runs passes `--checkout`.
- **Discard today** (`Staging/StagingService.cs`): `DiscardAsync` runs
  `restore --staged --worktree --source=HEAD`, which puts the index pin back but never moves the
  submodule's checkout (git needs `--recurse-submodules` for that); `DiscardAllAsync` is
  `reset --hard` + `clean -d`, same.
- **Patterns to reuse:**
  - Worktrees — `ToolDialog.Worktrees` in `Services/ToolDialogService.cs`, `WorktreesPageViewModel` /
    `WorktreesPageView`, the toolbar button in `HistoryPageView.axaml` (~line 230) and its line menu
    (~726–750);
  - `IInstanceLauncher.Launch(path)` opens a repository in a new window;
  - `MainWindow.axaml`: `LinkedWorktreeMark` (a window on a linked worktree says whose it is),
    `DetachedHeadPill`, the operation banner and `SignInNoticeBand` (persistent bands);
  - `SyncOperations.PushOrOfferForceAsync` (a push failure handed over to an offer) and `IPushGuard`
    (the profile push rule, per repository and remote);
  - every write runs through `IRepositoryContext.RunExclusiveAsync` (overlay, serialisation, refresh);
  - questions that lose work are red with Cancel focused (FEATURE-0A3D).
- **Settings:** `Core/Configuration/AppSettings.cs` (init properties with defaults); the Pull setting
  is in `SettingsPageView.axaml` (~line 320), texts in `SettingsDescriptions`.
- **Tests:** Core integration tests run real git in temporary repositories;
  `Infrastructure/GitWorkspace.cs` isolates `GIT_CONFIG_GLOBAL`. Since git 2.38.1 a submodule cannot
  be cloned from a local path without `protocol.file.allow=always`: the fixture writes it into its
  isolated global config — **never** the product. Desktop tests are headless (Avalonia.Headless).

## Rules every phase keeps

1. **Never lose work.** An automatic update never moves a submodule that has uncommitted changes, or
   whose checked-out commit no branch, tag or remote branch contains (it would be orphaned). Such a
   submodule is skipped and named, with the reason. An explicit *Discard* may move the second kind
   after a red question that says so.
2. **Only what the operation changed.** The automatic follow moves only the submodules whose pin the
   operation changed (HEAD before vs HEAD after), plus those listed in `.gitmodules` that are not
   downloaded. A submodule the user moved on purpose is never touched by it.
3. **Recursive.** Nested submodules are listed, downloaded and followed.
4. **The app's follow step is the only thing that moves submodules.** Updates pass `--checkout`; the
   commands that move HEAD run with `-c submodule.recurse=false`, so a user's
   `submodule.recurse=true` cannot make git move (or rebase) submodules outside these rules.
5. **Free without submodules.** Nothing new runs when the work tree has no `.gitmodules`.
6. **Untrusted input.** `.gitmodules` comes from whoever made the repository: paths always go after
   `--`; URLs are shown and logged with credentials redacted (`Git/ArgumentRedactor.cs`).

## PHASE01 — Read and update submodules in Core

**Branch:** `feature/feature-6693-phase01-submodule-service`
**Status:** DONE

### Steps

1. `Core/Submodules/` (new, beside `Worktrees/`): a `GitSubmodule` record — `Name`, `Path` (relative,
   `/`-separated), `Url`, `ExpectedSha` (the index's pin), `CheckedOutSha` (null when not
   downloaded), `State` (the vocabulary above), `HasUncommittedChanges`, `Parent` (null at top level).
2. `ISubmoduleService` / `SubmoduleService`:
   - `ListAsync(repository, ct)` — `git submodule status --recursive` (prefixes `-` not downloaded,
     `+` checked out elsewhere, `U` conflicted, space up to date) joined with
     `git config -f .gitmodules --get-regexp` for names and URLs; *Behind* / *New commits* /
     *Elsewhere* from `git -C <sub> merge-base --is-ancestor`; uncommitted changes from the parent's
     porcelain v2 status for top-level submodules, from the submodule's own status for nested ones.
     Returns an empty list at once without `.gitmodules`.
   - `UpdateAsync(repository, paths (null = all), progress, ct)` → `SubmoduleUpdateResult` (updated,
     skipped with reason, failed with git's message). Applies rule 1, then
     `git submodule update --init --recursive --checkout --progress -- <paths>`.
   - `ChangedPinsAsync(repository, oldSha, newSha, ct)` — paths whose gitlink differs
     (`git diff-tree -r --no-renames`, mode `160000` on either side).
   - `IsOnAnyRefAsync(submodule, sha, ct)` — `for-each-ref --contains <sha> --count=1` in the
     submodule.
3. `ForbiddenGitOperations`: refuse `submodule update --rebase` and a `-c submodule.<name>.update`
   override whose value is `rebase`.
4. Progress: reuse the clone/sync progress parsing where git's stderr has the same shape.
5. Register in Core's DI extension.

### Acceptance criteria

- Integration (local submodule repositories through the fixture): a repository with an up-to-date, a
  not-downloaded, a behind, an ahead and a nested submodule is listed with the right states; update
  downloads the missing one and moves the behind one; a submodule with uncommitted changes is skipped
  and named; one on a commit no ref contains is skipped; `ChangedPinsAsync` names exactly the moved
  pins; with `submodule.<name>.update=rebase` configured, update checks the commit out (detached) and
  rebases nothing.
- Unit: `submodule status` parsing (all prefixes, the `(heads/main)` suffix, paths with spaces),
  `.gitmodules` config parsing, the new forbidden cases.
- Zero build warnings; every suite green against the baseline.

## PHASE02 — The Submodules dialog

**Branch:** `feature/feature-6693-phase02-submodules-dialog`
**Status:** DONE

### Steps

1. `ToolDialog.Submodules` → `SubmodulesPageViewModel` / `SubmodulesPageView`, built like Worktrees.
   A *Submodules* toolbar button beside *Worktrees* in `HistoryPageView.axaml`, visible only when the
   repository has `.gitmodules` (`IRepositoryContext.HasSubmodules`, read at refresh).
2. One line per submodule, nested ones under their parent: path, short expected commit, state in
   words (*Elsewhere* names the commit it is on), a mark for uncommitted changes; tooltip with name
   and (redacted) URL.
3. Line menu, an icon on every item (`MenuIconTests`): *Open in a new window* (`IInstanceLauncher`;
   disabled when not downloaded) · *Update* (`UpdateAsync([path])`, overlay progress; skipped/failed
   named in an InfoBar) · *Open folder* · *Open terminal* · *Copy path*. The header has *Update all*.
4. Writes through `RunExclusiveAsync`; the list follows `StateRefreshed`.
5. README *Features*: a submodules bullet (later phases extend it).

### Acceptance criteria

- Headless: the button's visibility; lines and states from a fake `ISubmoduleService`; menu items
  enabled per state; every item has an icon; *Update* calls the service with the line's path.
- Zero build warnings; every suite green against the baseline.

## PHASE03 — Submodules follow the pin

**Branch:** `feature/feature-6693-phase03-follow-pins`
**Status:** DONE

### Steps

1. `AppSettings.UpdateSubmodulesAutomatically` (default `true`; a settings file without it loads
   `true`) and its switch in Settings beside Pull, with its `SettingsDescriptions` text.
2. The follow step: after a write that moved HEAD — checkout (branch, commit, tag), pull, merge (drag
   included, and finishing a conflicted merge), reset, revert, squash with checkout — when the setting
   is on and `.gitmodules` exists: `ChangedPinsAsync(before, after)` plus the not-downloaded ones,
   then `UpdateAsync(those)`, inside the same exclusive write, before its refresh, with the overlay's
   progress and Cancel. Preferred home: one place in the write path (`RepositoryContext` notes HEAD
   before and after, and calls an `ISubmoduleFollower`), so a future operation is covered; a write can
   opt out. Skipped or failed submodules: one InfoBar naming them and why, with *Submodules…*.
3. Rule 4: `-c submodule.recurse=false` on the commands that move HEAD.
4. *Create worktree*: when the setting is on, the new worktree's submodules are downloaded before its
   window opens.
5. Clone: *Clone submodules as well* starts ticked (`CloneRepositoryDialogViewModel`); it stays in the
   advanced section.
6. A band in `MainWindow` (pattern: `SignInNoticeBand`) when a submodule is *Not downloaded* or
   *Behind*, whatever the cause (setting off, a pull in a terminal, a skipped update):
   "2 submodules are not on the commit this repository expects" with *Update* (rule 1) and
   *Submodules…*. *New commits* and *Elsewhere* do not raise it — that is usually work in progress.
7. `RepositoryLayout`: a change under `<git dir>/modules/**/HEAD` or `…/refs/` is a working-tree change
   of the parent, so a commit made inside a submodule refreshes the parent within a second.

### Acceptance criteria

- Integration: after a pull that moves a pin, the submodule is on the new commit; checking out a branch
  that adds a submodule downloads it; a submodule the user moved on purpose, untouched by the
  operation, stays where it was; with the setting off nothing moves; `submodule.recurse=true` in the
  repository's config changes nothing.
- Unit/headless: the setting's round trip and default for an old file; the band's visibility rules;
  the clone box ticked by default; the new `RepositoryLayout` rules.
- Zero build warnings; every suite green against the baseline.

## PHASE04 — Working inside a submodule

**Branch:** `feature/feature-6693-phase04-submodule-window`
**Status:** TODO

### Steps

1. Core: discovery learns the superproject — `git rev-parse --show-superproject-working-tree` (empty
   when the repository is not a submodule) — on `RepositoryHandle` or beside it.
2. `MainWindow`: a window on a submodule carries a mark after the repository's name (pattern:
   `LinkedWorktreeMark`), tooltip "Submodule libs/Common of MainApp — <parent path>".
3. A band in a submodule's window while HEAD is detached: "You're on no branch — commits made here
   are easy to lose." Offers *Check out <branch>* for a local branch at HEAD (else a remote branch at
   HEAD, through the existing tracking-branch checkout) and always *Create a branch…* (the existing
   dialog, at HEAD). Hidden once on a branch; never shown in a window that is not a submodule's.

### Acceptance criteria

- Integration: the superproject is found for a submodule, nothing for a plain repository.
- Headless: the mark and the band's visibility and offers (fakes).
- Zero build warnings; every suite green against the baseline.

## PHASE05 — Pin changes: diff, guard, discard

**Branch:** `feature/feature-6693-phase05-pin-changes`
**Status:** TODO

### Steps

1. Core: the patch's two SHAs (`Subproject commit <sha>[-dirty]`) on the diff model, and
   `ISubmoduleService.DescribePinChangeAsync(repository, path, oldSha, newSha, ct)` → `PinChange`
   (`Forward` / `Back` / `Diverged` / `Unknown`, and the commits between — title, author, date, short
   SHA — capped with "and N more"). *Unknown* when the submodule is not downloaded or lacks a commit;
   no fetch is triggered.
2. Diff viewer, for a submodule entry, in the history and in the working changes: "libs/Common:
   a1b2c3d → f00ba12 · 3 new commits" and a row per commit; *Back* in the warning colour, "goes back
   3 commits"; *Diverged* names both counts; *Unknown* shows the two hashes and why. Dirt inside the
   submodule (modified / untracked content) says so, with *Open submodule*.
3. Working changes: a submodule's line carries a badge for its state (new commits, modified content,
   untracked content) from `ChangedFile.Submodule`, and *Open submodule* in its menu.
4. Commit guard: before a commit, every staged pin change that goes *Back* raises one red question —
   "This commit moves libs/Common back 3 commits (to a1b2c3d). Is that intended?" — *Commit anyway* /
   Cancel. *Forward* and *Unknown* ask nothing.
5. *Discard* on a submodule's line: refused when it has uncommitted changes (named, with *Open
   submodule*); otherwise a red question naming the commit it goes back to — and, when commits no ref
   contains would be left behind, how many — then the index pin back to HEAD and the checkout moved
   (`UpdateAsync([path])`). *Discard all* puts every such submodule back too, listed in its question.

### Acceptance criteria

- Integration: `DescribePinChangeAsync` forward, back, diverged and not downloaded; discarding a
  submodule moves it back; discard is refused with dirt inside.
- Unit/headless: `Subproject commit` parsing with and without `-dirty`; the guard asks only for *Back*;
  the diff viewer's texts per direction.
- Zero build warnings; every suite green against the baseline.

## PHASE06 — Push guard for submodule commits

**Branch:** `feature/feature-6693-phase06-push-guard`
**Status:** TODO

### Steps

1. `SyncService`: every push except a delete passes `--recurse-submodules=check` (it overrides a
   user's `push.recurseSubmodules`). git's refusal ("The following submodule paths contain changes
   that can not be found on any remote:" + paths) maps to a new `SyncFailureKind.UnpushedSubmodule`
   carrying the paths.
2. `SyncOperations`: that failure is handed over (like `NonFastForward`) to an offer naming each
   submodule and its branch — *Push libs/Common, then this repository*. For each submodule:
   `IPushGuard.CheckAsync` on the submodule and its remote, a push of its current branch (setting
   the upstream on a first push, as the app's own push does), then the parent's push again. A
   submodule on no branch is named with *Open submodule* instead of being pushed.
3. The force push (`--force-with-lease`) carries the check too.

### Acceptance criteria

- Integration: a parent push whose pin is unpushed fails with `UnpushedSubmodule` and the right
  paths; after the submodule is pushed, the parent push succeeds.
- Unit/headless: the failure mapping over a captured transcript; the offer's text; the order of the
  pushes; a profile that may not push to the submodule's remote refuses it (fakes).
- Zero build warnings; every suite green against the baseline.

## PHASE07 — Pin conflicts on the conflict page

**Branch:** `feature/feature-6693-phase07-pin-conflicts`
**Status:** TODO

### Steps

1. Core, `Merging/ConflictService.cs`: a conflict whose stages 2 and 3 are both mode `160000` is a
   submodule conflict with `OursSha` / `TheirsSha`, never read as text. `ResolveSubmoduleAsync(path,
   sha)` → `git update-index --cacheinfo 160000,<sha>,<path>`, then `UpdateAsync([path])` (rule 1; when
   the folder cannot move, the conflict is still resolved and the result says the folder stayed).
   `ResolveAllWithAsync` resolves submodules the same way.
2. Conflict page: two cards — "Ours: f00ba12 — Fix X" / "Theirs: c0ffee1 — Add Y" (author and date;
   hashes alone when the submodule is not downloaded) — with *Take ours* / *Take theirs*, and "A pin is
   one commit: to keep both, merge them inside the submodule first" with *Open submodule*. Any other
   submodule conflict (deleted on one side, added on both) says "resolve this one in a terminal"; the
   rest of the merge works as usual.

### Acceptance criteria

- Integration: two branches move the pin to divergent commits; the merge reports a submodule
  conflict; *theirs* resolves it, checks the submodule out, and the merge commits.
- Headless: the cards and their commands (fakes).
- Zero build warnings; every suite green against the baseline.
