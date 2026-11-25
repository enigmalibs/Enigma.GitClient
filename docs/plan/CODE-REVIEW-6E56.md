# CODE-REVIEW-6E56 — Fixes from review 2026-11-19

**Status:** IN PROGRESS
**Type:** CODE-REVIEW
**Branch:** one per phase, see below
**Run:** review/2026-11-19-solution-review-fixes

## Objective

Fix every High and Medium finding of the solution review `docs/review/2026-11-19-solution.md` (5 High,
18 Medium). Each finding is one phase, highest severity first. SOLID-02/03, the one finding too large
for one reviewable commit, takes seven phases. The review's Low findings are out of scope.

The review holds each finding's full location list, its *why it matters* and its rule: this plan
repeats only what a phase needs to be built and checked.

## Context & constraints

- .NET 10, C# 14, `Nullable` on, `TreatWarningsAsErrors`, `EnforceCodeStyleInBuild`, explicit usings
  (`Directory.Build.props`). Central Package Management. Avalonia 12.1.4 with Enigma.Avalonia.Desktop
  1.3.0. Explicit MVVM: CommunityToolkit.Mvvm commands, `field`-backed properties, no generator
  attributes. xUnit v3 on Microsoft.Testing.Platform (`global.json`).
- Composition root: `App.ConfigureServices` → `AddGitClientCore`, `AddEnigmaDesktopServices`,
  `AddGitClientApp`. The host is **started**, never run (`App.axaml.cs`). Page ViewModels and services
  are singletons; views and dialog views are transient.
- Desktop tests build the real container (`tests/…Desktop.UnitTests/Infrastructure/TestServices.cs`)
  and reach a page through its public surface. A refactor that keeps a ViewModel's public members
  keeps its tests valid.
- **This machine's standing rule: the Desktop suite is never run here, in any subset.** Desktop
  tests are written and compiled, and reported as "compiled, not run". The Core unit and integration
  suites are run in full. A dev that changes the container also validates it with a throwaway
  harness outside the repository. That harness builds `App.ConfigureServices` with `ValidateOnBuild`
  and `ValidateScopes`, as `BUG-FACC` did.
- Known baseline failure on Windows: `TokenStoreTests.AfterTheResetOfAnUnreadableStore_SecretsAreStoredAndReadAgain`
  (DPAPI). PHASE07 fixes it. Until then it is pre-existing.
- Core awaits with `ConfigureAwait(false)` and takes a `CancellationToken` on every public async
  method. The Desktop project writes `ConfigureAwait(true)` (ASYNC-03, Low, kept). New code follows
  the side of the line it is on.

## Rules every phase keeps

1. Behaviour is unchanged except where the finding is the behaviour. A structural phase keeps every
   public member a test or a view binds to.
2. No new package. No new project.
3. Each phase adds or adjusts tests for the behaviour it changes. Core tests are run, Desktop tests
   compiled.
4. Strings shown to the user follow the existing voice (plain sentences, no exclamation marks).

## PHASE01 — [High] SEC-01 System programs by path

**Branch:** `review/code-review-6e56-phase01-system-programs-by-path`
**Status:** DONE

Locations: `Desktop/Services/SystemInterop.cs:122`, `:207`, `:210`, `Core/Git/GitExecutable.cs:81`.

### Steps

1. `SystemInterop`, Windows: `cmd.exe` from `Environment.SystemDirectory`, `explorer.exe` from the
   Windows directory (`Environment.GetFolderPath(SpecialFolder.Windows)`), `wt.exe` from
   `%LOCALAPPDATA%\Microsoft\WindowsApps`. A candidate whose file does not exist is skipped. The
   command prompt keeps `UseShellExecute = true`, now with a rooted file name, so the repository
   directory can no longer be where it is found.
2. `GitExecutable`: the fallback no longer returns the bare `git`. When neither the candidates nor
   the `PATH` find it, resolution fails: `TryResolve` returns `false`, and `Path` throws
   `GitNotFoundException`. `PATH` entries that are not fully qualified (`.`, a relative folder) are
   skipped. A relative entry is the current directory under another name.
3. Tests:
   - Core unit: `GitExecutable` with an empty `PATH` and no override does not resolve, and never
     yields a bare name. A relative `PATH` entry holding a `git` is ignored.
   - Desktop (compiled): `TerminalCandidates(Windows, …)` yields rooted file names only. The
     existing Windows-candidate assertions are adapted to the rooted paths.

