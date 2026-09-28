# FEATURE-A2A2-PHASE02 — Pushing one tag to the remote

**Item:** FEATURE-A2A2 — Tags: bare placeholder, push from menus
**Branch:** `feature/feature-a2a2-phase02-push-one-tag`
**Run:** bugfix/2026-09-28-history-tag-push-release

## Summary

One tag, lightweight or annotated, can be pushed on its own. This phase adds the engine and the
operation; PHASE03 puts them in the menus.

- **Core.** `ISyncService.PushTagAsync(repository, remote, tag, progress, token)` runs
  `git push --progress <remote> refs/tags/<tag>:refs/tags/<tag>`, built by the public
  `SyncService.BuildPushTagArguments`.
  - The tag is named in full on both sides, so it cannot be read as an option or as a branch of the
    same name.
  - There is no leading `+`, so it is never forced.
  - An empty remote or tag, and a remote starting with `-`, are refused.
  - It signs in and maps git's errors like every other transfer.
- **App.** `ISyncOperations.PushTagAsync(tag)` works out the remote the way a push of the current
  branch does: its upstream's remote, else `origin`. It then:
  - goes through the push guard;
  - shows the overlay "Pushing <tag>" with a working Cancel;
  - on success, says "Pushed" — "<remote> has the tag \"<tag>\".".
- **A tag the remote already has on another commit** is refused by git and never replaced. git's
  message ("Updates were rejected…") maps to `NonFastForward`, whose sentence says to pull first,
  which is wrong here. The operation says instead: "<remote> already has a tag \"<tag>\", on another
  commit. Nothing was replaced: delete it there first, or give this tag another name." It is a warning,
  like every failure the reader can fix. Every other failure keeps the mapper's message.

## Files / modules touched

**Created**

- `docs/done/FEATURE-A2A2-PHASE02.md`

**Modified**

- `src/Enigma.GitClient.Core/Sync/SyncService.cs` — `PushTagAsync` on the interface and the service;
  `BuildPushTagArguments`.
- `src/Enigma.GitClient.App/Services/SyncOperations.cs` — `PushTagAsync` on the interface and the
  service.
- `tests/Enigma.GitClient.Core.UnitTests/Sync/SyncParsingTests.cs` — the exact arguments, with no `+`,
  no force and no `--tags`/`--follow-tags`; four refusals (empty remote, empty tag, blank remote, a
  remote that is an option).
- `tests/Enigma.GitClient.Core.IntegrationTests/Sync/SyncServiceTests.cs`, against a bare remote:
  - a lightweight tag arrives, and another local tag does not;
  - an annotated tag arrives as the tag object;
  - a tag of that name on another commit is rejected as `NonFastForward` and left alone;
  - pushing a tag the remote already has is not a failure.
- `tests/Enigma.GitClient.App.UnitTests/RemotesAndSyncTests.cs`:
  - the tag arrives alone, and the info bar says where;
  - with `main` tracking a second remote, the tag goes there and not to `origin`;
  - under a profile with no integration for the remote, nothing is pushed and the refusal is shown;
  - a rejection gets the tag sentence, as a warning, and the remote's tag is unchanged.
- `tests/Enigma.GitClient.App.UnitTests/AutoRefreshTests.cs` — the `ISyncOperations` double gets the
  member.
- `docs/roadmap.md`, `docs/plan/FEATURE-A2A2.md` — statuses.

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| A new method, or a tag field on `PushRequest` | A method of its own, `PushTagAsync` | `PushRequest` is about a branch (upstream, lease, delete, follow tags), and none of that applies to a tag; a field would make combinations that mean nothing |
| The current branch's upstream when it is gone | Ignored, so the push goes to `origin` | `PushBranchAsync` treats a gone upstream the same way |
| The success message | "<remote> has the tag \"<tag>\"." | It names where the tag went, which the menu item does not say |

## Deviations & follow-ups

- None. The plan's steps were implemented as written.
- Line endings: no CRLF churn.

## Documentation sweep

Nothing user-facing changes until PHASE03 puts the push in the menus; the README's tag bullet is
updated there. README's "Every fetch, pull and push — a tag's or a branch's — signs in" stays true. No
edit.

## Build/test evidence

- `dotnet build Enigma.GitClient.slnx`: 0 warnings, 0 errors.
- `dotnet test --solution Enigma.GitClient.slnx`: **2509 passed**, 0 failed, 0 skipped (13 new: 5 Core
  unit, 4 Core integration, 4 App).
- Fix budget: no fix cycle.
