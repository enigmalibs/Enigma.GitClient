# FEATURE-6149 — Delete tags locally and on the remote

**Item:** FEATURE-6149 — Delete tags locally and on the remote
**Branch:** `feature/feature-6149-delete-tags`
**Run:** feature/2026-10-02-tags-branches-release

## Summary

A tag can now be deleted here or on the remote, from both of its context menus:

- **the history's tag badge:** *Push "x"* · separator · *Delete "x" locally…* · *Delete "x" from the
  remote…* · separator · *Copy tag name*;
- **the Tags dialog's row:** *Delete…* is now *Delete locally…*, followed by *Delete from the remote…*.
  The row's trash button still deletes locally.

Both deletes ask first, with *Cancel* as the default button:

- **Local** (*Delete tag*): this was already the Tags dialog's question. The history reloads afterwards,
  so the badge goes.
- **Remote** (*Delete remote tag*): *Delete the tag "x" from "origin"? This changes the remote for
  everyone who uses it. The tag here is kept.* On success the info bar says *Deleted from the remote* ·
  *"origin" no longer has the tag "x".*, because nothing on screen changes (a remote's tags are not
  drawn).

The remote delete goes to the same remote as the tag push: the one the current branch pushes to, or
`origin`. It obeys the profile push guard, refusing before anything is asked, with the push's own
explanation. It runs the existing, tested `ITagService.DeleteRemoteAsync`, which deletes
`refs/tags/<name>` by its full name so a branch of that name is never hit.

## Files / modules touched

**Modified**

- `src/Enigma.GitClient.Desktop/Services/SyncOperations.cs`: the tag's remote rule extracted to
  `internal static TagRemote(RefCollection)`, used by `PushTagAsync` and the remote delete.
- `src/Enigma.GitClient.Desktop/Services/TagOperations.cs`: `ITagOperations.DeleteRemoteAsync`, with
  `IPushGuard` injected and a private `MayPushAsync`.
- `src/Enigma.GitClient.Desktop/ViewModels/Pages/HistoryBranchViewModel.cs`: `HistoryTagViewModel`
  gains `Delete`/`DeleteRemote`, their headers, and the separator flags.
- `src/Enigma.GitClient.Desktop/ViewModels/Pages/CommitRowViewModel.cs`: `HistoryRowCommands` gains
  `DeleteTag`/`DeleteRemoteTag`. `BuildBranches` now takes the row's commands.
- `src/Enigma.GitClient.Desktop/ViewModels/Pages/HistoryPageViewModel.cs`: the two commands, with a
  reload after a local delete.
- `src/Enigma.GitClient.Desktop/ViewModels/Pages/TagsPageViewModel.cs`: `DeleteRemoteCommand` on the
  page and on `TagRowViewModel`.
- `src/Enigma.GitClient.Desktop/Views/Pages/HistoryPageView.axaml`: the tag badge's menu.
- `src/Enigma.GitClient.Desktop/Views/Pages/TagsPageView.axaml`: the row's menu.
- `tests/Enigma.GitClient.Desktop.UnitTests/RemotesAndSyncTests.cs`: 8 new tests. The pinned header
  list of the Tags row menu is updated.
- `tests/Enigma.GitClient.Desktop.UnitTests/HistoryCopyTests.cs`: the tag badge's pinned menu shape
  (3 items → 6), renamed `ATagBadge_HasAMenuThatPushesDeletesAndCopiesTheTag`.
- `README.md`: the *Tag management* feature bullet.
- `docs/roadmap.md`, `docs/plan/FEATURE-6149.md`: statuses.

**Created**

- `docs/done/FEATURE-6149.md`

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| The guard or the question first | The guard | A profile that may not push there gets the explanation, not a question it cannot act on |
| The remote delete's icon | `CloudX`, beside `Trash` for the local one | The two items must be told apart at a glance; every menu item carries a glyph (`MenuIconTests`) |
| The separators | Shown only between groups that are both present (`SeparatesPushFromDeletes`, `HasActions`) | The badge's menu never opens on a dangling separator, whichever commands a row was built with |
| A tag the remote does not have | Not a failure | git 2.56 warns *deleting a non-existent ref* and succeeds, and the remote ends up without the tag, which is what was asked. The failure test uses a remote git cannot reach instead |
| Where the new tests live | `RemotesAndSyncTests`, beside the tag push tests | They need its two-clone world and its work-profile helper |

## Deviations & follow-ups

- None from the plan.
- Follow-up suggestion: the remote branch delete (`BranchOperations.DeleteRemoteAsync`) still skips
  the push guard that the remote tag delete now honours.
- Line endings: no CRLF churn; the touched files are LF.

## Documentation sweep

- `README.md`, the *Tag management* bullet: it listed *delete* without saying where or which copy. It
  now says a tag is deleted here or on the remote, from the badge or the dialog's line.
- No `CLAUDE.md` or `AGENTS.md` exists. `RELEASENOTES.md` is FEATURE-4D5A's.

## Build/test evidence

- `dotnet build Enigma.GitClient.slnx`: 0 warnings, 0 errors.
- `dotnet test --solution Enigma.GitClient.slnx`: **2655 passed**, 0 failed, 0 skipped (2647 before;
  8 new).
- New tests: the badge's menu shape; a local delete (asks, removes only the local tag, the badge goes);
  a remote delete (asks, names the remote, removes only the remote's tag, reports); Cancel deletes
  nothing; the remote follows the current branch's upstream; the push guard refuses; git's failure is
  reported; the Tags row deletes its own tag from the remote.
- Fix budget: 2 cycles used.
  1. A test expected a failure for a tag the remote lacks; git 2.56 succeeds. The test was replaced by
     a deterministic failure.
  2. `HistoryCopyTests` pinned the badge's old 3-item menu. It was updated to the new 6-item shape.