### Acceptance criteria

- No `ProcessStartInfo` built by `SystemInterop` on Windows carries a bare program name.
- An unresolvable git surfaces as `GitNotFoundException` / an unusable `GitAvailability`, not as a
  bare-name start.
- Zero new warnings. The Core suites are green against the baseline.

## PHASE02 — [High] SEC-02 Atomic token and key files

**Branch:** `review/code-review-6e56-phase02-atomic-secret-files`
**Status:** DONE

Locations: `Core/Security/TokenStore.cs:657`, `:663-673`, `:709`, `:713-723`,
`Core/Hosting/HostAccountService.cs:360`, `:365-375`.

### Steps

1. `AtomicFile`: add `WriteAllBytes(path, bytes, UnixFileMode? unixCreateMode = null)`. Optionally
   add an `unixCreateMode` parameter on the text writers too. The temporary file is created through
   `FileStreamOptions { UnixCreateMode = … }` off Windows, so it is `0600` before the rename. The
   existing retry and temporary-file clean-up are shared, not copied.
2. `FileTokenStore.WriteKeyFile` and `FileTokenStore.Write`, and `HostAccountService.Write`, go through
   `AtomicFile` with `UserRead | UserWrite`. The Windows / Unix branching in those three methods goes
   away.
3. Tests (Core unit): `AtomicFile.WriteAllBytes` replaces the contents and leaves no temporary file.
   Off Windows, the result is `0600` even when the target existed with a wider mode. The existing
   token-store mode test still passes.

### Acceptance criteria

- No secret-bearing file is written in place. A failed write leaves the previous file intact. This
  is shown by a test where the replace is made to fail, or argued from `AtomicFile`'s existing tests
  when a failing replace cannot be provoked portably.
- The file modes on Linux are unchanged (`0600`).
- Zero new warnings. The Core suites are green against the baseline.

## PHASE03 — [High] EXC-01 Corrupt accounts backed up

**Branch:** `review/code-review-6e56-phase03-corrupt-accounts-backup`
**Status:** DONE

Location: `Core/Hosting/HostAccountService.cs:346-351`.

### Steps

1. `HostAccountService` takes `ILogger<HostAccountService>`.
2. `Read`: on `JsonException`, and on `IOException` / `UnauthorizedAccessException` while reading,
   log a warning with the exception. Move the file aside to
   `host-accounts.corrupt-<yyyyMMdd-HHmmss>.json`, as `SettingsService.Backup` does, log the backup
   path, and return the empty list. A failed move is logged and does not throw.
3. Tests (Core unit): an unreadable accounts file returns no account, is moved to a `.corrupt-…`
   backup, and logs a warning. Adding an account afterwards writes a fresh file and leaves the
   backup. The existing tests construct the service with a `NullLogger`.

### Acceptance criteria

- A corrupt accounts file is never silently overwritten: its contents survive in the backup.
- Zero new warnings. The Core suites are green against the baseline.

## PHASE04 — [High] SOLID-01 No service locator

**Branch:** `review/code-review-6e56-phase04-no-service-locator`
**Status:** DONE

Locations: `ProfilesPageViewModel.cs:1041`, `:1161`, `RepositoriesPageViewModel.cs:617`, `:730`,
`HostRepositoryBrowser.cs:118`, `InstanceLauncher.cs:70`, `AppWindows.cs:231`, `:377-388`, `:421`.

### Steps

1. `AddGitClientApp`: register `Func<TView>` once for each dialog view the ViewModels build
   (`AddTransient<Func<TView>>(sp => sp.GetRequiredService<TView>)`). This covers
   `InitRepositoryDialogView`, `CloneRepositoryDialogView`, `IdentityProfileDialogView`,
   `AddHostAccountDialogView` and `HostRepositoriesDialogView`.
2. `RepositoriesPageViewModel`, `ProfilesPageViewModel` and `HostRepositoryBrowser` take those
   factories instead of `IServiceProvider`. The `?? new XView()` fallbacks go: a missing
   registration fails loudly.
3. `InstanceLauncher` takes `Func<IAppWindows>` (registered as
   `sp => sp.GetRequiredService<IAppWindows>`). This keeps BUG-FACC's cycle broken without a locator.
4. `AppWindows` takes `IContentDialogService`, `IOverlayService`, `IInfoBarService`,
   `IToolDialogService`, `IFileDialogService` and `IFolderDialogService` by constructor. It keeps
   `IServiceProvider` only for building the transient windows and their ViewModels: that is the
   legitimate factory use.
