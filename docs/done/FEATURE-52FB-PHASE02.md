# FEATURE-52FB-PHASE02 — Open, init & clone repositories

**Item:** FEATURE-52FB — App shell & repository opening
**Branch:** `feature/feature-52fb-phase02-open-clone`
**Run:** feature/2026-09-15-enigma-git-client

## Summary

The client can now be pointed at a repository: open one you have, clone one with live progress and a
working cancel, or create one. A versioned recent-repositories list remembers what has been opened,
survives a restart, and never silently loses an entry. Opening anything publishes it to the whole
shell and moves to the history.

With this phase FEATURE-52FB is complete.

## Files / modules touched

**Created — `src/Enigma.GitClient.Core/`**

- `Configuration/AppPaths.cs` — `IAppPaths`; the per-user configuration directory, created `0700`
  on Unix because it will hold encrypted tokens
- `Repositories/CloneRequest.cs` — the request, `CloneStage`, `CloneProgress`, and the
  directory-name derivation git itself uses
- `Repositories/CloneProgressParser.cs` — git's progress lines into reports
- `Repositories/RemoteUrlValidator.cs` — what git can and cannot clone from
- `Repositories/IRepositoryService.cs`, `RepositoryService.cs` — open, init, clone

**Modified — Core**

- `Git/IGitProcessRunner.cs`, `Git/GitProcessRunner.cs` — added `RunStreamingAsync`, which reports
  each chunk git writes to standard error **split on `\r` as well as `\n`**, because git rewrites its
  progress line in place and a newline-only reader reports nothing until the clone is over
- `DependencyInjection/ServiceCollectionExtensions.cs` — registers `IAppPaths`, `IRepositoryService`

**Created — `src/Enigma.GitClient.App/`**

- `Services/RecentRepositoryStore.cs` — `RecentRepository`, `IRecentRepositoryStore` and the
  versioned JSON store
- `Navigation/ShellNavigation.cs` — `ShellPage`, `IShellNavigation`, `ShellNavigation`
- `Controls/ProgressOverlayCard.cs` + its theme — the card every long operation shows
- `ViewModels/Dialogs/CloneRepositoryDialogViewModel.cs`, `InitRepositoryDialogViewModel.cs`
- `Views/Dialogs/CloneRepositoryDialogView.axaml(.cs)`, `InitRepositoryDialogView.axaml(.cs)`
- `ViewModels/Pages/RepositoriesPageViewModel.cs`, `Views/Pages/RepositoriesPageView.axaml(.cs)`

**Modified — App**

- `ViewModels/MainWindowViewModel.cs` — consumes `IShellNavigation` instead of building the rail
- `DependencyInjection/ServiceCollectionExtensions.cs`, `Themes/Controls.axaml`

**Created / modified — tests**

- Unit: `Repositories/RepositoryCreationTests.cs` (46 cases across clone requests, URL validation
  and progress parsing)
- Integration: `Repositories/RepositoryServiceTests.cs` (14) — real init, real clone, real failure
  and cancellation clean-up, and the streaming runner
