# FEATURE-0123 — Remote mark on tag badges

**Status:** IN PROGRESS
**Type:** FEATURE
**Branch:** one per phase, see below
**Run:** feature/2026-11-05-history-badges-release

## Objective

A tag badge of the History carries the same cloud a branch badge carries when its upstream is on the
same commit: the cloud says the remote has the tag, on that commit. A tag that was not pushed, or that
the remote has on another commit, has no cloud. The tooltip says where it is (`v1.0, on origin`).

## Context & constraints

- **The branch's mark:** `RefBadge.Upstream` (non-empty → `HasUpstream` → the `UpstreamIcon`,
  `CloudArrowDown`, after the kind icon), and the tooltip `Description` `"main and origin/main"`.
  `RefBadgeItem.Upstream` feeds it; `RefBadgeMetrics` counts the icon through `HasUpstream`.
- **git has no remote-tracking tags.** Whether a remote has a tag is only known by asking it:
  `git ls-remote --tags <remote>` prints `<sha>\trefs/tags/<name>` and, for an annotated tag, a
  second `<sha>\trefs/tags/<name>^{}` with the commit it peels to.
- **The remote a tag goes to:** `SyncOperations.TagRemote(refs)`: the current branch's push remote,
  else `origin`. The tag badge's push and remote delete already use it.
- **Network calls with credentials:** `TagService.RunOnRemoteAsync` resolves them through
  `IGitCredentialResolver.ForRepositoryAsync` and `WithCredentials`.
- **Refreshes:** `IAutoRefreshService.Refreshed` (`AutoRefreshResult.Fetch == Fetched` after a
  periodic or requested refresh; every 60 s by default); `IRepositoryContext.RepositoryChanged` when a
  repository opens. The file watcher's local refreshes never touch the network.
- **The tag badge VM** `HistoryTagViewModel` is an immutable record built once per row
  (`CommitRowViewModel.BuildBranches`); the rows are built before any remote answer can arrive.
- `GitTag.TargetSha` is the commit a tag points at (peeled).

## PHASE01 — Read a remote's tags in Core

**Branch:** `feature/feature-0123-phase01-remote-tags-core`
**Status:** DONE

### Steps

1. `ITagService.ListRemoteAsync(RepositoryHandle repository, string remote, CancellationToken
   cancellationToken = default)` → `Task<IReadOnlyDictionary<string, string>>`: each tag the remote
   has, by name, with the commit it points at.
   - Runs `git ls-remote --tags <remote>` with the repository's credentials (as the push does).
   - Parses each `<sha>\t<ref>` line: only `refs/tags/…`; a `^{}` line overrides the tag's sha with the
     peeled one; malformed lines are skipped. Names compare ordinally.
   - A failure throws `GitCommandException`, as the other remote calls do; the caller decides.
2. The parsing is a `static` method (`RemoteTagParser.Parse(string output)`), tested on its own.
3. Every implementation and test double of `ITagService` gets the member.
4. Tests:
   - unit: lightweight and annotated lines, the peeled line overriding, a non-tag ref and a malformed
     line ignored, an empty output;
   - integration (`TagAndCheckoutServiceTests`, real git and a bare remote): a pushed lightweight tag
     and a pushed annotated tag are listed at their commit; an unpushed tag is not; an unknown remote
     throws;
   - `SignedInOperationsTests`: the call carries the repository's credentials, like the push.

### Acceptance criteria

- `ListRemoteAsync` returns the remote's tags with their commits, annotated ones peeled.
- The build has zero warnings; the whole suite is green against the baseline.

## PHASE02 — The cloud on pushed tag badges

**Branch:** `feature/feature-0123-phase02-tag-cloud`
**Status:** TODO

### Steps