5. Tests: the Desktop tests that build these classes by hand are updated (compiled). The scratch
   harness validates the container and resolves `IAppWindows`, `IInstanceLauncher`,
   `IHostRepositoryBrowser` and both pages.

### Acceptance criteria

- `IServiceProvider` remains only in `ContainerPageFactory`, `ToolDialogService`, `AppWindows`
  (window creation), the navigation classes that already take it as a factory, and `App`.
- The container validates on build, with no cycle.
- Zero new warnings. The Core suites are green against the baseline. The Desktop suite compiles.

## PHASE05 — [High] TEST-01 Desktop suite on Windows

**Branch:** —
**Status:** ABANDONED

Locations: `docs/roadmap.md:216`, `tests/Enigma.GitClient.Desktop.UnitTests/Infrastructure/TestServices.cs:202-210`.

Not built by this run: see the footnote in the roadmap and the decision below. The review's fix has
three parts. The Desktop teardown would go through a retrying, read-only-clearing cleanup like the
Core integration tests' `DirectoryCleanup`. The line-ending-sensitive assertions would stop depending
on `core.autocrlf`. Then the suite would be run on Windows until it is green, and kept in the
Definition of Done. The last part is the verification of the first two. This machine's standing
rule forbids running the Desktop suite in a `/vibe` run.

## PHASE06 — [Medium] SEC-03 Open never runs a file

**Branch:** `review/code-review-6e56-phase06-open-never-runs`
**Status:** DONE

Locations: `ChangedFilesPanelViewModel.cs:1085-1093`, `SystemInterop.cs:112`.

### Steps

1. A small pure classifier, `RunnableFiles.IsRunnable(path)`. It holds the extensions that execute
   or redirect when opened: the `PATHEXT` set (from the environment on Windows, with the usual
   defaults when it is empty) plus `.lnk`, `.url`, `.scf`, `.desktop`, `.application`, `.appref-ms`,
   `.ps1`, `.vbs`, `.js`, `.jse`, `.wsf`, `.wsh`, `.msi`, `.msc`, `.reg`, `.hta`, `.cpl`, `.jar`,
   `.sh`, `.command`, `.app`. The comparison is case-insensitive.
2. `ChangedFilesPanelViewModel.OpenAsync`: a runnable file is revealed in the file manager instead
   of opened. The menu item's tooltip (or its label, where the menu text is bound) says so for those
   files.
3. Tests: unit tests of the classifier, in the Desktop suite (compiled). There is no Core home for a
   desktop concern. A panel test asserts that "Open" on `build.cmd` asks the interop to reveal, not
   to open (compiled).

### Acceptance criteria

- No path from a commit or the working tree with a runnable extension reaches `OpenPathAsync`.
- Zero new warnings. The Core suites are green against the baseline.

## PHASE07 — [Medium] TEST-02 Token test on Windows

**Branch:** `review/code-review-6e56-phase07-token-test-windows`
**Status:** DONE

Location: `tests/Enigma.GitClient.Core.UnitTests/Security/TokenStoreTests.cs:398-410`.

### Steps

1. `AfterTheResetOfAnUnreadableStore_SecretsAreStoredAndReadAgain`: the reset still goes through the
   refusing store, which cannot open the old key. The new secret is then written **and** read
   through `_store`, the real protector, so the key is sealed and opened by the same kind of
   protector on every platform. The "fresh key" assertion stays. No skip on Windows.

### Acceptance criteria

- The Core unit suite is fully green on Windows: the baseline's one failure is gone.

## PHASE08 — [Medium] EXC-02 Git start keeps its cause

**Branch:** `review/code-review-6e56-phase08-git-start-inner-exception`
**Status:** DONE

Locations: `Core/Git/GitProcessRunner.cs:287-291`, `Core/Git/GitNotFoundException.cs:9`.

### Steps

1. `GitNotFoundException`: the standard parameterless, `(string)` and `(string, Exception?)`
   constructors.
2. `GitProcessRunner`: the `Win32Exception` becomes the inner exception of the
   `GitNotFoundException` it throws.
3. Tests (Core unit): a runner pointed at a missing executable throws `GitNotFoundException` whose
   `InnerException` is the `Win32Exception`.

### Acceptance criteria

- The native error is kept on the thrown exception.
- Zero new warnings. The Core suites are green against the baseline.

