# FEATURE-6149 — Delete tags locally and on the remote

**Status:** TODO
**Type:** FEATURE
**Branch:** `feature/feature-6149-delete-tags`
**Run:** feature/2026-10-02-tags-branches-release

## Objective

A tag can be deleted **here** and **on the remote**, from both of a tag's context menus:

- the tag badge on a history line;
- the tag's line in the Tags dialog.

## Context & constraints

- **The engine already does both.** `Core/Tags/TagService.cs`:
  - `DeleteAsync(repository, name)` runs `git tag --delete <name>`;
  - `DeleteRemoteAsync(repository, remote, name)` runs `git push <remote> --delete refs/tags/<name>`
    (the full ref, so a branch of the same name is never hit), signed in through
    `IGitCredentialResolver`. Both are covered by `TagAndCheckoutServiceTests`.
- **The operation.** `Desktop/Services/TagOperations.cs` (`ITagOperations`) has `CreateAsync` and
  `DeleteAsync` (local, after a confirmation whose default button is the harmless one). Nothing in the
  UI deletes a tag from a remote yet.
- **Which remote.** `SyncOperations.PushTagAsync` pushes a tag to the remote the current branch pushes
  to (its upstream's remote, when the upstream is not gone), or `origin`. It checks the profile's push
  guard first (`MayPushAsync`, `IPushGuard`) and explains a refusal with `DescribeRefusal`. A remote
  delete is a push, so it obeys the same rule and the same guard.
- **The menus.**
  - History: `HistoryTagViewModel(RefBadgeItem Badge, Copy, Push?)`, built in
    `CommitRowViewModel.BuildBranches` from `HistoryRowCommands.PushTag`/`Copy`; its menu in
    `HistoryPageView.axaml` reads *Push "x"* · separator · *Copy tag name*.
  - Tags dialog: `TagRowViewModel`, menu in `TagsPageView.axaml`: *Select in the history* · *Check out
    (detaches HEAD)* · *Push to the remote* · *Delete…*; the row's trash button deletes locally.
- `AutoRefreshTests` has a hand-written `ISyncOperations` double: `ISyncOperations` is not widened.

## Steps

1. `SyncOperations`: extract the "which remote does a tag go to" rule into an `internal static`
   helper, used by `PushTagAsync` and by the remote delete.
2. `ITagOperations.DeleteRemoteAsync(string name)`, in `TagOperations`:
   - resolve the remote by that rule;
   - check the push guard (inject `IPushGuard`), and report a refusal with `DescribeRefusal`;
   - confirm: `Delete the tag "x" from "origin"? This changes the remote for everyone who uses it.
     The tag here is kept.` — *Delete* / *Cancel*, Cancel the default;
   - run `ITagService.DeleteRemoteAsync` under the repository lock; report success
     (`Deleted from the remote` · `"origin" no longer has the tag "x".`) and failures (git's first
     line) on the info bar, as the local delete does.
3. History: `HistoryRowCommands` gains `DeleteTag` and `DeleteRemoteTag` (`AsyncRelayCommand<string>`);
   `HistoryTagViewModel` carries them; the badge menu becomes *Push "x"* · separator · *Delete "x"
   locally…* · *Delete "x" from the remote…* · separator · *Copy tag name*. A local delete reloads the
   history (the badge goes); a remote one moves nothing the graph draws.
4. Tags dialog: `TagRowViewModel`/`TagsPageViewModel` gain `DeleteRemoteCommand`; the row menu's
   *Delete…* becomes *Delete locally…*, followed by *Delete from the remote…*.
5. Tests (headless, real git, a bare `origin`): both menus offer both items; a local delete asks and
   removes only the local tag; a remote delete asks, removes only the remote's tag and keeps the local
   one; Cancel deletes nothing; the remote is the current branch's upstream's; a profile that may not
   push there deletes nothing.

## Acceptance criteria

- The history tag badge's menu and the Tags dialog row's menu both offer a local and a remote delete.
- Each asks first, with Cancel as the default, and does exactly what it named.
- The remote delete goes to the remote the tag push goes to, and obeys the push guard.
- Build clean (zero warnings); whole suite green, new tests included.

## Out of scope

- Choosing among several remotes in the menu (the push's rule decides, as for a push).
- Listing which tags a remote has.
- A remote delete from the history's line menu (the badge is the tag's menu).

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| Where the two deletes appear | The history tag badge's menu and the Tags dialog row's menu | "The tags context menu" is both: the badge is a tag's menu in the graph, the row is its menu in the dialog | Only the dialog; also the line's menu |
| Which remote | The one the current branch pushes to, else `origin` | It is the rule the tag push already follows, so push and delete always talk to the same remote | A submenu per remote (heavier, and unlike the push); always `origin` |
| The push guard | Applies to the remote delete | A remote delete is a push; "this profile never pushes there" is a promise | Skipping it as the remote branch delete does |
| Confirmation | Both deletes ask; Cancel is the default | The house rule for every delete; a remote delete changes it for everyone | Deleting locally without asking |
| Where the remote delete lives | `ITagOperations.DeleteRemoteAsync`, reusing `ITagService.DeleteRemoteAsync` | The tag operations own the tag questions; the engine method exists and is tested; `ISyncOperations` has a hand-written test double | A new `ISyncService` method duplicating the engine's |
| Progress overlay | None | A ref deletion is one short round trip, like the remote branch delete, which shows none | The sync overlay |
| Menu wording | *Delete "x" locally…* / *Delete "x" from the remote…* (badge); *Delete locally…* / *Delete from the remote…* (row) | Says exactly which copy goes; the confirmation names the remote | Naming the remote in the menu header (it can change while the line is on screen) |