1. `Services/RemoteTags.cs` (new), `IRemoteTags`, a singleton:
   - `string? Remote` and `IReadOnlyDictionary<string, string>? Tags` — what the remote last said,
     `null` while unknown;
   - `string? RemoteOf(string tag, string commit)` → the remote's name when it has that tag on that
     commit, else `null`;
   - `Task RefreshAsync()` reads `TagRemote(context.Refs)` through `ITagService.ListRemoteAsync`, off
     the UI thread's work, with the repository lifetime's token; a result for a repository that is no
     longer the open one is dropped; a failure is logged at Debug and leaves the state unknown;
   - `event EventHandler? Changed`, raised when the answer changed;
   - it refreshes when a repository opens (`RepositoryChanged`) and after every
     `IAutoRefreshService.Refreshed` whose fetch ran (`Fetched`), and forgets everything when the
     repository closes. A repository with no remote is not asked.
   - registered in `ServiceCollectionExtensions` and the test service builder.
2. `HistoryTagViewModel` becomes an observable class with the same constructor (plus an optional
   remote): its `Badge` is replaced by `Badge with { Upstream = remote }` when the remote state changes,
   raising `PropertyChanged`.
   - `RefBadgeItem.Upstream`'s documentation covers the tag case (the remote's name).
   - `RefBadge.Description` for a tag with a remote: `"v1.0, on origin"`.
3. `CommitRowViewModel`: an optional `Func<string, string, string?> remoteTagOf` (tag, commit →
   remote), applied when the row is built, and a method that applies a new one to the row's tag
   badges. `HistoryPageViewModel` passes `_remoteTags.RemoteOf` and, on `Changed`, applies it to every
   loaded row and re-measures the Refs column (the cloud widens a badge).
4. `HistoryPageView.axaml`, the tag template: `Upstream="{Binding Badge.Upstream}"`.
5. After the history's own tag push and remote tag delete succeed, `HistoryPageViewModel` calls
   `_remoteTags.RefreshAsync()`.
6. Tests (headless, real git, a bare remote):
   - `RemoteTags`: an unpushed tag → no remote; pushed → the remote's name; pushed then moved locally
     → none; a failing read leaves the state unknown and logs; a stale repository's answer is dropped;
   - History: after pushing `v1` from its badge, the badge draws the cloud (`HasUpstream`, the
     `UpstreamIcon` visible) and its tooltip says `v1, on origin`; an unpushed tag's badge has no
     cloud; deleting it from the remote takes the cloud away;
   - the Refs column is re-measured when a cloud appears.
7. Documentation sweep (§7).

### Acceptance criteria

- A tag the remote has on the same commit shows the cloud; any other tag does not.
- The mark follows the reader's own push and remote delete, the repository opening, and every
  successful automatic or requested refresh. A remote that cannot be reached shows no mark and no
  error.
- No network call on the file watcher's refreshes.
- The build has zero warnings; the whole suite is green against the baseline.

## Out of scope

- The same mark on the Tags page.
- Tags on remotes other than the one the tag push goes to.
- Telling "not pushed" apart from "unknown" visually.

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| How to know a tag is pushed | `git ls-remote --tags` against the tag's push remote | git keeps no remote-tracking tags; this is the one read that answers it | Inferring from the fetch: a fetched tag is local like any other |
| What "on the remote" means | The remote has the tag on the same commit | It is what the branch's cloud means (upstream on the same commit); a tag moved after pushing is not pushed | Name only |
| When to ask | Repository opened, after each refresh whose fetch ran, after the reader's own tag push / remote delete | Only when the network was just reachable or the remote just changed; never on file-watcher refreshes | Every history load: the watcher reloads often and offline |
| A failed read | No mark, Debug log, no notification | Nobody asked for it, as the quiet fetch | An info bar: an offline laptop would see it every minute |
| Which remote | `SyncOperations.TagRemote` | The tag's push and delete use it, so the mark and the actions agree | Every remote |
| The mark | The branch's cloud (`Upstream` → `UpstreamIcon`) | "The same indication", as asked; `RefBadgeMetrics` already counts it | A new icon |
| Updating badges already drawn | Observable tag VM, updated in place | No reload of the history when the answer arrives | Reloading the history |
| Breakdown | Core, then Desktop | Two reviews: a git read with its parser, then the state and the UI | One phase |