## PHASE09 — [Medium] ASYNC-01 No blocking at startup

**Branch:** `review/code-review-6e56-phase09-sync-settings-load`
**Status:** DONE

Location: `Desktop/App.axaml.cs:74`.

### Steps

1. `ISettingsService.Load()`, synchronous: it reads the file under the same gate and raises
   `Changed`. `LoadAsync` keeps its signature. It wraps the same read and keeps its cancellation
   check.
2. `App.OnFrameworkInitializationCompleted` calls `Load()`. The two blocking calls in `OnExit` stay:
   they are at the process edge, as the review notes.
3. Every `ISettingsService` test double implements `Load()`.
4. Tests (Core unit): `Load()` reads the file, raises `Changed` once, and backs up a corrupt file,
   as `LoadAsync` does.

### Acceptance criteria

- No `GetAwaiter().GetResult()` on the UI thread before the first window.
- Zero new warnings. The Core suites are green against the baseline.

## PHASE10 — [Medium] SOLID-05 One git run sequence

**Branch:** `review/code-review-6e56-phase10-one-git-run-sequence`
**Status:** DONE

Locations: `Core/Git/GitProcessRunner.cs:90-129`, `:195-223`, `:225-254`.

### Steps

1. One private `RunAsync<TOutput, TError>(command, readOutput, readError, cancellationToken)`. It
   starts the process, hands standard input over, awaits both readers and the exit, logs the
   completion, and kills the process on cancellation. The three public entry points pass their
   readers and shape the result.
2. Logging, redaction, the forbidden-operation check and the exception types are unchanged.

### Acceptance criteria

- The kill-on-cancel, standard-input and completion-log code exists once.
- The existing runner unit and integration tests pass unchanged. Zero new warnings.

## PHASE11 — [Medium] SOLID-04 Diff viewer lifetime

**Branch:** `review/code-review-6e56-phase11-diff-viewer-lifetime`
**Status:** DONE

Locations: `ServiceCollectionExtensions.cs:160`, `HistoryPageViewModel.cs:149`,
`DiffViewerViewModel.cs:324`, `ChangedFilesPanelViewModel.cs:408`.

### Steps

1. `DiffViewerViewModel` and `ChangedFilesPanelViewModel` become `IDisposable`. Each subscribes to
   `ISettingsService.Changed` with a named handler and unsubscribes in `Dispose`.
2. `HistoryPageViewModel` takes `Func<DiffViewerViewModel>` (registered once) and builds its viewer
   through it. Every owner of a `ChangedFilesPanelViewModel` or a viewer disposes it when the owner
   is disposed: the history page, the working-tree panel, and the other pages that own one.
3. Tests (compiled): a disposed viewer and a disposed panel no longer react to a settings change.
   The scratch harness validates the container.

### Acceptance criteria

- No transient ViewModel subscribes to a singleton's event without a way to let go.
- Zero new warnings. The Core suites are green against the baseline.

## PHASE12 — [Medium] ASYNC-02 Hosted refresh loops

**Branch:** `review/code-review-6e56-phase12-hosted-refresh-loops`
**Status:** DONE

Locations: `AutoRefreshService.cs:147`, `:295`, `RepositoryWatcher.cs:126`, `:195`, `App.axaml.cs:82`.

### Steps

1. `AutoRefreshService` and `RepositoryWatcher` implement `IHostedService`. The subscriptions and
   the first schedule move from the constructor to `StartAsync`, which runs on the UI thread that
   starts the host, so the loop keeps that context. `StopAsync` unsubscribes, cancels and awaits the
   loop. Start and stop are logged at `Debug`.
2. Registration: the existing singleton, plus `AddHostedService(sp => (X)sp.GetRequiredService<IX>())`,
   so callers keep the same instance. `App` drops the forced `GetRequiredService<IRepositoryWatcher>()`.
   Because `host.Start()` runs before the settings are read, the services pick the first interval up
   from the `Changed` that `Load()` raises.
3. `TestServices` starts the hosted services after building the container and stops them on
   dispose. `RepositoryWatcherTests` / `AutoRefreshTests` that build the classes by hand call
   `StartAsync` (compiled).

### Acceptance criteria

- No background loop starts from a constructor.
- The container validates on build. Zero new warnings. The Core suites are green against the
  baseline.

## PHASE13 — [Medium] LOG-01 Rolling log file

**Branch:** `review/code-review-6e56-phase13-rolling-log-file`
**Status:** DONE

