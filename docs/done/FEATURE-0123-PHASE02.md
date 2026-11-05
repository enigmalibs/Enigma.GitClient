# FEATURE-0123-PHASE02 — The cloud on pushed tag badges

**Item:** FEATURE-0123 — Remote mark on tag badges
**Branch:** `feature/feature-0123-phase02-tag-cloud`
**Run:** feature/2026-11-05-history-badges-release

## Summary

A History tag badge now carries the remote's cloud when the remote has the tag on the same commit,
as a branch badge does when its upstream is on it. Its tooltip says `v1, on origin`. A tag that was
not pushed, or that the remote has on another commit, has no cloud.

- `Services/RemoteTags` (new `IRemoteTags`, a singleton) holds what the tag's push remote
  (`SyncOperations.TagRemote`) last said, through Core's `ListRemoteAsync`.
  - **When it asks:** once a repository has opened and its references are read; after every
    automatic or requested refresh whose fetch ran; and when asked.
  - **When it does not ask:** on a file-watcher refresh, or when the repository has no such remote.
  - **Answers it drops:** one overtaken by a newer read, or by another repository.
  - **A failed read:** logged at Debug. It keeps what the same remote said last, so a pushed tag
    stays pushed offline; a repository the remote never answered for stays unknown.
- `HistoryTagViewModel` is now an observable class with the same constructor. Its badge is replaced
  in place by `ShowRemote`. `CommitRowViewModel` takes a `remoteTagOf` when built, and
  `ShowRemoteTags` applies a new answer to its tag badges and its `Refs`.
- `HistoryPageViewModel` passes `RemoteOf` to the rows, applies `Changed` to every loaded line and
  re-measures the Refs column (the cloud widens a badge). It asks the remote again:
  - after its own tag push and remote tag delete succeed;
  - after the Tags or Remotes dialog closes.
- The tag template binds `Upstream`. `RefBadge.Description` reads `"<tag>, on <remote>"` for a tag.

## Files/modules touched

- Created:
  - `src/Enigma.GitClient.Desktop/Services/RemoteTags.cs`
  - `tests/Enigma.GitClient.Desktop.UnitTests/RemoteTagsTests.cs`
  - `docs/done/FEATURE-0123-PHASE02.md`
- Modified:
  - `src/Enigma.GitClient.Desktop/ViewModels/Pages/HistoryBranchViewModel.cs`: `HistoryTagViewModel`.
  - `src/Enigma.GitClient.Desktop/ViewModels/Pages/CommitRowViewModel.cs`: `remoteTagOf`,
    `ShowRemoteTags`, the `Upstream` documentation.
  - `src/Enigma.GitClient.Desktop/ViewModels/Pages/HistoryPageViewModel.cs`
  - `src/Enigma.GitClient.Desktop/Views/Pages/HistoryPageView.axaml`
  - `src/Enigma.GitClient.Desktop/Controls/RefBadge.cs`
  - `src/Enigma.GitClient.Desktop/DependencyInjection/ServiceCollectionExtensions.cs`
  - `docs/roadmap.md`, `docs/plan/FEATURE-0123.md`: statuses.

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| A failed read | Keep the same remote's last answer; forget another remote's | The plan said "leaves the state unknown". Keeping the last answer is what a remote-tracking branch does offline, and it avoids every cloud blinking off when the network drops. A first read that fails still leaves the state unknown |
| When "a repository opened" is acted on | At the first `StateRefreshed` after `RepositoryChanged` | `RepositoryChanged` fires before the references are read, and the tag remote follows the current branch's upstream |
| Does the remote exist? | Asked locally through `IRemoteReader` first | A repository with no `origin` (most tests, and fresh repositories) costs no network call and logs nothing |
| The Tags and Remotes dialogs | Re-ask after they close | Both can change what the remote has, or which remote it is |
| The tag's tooltip | `v1, on origin` | A branch's says `main and origin/main` because its upstream is a ref; a tag's remote is not a ref |

## Deviations & follow-ups

- The failure rule is the one in the decisions above, not "unknown on any failure".
- The toolbar's Fetch / Pull / Push (`MainWindowViewModel`) do not re-ask the remote. The next
  automatic refresh does (60 s by default). This could be a follow-up if the lag shows.
- The Tags page does not show the mark (out of scope).

## Build/test evidence

- Baseline: see `docs/done/FEATURE-D7D5-PHASE01.md`.
- `dotnet build Enigma.GitClient.slnx`: **0 warnings, 0 errors**.
- `dotnet test --solution Enigma.GitClient.slnx`, run twice: 3,626 total, 3,623 passed, 3 skipped,
  **0 failed** both times.
- New tests (`RemoteTagsTests`, 10, real git with a bare `origin`):
  - a pushed tag is on `origin` and an unpushed one is not;
  - a tag moved after its push is not;
  - an annotated tag is compared by its commit;
  - no remote → not asked, unknown;
  - a failing read is logged at Debug and leaves an unknown state;
  - a failing read keeps the same remote's last answer;
  - an overtaken answer for a closed repository is dropped;
  - pushing from the badge draws the cloud (`HasUpstream`, `UpstreamIcon` visible, tooltip
    `v1, on origin`), and deleting from the remote takes it away;
  - a tag already pushed has its cloud when the history opens, in the badge and in `Refs`;
  - the Refs column widens by the cloud.
- Fix budget: 0 cycles (a missing `using` in the new test file was fixed before the first test run).

## Documentation sweep

- README line 75 (tag management) stays true; nothing mentions the clouds. No edit.
