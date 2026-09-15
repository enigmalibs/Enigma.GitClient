# FEATURE-06FE-PHASE01 — Fetch, pull, push & remotes

**Item:** FEATURE-06FE — Remotes & synchronisation
**Branch:** `feature/feature-06fe-phase01-sync`
**Run:** feature/2026-09-15-enigma-git-client

## Summary

The client talks to remotes. The shell's toolbar fetches, pulls and pushes with the ahead and behind
counters that say whether either is worth doing, showing real progress with a cancel that works. A
remotes page lists what the repository knows about — name, URL, host, tracking-branch count — and
adds, edits, removes and fetches from them.

Underneath, `pull` always carries `--no-rebase`, force is always `--force-with-lease`, and the six
failures that actually happen — no credentials, an unknown host key, a rejected push, a stale lease,
no upstream, no network — each turn into a sentence that says what to do next instead of git's own
diagnostic.

## Files / modules touched

**Created — Core**

- `Sync/SyncProgress.cs` — `SyncStage`, `SyncProgress` and the parser over git's `--progress` lines
- `Sync/SyncFailure.cs` — `SyncFailureKind`, `SyncFailure`, `SyncException` and the classifier
- `Sync/SyncService.cs` — `PushRequest`, `PullStrategy`, `ISyncService` and `IRemoteService`

**Created — App**

- `Services/SyncOperations.cs` — the progress overlay, the cancel, and the report afterwards
- `ViewModels/Dialogs/RemoteDialogViewModel.cs` — one dialog for adding and editing
- `Views/Dialogs/RemoteDialogView.axaml` (+ code-behind)

**Rewritten**

- `ViewModels/Pages/RemotesPageViewModel.cs` — `RemoteRowViewModel` and the page
- `Views/Pages/RemotesPageView.axaml` — the list and the per-row actions

**Modified**

- `ViewModels/MainWindowViewModel.cs` — the fetch, pull and push commands and their counters
- `Views/MainWindow.axaml` — the synchronise toolbar
- `DependencyInjection/ServiceCollectionExtensions.cs` (both projects)
- `docs/roadmap.md`, `docs/plan/FEATURE-06FE.md`

**Created — tests**

- `tests/Enigma.GitClient.Core.UnitTests/Sync/SyncParsingTests.cs` — 37 cases over captured progress
  transcripts, captured failure transcripts, and every argument vector
- `tests/Enigma.GitClient.Core.IntegrationTests/Sync/SyncServiceTests.cs` — 21 cases against a
  second local directory: fetch, prune, pull, merge, push, rejection, lease and delete
- `tests/Enigma.GitClient.App.UnitTests/RemotesAndSyncTests.cs` — 17 cases on the page, the dialog,
  the toolbar's counters and a rendered snapshot

**Modified — tests**

- `tests/Enigma.GitClient.Core.UnitTests/Git/ForbiddenGitOperationsTests.cs` — a scan of the shipped
  source for the forbidden verb

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| `pull` and rebase | `--no-rebase` on every invocation, always | A repository configured with `pull.rebase=true` would otherwise rebase behind the user's back — and the command factory refuses the verb, so the alternative is not a rebase but a failure nobody can explain |
| Guarding that | A test that scans the shipped source for a quoted `"rebase"` | The runtime refusal is the real guard, but a refusal a user has to trigger is a failure nobody sees until a user does |
| Force pushing | `--force-with-lease`, never a bare `--force` | The lease refuses to clobber work that arrived since the last fetch, which is the exact case a bare force destroys — and an integration test drives both halves of it |
| Unrecognised progress lines | Passed through with their text intact | git says useful things that are not progress; swallowing them leaves a user watching a bar that never explains itself |
| Unrecognised failures | git's own first diagnostic line, verbatim | A wrong explanation is worse than git's own. Only the six failures that actually happen get a rewrite |
| A stale lease against a plain rejection | Separate kinds with separate messages | They read the same to anyone who sees only "rejected", and the thing to do about them is completely different |
| Severity of a failure | Warning when the user can fix it themselves, error otherwise | "Pull first" is not the same kind of event as "your credentials were refused" |
| Add and edit a remote | One dialog | They ask exactly the same three questions, and a second dialog differing only in its title is a second place for a rule to drift |
| An empty push URL | Means "push where you fetch from" | git has no way to unset a push URL and leave the remote usable, so the fetch URL is written into it |
| Refreshing a list | Read first, replace after | Found by a test: clearing before the await let a second refresh — and one arrives on every state change — interleave and list every remote twice |

## Deviations & follow-ups

- **Deviation:** the plan's step 5 — a pull that stops on conflicts handing off to the conflict state
  — is classified here (`SyncFailureKind.MergeConflict`, tested against a real conflicting pull) but
  has nowhere to hand off to yet. FEATURE-6DCC builds that screen and will read this kind.
- **Deviation:** `--ff-only` is modelled as `PullStrategy.FastForwardOnly` and tested, but no
  setting exposes it: the settings store is FEATURE-5D77's. The toolbar pulls with a merge.
- **Follow-up:** the progress overlay shows one stage at a time. A transfer that reports bytes as
  well as objects could show a rate; git gives it and the parser keeps the line, so it is a view
  change rather than a plumbing one.
- **Follow-up:** credentials are entirely the system's, by design. If a user has no helper
  configured, the failure says so clearly, but the client cannot offer to store one — which is the
  decision the plan already recorded.
- **Line endings (recommendation only):** no CRLF churn observed. No action taken.

## Build/test evidence

```
dotnet build Enigma.GitClient.slnx --no-incremental
  0 Warning(s)
  0 Error(s)

dotnet test --solution Enigma.GitClient.slnx
  Test run summary: Passed!
  total: 1113  failed: 0  succeeded: 1113  skipped: 0
```

76 tests are new in this dev. Both Core suites passed on their first run. Two defects were found by
the App tests: a bare `git init` picks its default branch from the machine's configuration, so the
test's own bare remote had to be pointed at `main` before a clone of it checked anything out; and the
remotes page listed every remote twice, because it cleared its list before awaiting the read and a
second refresh arrived in the gap. The page is written to `snapshots/remotes-page.png`, and the
toolbar to `snapshots/main-window-repository-open.png`.