Location: `Desktop/App.axaml.cs:137-148`.

### Steps

1. A `FileLoggerProvider` in the Desktop project, with no package. It writes one line per entry
   (UTC time, level, category, message, exception) to
   `<configuration directory>/logs/enigma-gitclient-<yyyyMMdd>.log`, through a single background
   writer with a bounded queue. It never blocks the UI thread. It keeps the last 7 daily files.
   Every line goes through `ArgumentRedactor.Redact`.
2. Registered on `builder.Logging` in `App.BuildHost`. The level comes from the host configuration's
   `Logging` section (`appsettings.json` beside the executable, read by the host builder), at
   `Information` by default.
3. The log directory is shown in the unhandled-error dialog's text, and documented in the README.
4. Tests (Desktop, compiled): the provider writes a redacted line, rolls by day, and deletes files
   beyond the retention.

### Acceptance criteria

- A running application leaves a log file a user can attach to a report.
- Zero new warnings. The Core suites are green against the baseline.

## PHASE14 — [Medium] PERF-03 Validation off UI

**Branch:** `review/code-review-6e56-phase14-dialog-validation`
**Status:** DONE

Locations: `CloneRepositoryDialogViewModel.cs:161-165`, `InitRepositoryDialogViewModel.cs:114-118`.

### Steps

1. Both dialogs store `ValidationMessage`. It is recomputed once per change of the fields it
   depends on, and the change raises `ValidationMessage`, `HasValidationMessage` and `IsValid` and
   the `ValidationChanged` event.
2. The text checks stay synchronous. The disk probe (exists / is empty) runs off the UI thread after
   a short debounce (250 ms), is cancelled by the next change, and catches `IOException` /
   `UnauthorizedAccessException`, which it reports as a validation message. `IsValid` is false while
   a probe is pending. The confirm path awaits the pending probe, so a fast Enter cannot skip it.
3. Tests (Desktop, compiled): one probe per change, a target that cannot be listed gives a message
   instead of an exception, and the dialog is valid once the probe finds an empty folder.

### Acceptance criteria

- A keystroke costs no synchronous disk access on the UI thread.
- Zero new warnings. The Core suites are green against the baseline.

## PHASE15 — [Medium] PERF-01 Bulk collection refills

**Branch:** `review/code-review-6e56-phase15-bulk-collection-refills`
**Status:** DONE

Locations: the 14 sites in the review (tags, repositories ×2, history, branches, conflicts, profiles
×3, remotes, submodules, worktrees, changed files, host repositories).

### Steps

1. `RangeObservableCollection<T> : ObservableCollection<T>` in the Desktop project.
   `ReplaceWith(IEnumerable<T>)` raises one `Reset`. `AddRange(IEnumerable<T>)` raises one `Add`
   with the item list, or a `Reset` when the binding cannot take a multi-item add. It raises the
   `Count` and indexer notifications once.
2. Every listed refill uses it. The collection property's type changes only where a test or view
   needs nothing else.
3. Tests (Desktop, compiled): the collection raises one notification per refill, and keeps its
   order and contents.

### Acceptance criteria

- No listed site refills a bound collection item by item.
- Zero new warnings. The Core suites are green against the baseline.

## PHASE16 — [Medium] PERF-02 Folded conflict context

**Branch:** `review/code-review-6e56-phase16-folded-conflict-context`
**Status:** DONE

Locations: `ConflictResolutionPageView.axaml:33`, `:165`, `:180`, `:188`,
`ConflictResolutionPageViewModel.cs:87-90`.

### Steps

1. `ConflictRegionViewModel` shows a stable region longer than twice the context (the diff's
   context-lines setting, 3 by default) as its first and last context lines, with an
   "N unchanged lines" expand row between them. Lines are built for what is shown, and the hidden
   ones only when the row is expanded. The first and last regions of the file keep only the side
   next to a conflict.
2. The expand row is a button with an automation name, styled like the diff viewer's.
3. Writing the file back is unaffected: it is built from the document, not from the line
   ViewModels.
4. Tests (Desktop, compiled): a 5,000-line stable region builds context lines only. Expanding shows
   them all. A short region is not folded.

### Acceptance criteria

- A large stable region costs a few line ViewModels and controls until it is expanded.
- Zero new warnings. The Core suites are green against the baseline.

## PHASE17 — [Medium] TEST-03 No fixed test delays

**Branch:** `review/code-review-6e56-phase17-no-fixed-test-delays`
**Status:** DONE