- App: `RecentRepositoryStoreTests.cs` (17), `RepositoriesPageTests.cs` (14, including an off-screen
  render of the page), `Infrastructure/TestServices.cs`, `Infrastructure/UiServiceDoubles.cs`,
  `Infrastructure/HeadlessAvaloniaFixture.RunAsync`

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| Order of the URL checks | Local path, then scp syntax, then URI | Found by testing, and neither trap is obvious. A URI scheme may legally contain dots, so `gitlab.example.com:group/project.git` parses as a URI whose *scheme* is `gitlab.example.com`; and on Unix an absolute path parses as a `file:` URI, so `/tmp/does-not-exist` was being accepted without ever checking that it exists. Both now have tests |
| A `file:` URL | Held to the same existence check as a bare path | It names a directory on this machine; accepting one that is not there only moves the failure into git |
| Progress chunking | Split on `\r` **and** `\n` | git's `--progress` output rewrites one line in place. A newline-only reader shows a frozen dialog for the whole clone and then jumps to 100% |
| A clone that fails or is cancelled | The target directory is removed | A half-written clone is worse than none: the user has to find and delete it before they can retry. Two integration tests assert nothing is left behind |
| Where the rail lives | Moved out of `MainWindowViewModel` into `IShellNavigation` | A page has to be able to say "now show the history"; reaching back into the window's ViewModel to do it would be a cycle in the object graph as well as in the design |
| Selecting the first page | An explicit `Start()`, never from the constructor | Found by a test that counted ten rail items instead of five: selecting a page **builds** it, and a page's ViewModel depends on `IShellNavigation` — so navigating from that service's own constructor resolved the singleton a second time and built the rail twice |
| Recent list capacity | 20, and the cap never drops a pinned entry | A cap that can evict something the user deliberately pinned is a cap that loses their data |
| A corrupt recent file | Moved to `.corrupt` and the list starts empty | Never block startup on a file the user can delete — and never destroy it either, in case it can be salvaged |
| A file from a newer schema version | Read anyway, and logged | The entries this build understands are still the best available answer; it is only rewritten when the user changes something |
| Dialog validation | The ViewModel raises `ValidationChanged`; the page enables the dialog's primary button | `ContentDialog` has no binding surface for its buttons, so the page wires it explicitly rather than letting the user press Clone on a URL that cannot work |
| Directory-name suggestion | Follows the URL until the user types one | Matches what git does, without overwriting a name the user chose |
| Test doubles for the three host-driven UI services | Recording doubles rather than real hosts | The real dialog, overlay and info-bar services drive animated controls and throw until a host is registered; hosting them outside a live visual tree made the suite hang. The doubles also turn "did the page report the failure?" and "was the overlay hidden again?" into direct assertions — both of which are now tested |

## Deviations & follow-ups

- **Deviation:** the plan called this the "Welcome page". It is implemented as a first-class
  **Repositories** page on the rail, reachable at any time rather than only at startup — the same
  content, but it does not disappear once a repository is open.
- **Deviation (additive):** `IAppPaths`, `IShellNavigation`, `ProgressOverlayCard` and
  `RunStreamingAsync` were not named in the plan. Each is load-bearing for what the plan did ask for,
  and `IAppPaths` is also what FEATURE-22C0's token store will use.
- **Follow-up:** `RecentRepository` does not yet show each repository's current branch, which the
  plan mentions. Doing it means running git per entry on a page that must open instantly; it belongs
  with a background refresh, recorded for the settings/polish item.
- **Follow-up:** the clone dialog has no "browse for a URL from a connected account" button yet —
  that arrives with FEATURE-22C0, which is also where the Integrations page gets its content.
- **Line endings (recommendation only):** no CRLF churn observed. No action taken.

## Build/test evidence

```
dotnet build Enigma.GitClient.slnx --no-incremental
  0 Warning(s)
  0 Error(s)

dotnet test --solution Enigma.GitClient.slnx
  Test run summary: Passed!
  total: 604  failed: 0  succeeded: 604  skipped: 0
```

The build was run with `--no-incremental` deliberately: `AVLN*` XAML warnings are produced by an
MSBuild task that an incremental build skips, so a warning count read from an up-to-date build is not
evidence. Read that way, this dev did carry three `AVLN5001` warnings (`TextBox.Watermark` is obsolete
in Avalonia 12, superseded by `PlaceholderText`); they are fixed.

**Fix cycles.** The Definition-of-Done gate — a full rebuild plus the whole solution suite — failed
once, on the two URL-validation defects described above, and was green on the next run. The
per-project runs used while authoring are not counted as gate cycles; they surfaced four further
issues (a shallow-clone assertion that ignored git's local-clone behaviour, two rail expectations
that the new page changed, the rail-built-twice defect, and the hanging UI services), each fixed
before the gate was run.
