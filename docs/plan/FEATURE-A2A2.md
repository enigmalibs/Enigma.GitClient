# FEATURE-A2A2 — Tags: bare placeholder, push from menus

**Status:** IN PROGRESS
**Type:** FEATURE
**Branch:** one per phase, see below
**Run:** bugfix/2026-09-28-history-tag-push-release

## Objective

- The *Create a tag* dialog's name box suggests `1.0.0`, not `v1.0.0`.
- A tag can be pushed to the remote on its own:
  - from its badge's context menu on a history line;
  - from its line's context menu in the Tags dialog.

## Context & constraints

- **The placeholder.** `Views/Dialogs/CreateTagDialogView.axaml`, the name `TextBox`:
  `PlaceholderText="v1.0.0"`. This repository's own releases are tagged bare `X.Y.Z`
  (`docs/RELEASE.md`).
- **Pushing today.** Tags reach a remote only as a side effect of pushing a branch
  (`PushRequest.PushTags` → `--follow-tags`), and `--follow-tags` pushes annotated tags only. A
  lightweight tag cannot be pushed from the app at all.
- **The engine.** `Core/Sync/SyncService.cs`:
  - `ISyncService.PushAsync(repository, PushRequest, progress, token)`, with the arguments built by the
    public static `BuildPushArguments` (unit-tested in `SyncParsingTests`);
  - every transfer signs in through `IGitCredentialResolver` and maps git's error text through
    `SyncErrorMapper`. A rejected tag ("Updates were rejected because the tag already exists in the
    remote") maps to `SyncFailureKind.NonFastForward`, whose message talks about commits.
- **The operation.** `App/Services/SyncOperations.cs` is what the history's branch badge calls
  (`PushBranchAsync`):
  - the push guard (`MayPushAsync`), since a profile pushes only where one of its integrations leads;
  - the progress overlay with a working Cancel;
  - the exclusive run;
  - the info bar, with an `explain` hook for a failure that needs its own sentence.

  A push with no upstream goes to `origin` (`GitRemote.DefaultName`).
- **The history's tag badge.**
  - It is `HistoryTagViewModel(RefBadgeItem Badge, AsyncRelayCommand<string> Copy)`, built in
    `CommitRowViewModel.BuildBranches` from `HistoryRowCommands.Copy`.
  - Its menu is in `HistoryPageView.axaml` (`DataTemplate DataType="vm:HistoryTagViewModel"`): *Copy tag
    name* only.
  - The branch badge's menu reads `Push "<name>"` with the `ArrowUp` icon, and ends with the copy item
    after a separator.
- **The Tags dialog.**
  - `Views/Pages/TagsPageView.axaml`: a row's context menu holds *Select in the history* · *Check out
    (detaches HEAD)* · *Delete…*.
  - `TagRowViewModel` carries its commands, built by `TagsPageViewModel`, whose writes go through
    operations services (`ITagOperations`, `ICheckoutOperations`).
- **Tests.**
  - Core: `SyncServiceTests` pushes to a bare repository in a temporary directory.
  - App: `RemotesAndSyncTests` builds the same kind of world (`BuildWorldAsync`) and checks the push
    guard.
  - `AutoRefreshTests` has a hand-written `ISyncOperations` double.

## PHASE01 — The placeholder says 1.0.0

**Branch:** `feature/feature-a2a2-phase01-bare-tag-placeholder`
**Status:** DONE — see `docs/done/FEATURE-A2A2-PHASE01.md`

### Steps

1. `CreateTagDialogView.axaml`: `PlaceholderText="1.0.0"`.
2. Test: the dialog's name box suggests `1.0.0`.

### Acceptance criteria

- The *Create a tag* dialog's name box shows `1.0.0` as its watermark. A name with a `v` is still
  accepted.
- Build clean with zero warnings; the whole suite green.

## PHASE02 — Pushing one tag to the remote

**Branch:** `feature/feature-a2a2-phase02-push-one-tag`
**Status:** TODO

### Steps

1. Core, `SyncService`:
   - `BuildPushTagArguments(remote, tag)` → `push --progress <remote> refs/tags/<tag>:refs/tags/<tag>`.
     It refuses an empty remote or tag, and a remote starting with `-`. Both sides are full ref names,
     so the tag cannot be read as an option, and there is no leading `+`: it is never forced.
   - `ISyncService.PushTagAsync(repository, remote, tag, progress, token)` runs it like every other
     transfer: signed in, errors mapped.
2. App, `ISyncOperations.PushTagAsync(string tag)`:
   - the remote is the one the current branch's upstream is on, else `origin` — the branch push's
     rule;
   - it goes through the push guard;
   - it shows the overlay "Pushing <tag>";
   - on success, "Pushed" — "<remote> has the tag \"<tag>\".";
   - a rejection (`NonFastForward`) is explained as "<remote> already has a tag \"<tag>\", on another
     commit. Nothing was replaced: delete it there first, or give this tag another name.";
   - every other failure keeps the mapper's message.
3. `AutoRefreshTests`' `ISyncOperations` double gets the new member.
4. Tests:
   - Core unit — the arguments; the refusals;
   - Core integration — a lightweight and an annotated tag each arrive on the bare remote, and only
     that tag (another local tag stays unpushed); a tag of the same name on another commit on the
     remote is rejected, as a `NonFastForward`, and left alone;
   - App — the tag arrives and the info bar says so; a profile with no integration for the remote
     pushes nothing and says why; a rejection gets the tag sentence.

### Acceptance criteria

- `ISyncOperations.PushTagAsync` pushes exactly one tag, lightweight or annotated, to the current
  branch's remote (else `origin`), under the push guard, with progress and a result on the info bar.
- A tag the remote already has elsewhere is never overwritten, and the reader is told why.
- Build clean with zero warnings; the whole suite green.

## PHASE03 — Push from the badge and the dialog

**Branch:** `feature/feature-a2a2-phase03-push-tag-menus`
**Status:** TODO

### Steps

1. `HistoryTagViewModel` gets an optional `Push` command (`AsyncRelayCommand<string>?`) and a
   `PushHeader` (`Push "<name>"`).
   - `HistoryRowCommands` carries it (`PushTag`), and `CommitRowViewModel` hands it to every tag badge.
   - `HistoryPageViewModel` wires it to `ISyncOperations.PushTagAsync`.
2. The history's tag badge menu (`HistoryPageView.axaml`):
   - `Push "<name>"` (`ArrowUp`) first;
   - a separator;
   - *Copy tag name*.
3. `TagRowViewModel` gets a `PushCommand`. `TagsPageViewModel` takes `ISyncOperations` and wires it.
4. The Tags dialog row menu (`TagsPageView.axaml`): *Push to the remote* (`ArrowUp`) after *Check out
   (detaches HEAD)*, before the separator and *Delete…*.
5. Tests:
   - the badge's menu offers the push with its icon, and running it pushes that tag to the bare
     remote;
   - the Tags dialog row's menu offers *Push to the remote*, and running it pushes that row's tag;
   - the menu icon tests still find an icon on every item.

### Acceptance criteria

- Right-clicking a tag badge in the history offers `Push "<name>"`. Right-clicking a tag in the Tags
  dialog offers *Push to the remote*. Both push that one tag through PHASE02's operation.
- Build clean with zero warnings; the whole suite green.

## Out of scope

- Choosing the remote per push (a submenu of remotes).
- Force-pushing a tag, or deleting a tag on the remote.
- Pushing every tag at once.
- Validating or rewriting tag names (a `v` prefix stays allowed).

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| Which remote | The current branch's upstream remote, else `origin` | The branch push's rule, so the two pushes agree. It needs no new question | A submenu per remote (heavier than asked); always `origin` |
| What is pushed | Only that tag, `refs/tags/<tag>:refs/tags/<tag>` | The reader asked for this tag. `--tags` would publish every local tag, and `--follow-tags` skips lightweight ones | `--tags`; `--follow-tags` |
| A tag already on the remote, elsewhere | Refuse, and explain | A moved tag breaks everyone who fetched it; git refuses it for that reason | Offering a force push |
| Confirm before pushing | No | Pushing a tag only adds to the remote, like the branch push, which is not confirmed either | A confirmation dialog |
| Badge menu order | Push, separator, copy | The branch badge's shape: actions first, copying last | Copy first |
| Dialog wording | *Push to the remote* | The dialog's other items name no tag ("Check out (detaches HEAD)", "Delete…") | `Push "<name>"` |
| Row button in the Tags dialog | None; context menu only | Asked for as a context menu item; the row's two buttons stay | A third row button |
| Placeholder | `1.0.0`, nothing else | The house tags are bare (`docs/RELEASE.md`) | Validating against a `v` prefix |
| Phases | Placeholder · engine and operation · menus | One reviewable commit each; the menus build on the operation | One dev for everything |