Locations: `RepositoryWatcherTests.cs:296`, `FileSystemRepositoryEventSourceTests.cs:165`,
`DiffSyntaxHighlightingTests.cs:89`, `AtomicFileTests.cs:159`, `:177`.

### Steps

1. The two `AtomicFile` held-file tests release the reader on a signal raised once the writer has
   met the held file, instead of after 50 ms. On Windows the signal is the first refused replace,
   observed through a test hook or a file-system condition. When no hook can be added without a
   product seam, the test releases on the writer's own progress.
2. The three Desktop tests drive the producer to completion with the scripted event source, the
   `ManualTimeProvider` or the dispatcher, and assert on its state instead of waiting (compiled).

### Acceptance criteria

- None of the five tests sleeps to prove that nothing happened.
- The Core suites are green against the baseline.

## PHASE18 — [Medium] XAML-01 Field label styles

**Branch:** `review/code-review-6e56-phase18-field-label-styles`
**Status:** DONE

Locations: the 52 sites in the review, in 16 views.

### Steps

1. `Themes/Styles.axaml`: `TextBlock.fieldlabel` (12, `EnigmaForegroundSecondaryBrush`) and
   `TextBlock.validation` (12, wrap, `EnigmaErrorBrush`). The brush keys are the ones the sites use
   today.
2. Every listed label and validation line uses the class instead of its inline attributes. A site
   whose attributes differ keeps the difference inline.

### Acceptance criteria

- No listed site repeats the size, the brush or the wrapping inline. Rendering is unchanged.
- Zero new warnings, `AVLN*` included.

## PHASE19 — [Medium] XAML-02 One edge auto-scroll

**Branch:** `review/code-review-6e56-phase19-edge-auto-scroll`
**Status:** DONE

Locations: `BranchesPageView.axaml.cs:558-606`, `HistoryPageView.axaml.cs:685-728`,
`RepositoriesPageView.axaml.cs:497-520`.

### Steps

1. `Views/EdgeAutoScroll`: it owns the timer, the step (`BranchDragGesture.ScrollFor`) and the
   `ScrollViewer`, with `Follow(pointer position)`, `Stop()` and an optional `AfterStep` callback.
2. The three views keep what is theirs (what is dragged, where it drops) and delegate the edge
   scrolling. The press / move / release / capture-lost state machine is shared only where it is
   identical.
3. Tests (Desktop, compiled): the helper scrolls towards the edge it is near, and stops.

### Acceptance criteria

- `FollowTheEdge` / `StopScrolling` / `ScrollTowardsTheEdge` exist once.
- Zero new warnings.

## PHASE20 — [Medium] XAML-03 Theme colour resources

**Branch:** `review/code-review-6e56-phase20-theme-colour-resources`
**Status:** DONE

Locations: `Themes/Controls.axaml:152`, `:216`, `AppWindows.cs:431`,
`UnhandledErrorReporter.cs:198`, `Controls/AuthorAvatar.cs:25-32`.

### Steps

1. `Themes/Graph.axaml`, per theme: the badge text colour, the avatar initials colour and the
   avatar tints. The tints take the same values as the graph lanes, referenced or duplicated per
   theme as the file already does for the lanes.
2. `Controls.axaml` uses `DynamicResource` for both. `AuthorAvatar` reads its tints and initials
   from the resources, and follows a theme change.
3. A warning brush key, the library's `Enigma*` warning brush when one exists, is looked up by
   `AppWindows` and `UnhandledErrorReporter` instead of `Color.Parse("#E8A33D")`.

### Acceptance criteria

- No colour literal remains at the five sites. Both themes resolve every new key.
- Zero new warnings.

## PHASE21 — [Medium] STYLE-01 Null-forgiving removed

**Branch:** `review/code-review-6e56-phase21-null-forgiving`
**Status:** DONE

Locations: the 55 operators at the 34 sites in the review.

### Steps

1. Command handlers: a pattern match that returns when the parameter or its member is null
   (`row is { Worktree: { } worktree } ? … : Task.CompletedTask`).
2. Core sites: a guard, or a local captured after the null check.
3. Where a `!` cannot be avoided, a one-line comment says why it holds.

### Acceptance criteria

- No `!` at the listed sites without a stated reason.
- Zero new warnings. The Core suites are green against the baseline.

## PHASE22 — [Medium] SOLID-03 Split SubmoduleService

**Branch:** `review/code-review-6e56-phase22-split-submodule-service`
**Status:** DONE

