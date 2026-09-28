# FEATURE-A2A2-PHASE03 — Push from the badge and the dialog

**Item:** FEATURE-A2A2 — Tags: bare placeholder, push from menus
**Branch:** `feature/feature-a2a2-phase03-push-tag-menus`
**Run:** bugfix/2026-09-28-history-tag-push-release

## Summary

A tag can be pushed from its context menu, in both places tags are shown. Both run PHASE02's
`ISyncOperations.PushTagAsync`: that one tag goes to where the current branch pushes (else `origin`),
under the push guard, with the overlay and a result on the info bar.

- **The history's tag badge.** Its menu has the branch badge's shape:
  - `Push "<name>"` (`ArrowUp`);
  - a separator;
  - *Copy tag name*.

  `HistoryTagViewModel` carries an optional `Push` command, with `CanPush` and `PushHeader`.
  `HistoryRowCommands.PushTag` hands the command to every tag badge on a line, and
  `HistoryPageViewModel` wires it.
- **The Tags dialog.** A row's menu offers *Push to the remote* (`ArrowUp`), right after *Check out
  (detaches HEAD)* and before the separator and *Delete…*. `TagRowViewModel` carries a `PushCommand`
  and `CanPush`; `TagsPageViewModel` now takes `ISyncOperations` and wires it through its usual busy
  `Run`.
- **This phase completes FEATURE-A2A2.**

## Files / modules touched

**Created**

- `docs/done/FEATURE-A2A2-PHASE03.md`

**Modified**

- `src/Enigma.GitClient.App/ViewModels/Pages/HistoryBranchViewModel.cs` — `HistoryTagViewModel.Push`,
  `CanPush`, `PushHeader`.
- `src/Enigma.GitClient.App/ViewModels/Pages/CommitRowViewModel.cs` — `HistoryRowCommands.PushTag`,
  handed to the tag badges.
- `src/Enigma.GitClient.App/ViewModels/Pages/HistoryPageViewModel.cs` — the command and its handler.
- `src/Enigma.GitClient.App/Views/Pages/HistoryPageView.axaml` — the badge menu's push item and
  separator.
- `src/Enigma.GitClient.App/ViewModels/Pages/TagsPageViewModel.cs` — `TagRowViewModel.PushCommand`
  and `CanPush`; the page's `PushCommand` over `ISyncOperations`; the constructor's missing parameter
  docs.
- `src/Enigma.GitClient.App/Views/Pages/TagsPageView.axaml` — the row menu's push item.
- `tests/Enigma.GitClient.App.UnitTests/HistoryCopyTests.cs` — the badge test becomes
  `ATagBadge_HasAMenuThatPushesTheTagAndCopiesItsName`, which checks:
  - the three items in order;
  - the push item's header, icon, command and parameter;
  - the copy still copies.
- `tests/Enigma.GitClient.App.UnitTests/RemotesAndSyncTests.cs`, against a bare remote:
  - `History_ATagBadgePushesThatTag` — an annotated tag arrives from its badge's command;
  - `TagsDialog_ARowsMenuPushesThatRowsTag` — the real row menu's items are in order, and its push
    item sends that row's tag and not the other one.
- `README.md` — the tag-management bullet names the push (documentation sweep).
- `docs/roadmap.md`, `docs/plan/FEATURE-A2A2.md` — statuses; the item is `DONE`.

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| Reload the history after a tag push | No | A tag has no remote-tracking reference, so nothing the graph draws moves. The exclusive run re-reads the repository's state anyway |
| How the dialog item decides it is shown | A `CanPush` flag on the row | The codebase's `Can…`/`Has…` idiom, rather than a null converter in the markup |
| Where the push-execution tests live | `RemotesAndSyncTests` | It builds the world with a bare remote that a real push needs |

## Deviations & follow-ups

- None. The plan's steps were implemented as written.
- Line endings: no CRLF churn.

## Documentation sweep

`README.md`, *Features*: "Tag management — create (lightweight or annotated) and delete" becomes
"…create (lightweight or annotated), delete, and push one tag to the remote from its badge in the
history or its line in the tags dialog". The release notes come with FEATURE-A349.

## Build/test evidence

- `dotnet build Enigma.GitClient.slnx`: 0 warnings, 0 errors.
- `dotnet test --solution Enigma.GitClient.slnx`: **2511 passed**, 0 failed, 0 skipped (2 new, 1
  rewritten). `MenuIconTests` confirms both new items carry a glyph.
- Fix budget: no fix cycle.