Location: `Core/Submodules/SubmoduleService.cs:228` (1,475 lines).

### Steps

1. Split along the review's three reasons to change, as internal classes under `Core/Submodules/`:
   - reading: list, head, superproject, is-on-ref, changed and staged pins;
   - pins: describe a pin change, a pin commit;
   - updating: update, follow, plan discard, put back.
   The pure parsers move to `SubmoduleOutputParser`.
2. `SubmoduleService` stays the `ISubmoduleService` callers use and composes the three. Its public
   static helpers keep their names and forward, because tests and callers use them.

### Acceptance criteria

- No class under `Core/Submodules/` is past ~600 lines. The public surface is unchanged.
- The submodule unit and integration tests pass unchanged. Zero new warnings.

## PHASE23 — [Medium] SOLID-02 Shared clone flow

**Branch:** `review/code-review-6e56-phase23-shared-clone-flow`
**Status:** DONE

Locations: `RepositoriesPageViewModel.cs:106`, `ProfilesPageViewModel.cs:205`.

### Steps

1. `IRepositoryCloner` (Desktop service). `RunCloneAsync(request)` covers the progress overlay, the
   cancel, the global-identity offer, the open, the add to the list, the report and the switch to
   the repository window. It is moved out of `RepositoriesPageViewModel`, as is the identity offer
   it shares with *Create*.
2. `ProfilesPageViewModel` takes `IRepositoryCloner` instead of `RepositoriesPageViewModel`.
   `RepositoriesPageViewModel` uses it too. Busy state and cancel stay observable on the page
   through the service.
3. Tests that called `RepositoriesPageViewModel.RunCloneAsync` call the service, or the page member
   is kept as a forward when a view binds to it (compiled).

### Acceptance criteria

- No page ViewModel takes another page's ViewModel.
- The container validates on build. Zero new warnings.

## PHASE24 — [Medium] SOLID-02 History ref commands

**Branch:** `review/code-review-6e56-phase24-history-ref-commands`
**Status:** DONE

Location: `HistoryPageViewModel.cs:132`, the branch-badge and tag commands.

### Steps

1. Two collaborators, each owning its commands and their handlers:
   - `HistoryBranchActions`: checkout, merge, drop, delete, pull / push, rename, worktree from a
     branch, merge source;
   - `HistoryTagActions`: create here, push, delete, delete remote.
   They are registered as singletons, take only the operations they use, and read the page's state
   through a narrow interface the page implements.
2. The page exposes the same `BranchCommands` / `RowCommands` objects, now built from the
   collaborators, and its constructor loses those operations.

### Acceptance criteria

- The page's constructor is down by the operations the two collaborators own. Its public surface is
  unchanged.
- The container validates on build. Zero new warnings.

## PHASE25 — [Medium] SOLID-02 History row commands

**Branch:** `review/code-review-6e56-phase25-history-row-commands`
**Status:** TODO

Location: `HistoryPageViewModel.cs`, the reset, revert, squash, stash, discard and worktree-here
commands.

### Steps

1. `HistoryCommitActions` owns reset, revert, squash (with its base) and worktree here.
   `HistoryStashActions` owns stash, apply, pop, drop and discard uncommitted. Both are built as in
   PHASE24.
2. The page keeps its public command properties, built from them.

### Acceptance criteria

- The page's constructor takes at most about 12 parameters after PHASE24 and this phase. Its public
  surface is unchanged.
- The container validates on build. Zero new warnings.

## PHASE26 — [Medium] SOLID-03 History search, stash

**Branch:** `review/code-review-6e56-phase26-history-search-stash`
**Status:** TODO

Location: `HistoryPageViewModel.cs:40`, the search and the stash folding.

### Steps

1. `HistorySearch` (search text, matches, next / previous, clear) and `StashFolding` (the stash list,
   the helper commits, folding a page of rows) become plain classes owned by the page. They are pure
   state and logic and need no DI.
2. The page delegates its existing public members to them.

### Acceptance criteria

- `HistoryPageViewModel` is substantially shorter (target: under ~1,800 lines after PHASE24–26). Its
  public surface is unchanged.
- Zero new warnings.

## PHASE27 — [Medium] SOLID-02 Profiles page split

**Branch:** `review/code-review-6e56-phase27-profiles-page-split`
**Status:** TODO

Location: `ProfilesPageViewModel.cs:168` / `:205`.

### Steps

1. The connected-accounts half (add, edit, remove and browse an account, token recovery, the
   account rows) moves to a `HostAccountsViewModel` that the page exposes as a property. The page
   keeps the identity profiles.
2. The view binds the accounts section to the new property. Bindings change in the XAML only where
   the path moves.
3. Tests (compiled): the account tests reach the same members through the new property.

### Acceptance criteria

- The page's constructor and the new class each take fewer than ~10 parameters. Behaviour is
  unchanged.
- The container validates on build. Zero new warnings, `AVLN*` included.

## PHASE28 — [Medium] SOLID-02 Operation reporter

**Branch:** `review/code-review-6e56-phase28-operation-reporter`
**Status:** TODO

Locations: the `*Operations` classes in the review (5–7 parameters each).

### Steps

1. `IOperationFeedback` (singleton) bundles the info bar, the content dialog service and the
   recurring helpers the operations repeat today (report, confirm, first line of git's error).
2. The `*Operations` classes take it in place of `IContentDialogService` + `IInfoBarService`, and
   their duplicated private helpers go.

### Acceptance criteria

- Every listed `*Operations` class loses at least one parameter, and their shared helpers exist
  once.
- The container validates on build. Zero new warnings.

## Out of scope

- The 15 Low findings of the review.
- SOLID-03's other four god classes: `ConflictResolutionPageViewModel`, `BranchesPageViewModel`,
  `DiffViewerViewModel`, `BranchOperations`. See the decision below.
- TEST-01 (PHASE05): see the decision below.
- A release (version bump, release notes): releases are their own items in this repository.

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| Item shape | One `CODE-REVIEW` item, one phase per finding, SOLID-02/03 over seven phases | The house rule for review items. One reviewable commit per phase | One item per finding (28 roadmap items); a single big-bang commit |
| Phase order | Highs (SEC-01 first, as the review asks), then Mediums: security and correctness, then the small structural ones, then the large refactors last | Highest value per risk first. The line-anchored findings are fixed before the refactors move their code. A quarantine at the end blocks nothing | The review's own Medium order (puts the riskiest refactors first) |
| TEST-01 | Planned and set `ABANDONED` at planning, with a footnote | Its only verification is running the Desktop suite on Windows, which this machine's standing rule forbids in `/vibe` runs. A teardown change that cannot be run is unverifiable work | Patching the teardown blind and reporting it done; running the suite against the standing rule |
| How Desktop changes are verified | Desktop tests written and compiled, not run. A scratch container-validation harness outside the repo for every DI change | The standing rule. `BUG-FACC` established the harness | Running the Desktop suite; skipping verification of the container |
| SOLID-02/03 scope | The review's named starting set: the three pages, the clone flow, `SubmoduleService`, the operations' shared feedback. The other four god classes are deferred | Each split is a behaviour-preserving refactor whose safety net is the Desktop suite, which cannot run here. The run does the set the review names first and leaves the rest for a session where that suite runs | Splitting all eight classes blind |
| SOLID-01 view factories | `Func<TView>` per dialog view | Explicit in each constructor, registered once, no new abstraction | One `IDialogViewFactory` (a locator by another name for five views) |
| LOG-01 sink | An in-house rolling file provider. The level comes from the host's `Logging` configuration | No new package (rule 2). The host builder already reads `appsettings.json` beside the executable | Serilog / NLog (new dependency); a level field in `settings.json` with UI (scope) |
| SEC-03 behaviour | Reveal runnable files in the file manager instead of opening them | Safe without a question. The user still reaches the file | Asking first (friction on every script); refusing outright (nothing happens) |
| ASYNC-02 registration | Same singleton exposed as `IHostedService`. `TestServices` starts the hosted services | Callers keep their interface and instance. Tests keep the running loops they rely on | Separate hosted wrappers (two objects per loop) |
| PERF-01 | `RangeObservableCollection<T>` | Smallest change that removes the per-item events, with no view change | `CollectionView` everywhere (larger change of the views) |
| PERF-02 | Fold stable regions to context lines with an expand row | Matches the diff viewer's hunks. Bounded cost whatever the file size | Hosting the virtualising diff editor in each region (large rework) |
| PERF-03 | A stored validation property, with a debounced probe off the UI thread | The review's fix. It removes both the triple probe and the UI-thread I/O | Caching the probe result only (still on the UI thread) |
| Release | No version bump | Releases are separate `FEATURE` items in this repo | Bumping in the last phase |
