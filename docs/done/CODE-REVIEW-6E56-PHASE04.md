# CODE-REVIEW-6E56-PHASE04 — [High] SOLID-01 No service locator

## Summary

`IServiceProvider` is gone from the classes that are not factories. Their constructors say again what
they need:

- `RepositoriesPageViewModel` takes `Func<InitRepositoryDialogView>` and
  `Func<CloneRepositoryDialogView>`. `ProfilesPageViewModel` takes `Func<IdentityProfileDialogView>`
  and `Func<AddHostAccountDialogView>`. `HostRepositoryBrowser` takes
  `Func<HostRepositoriesDialogView>`. The `?? new XView()` fallbacks are gone, so a missing
  registration now fails loudly instead of building a view the container never configured.
- `InstanceLauncher` takes `Func<IAppWindows>`. BUG-FACC's cycle stays broken, and the constructor
  now shows the deferred edge instead of hiding it in a lookup.
- `AppWindows` keeps `IServiceProvider` only to build the transient windows and their ViewModels by
  type, its legitimate factory use. The six host-bound services it used to pull from the container
  are now held by a new `WindowHostBinder`. It binds a window's dialog, overlay, info-bar and
  tool-dialog hosts and its storage provider before the window is shown. `AppWindows` takes the
  binder, plus `IInfoBarService` and `IContentDialogService` for its own two messages.
- `AddGitClientApp` registers the five view factories and `Func<IAppWindows>` once, as singleton
  delegates over the container.

## Files/modules touched

- Created: `src/Enigma.GitClient.Desktop/Services/WindowHostBinder.cs`
- Modified: `src/Enigma.GitClient.Desktop/Services/AppWindows.cs`
- Modified: `src/Enigma.GitClient.Desktop/Services/InstanceLauncher.cs`
- Modified: `src/Enigma.GitClient.Desktop/Services/HostRepositoryBrowser.cs`
- Modified: `src/Enigma.GitClient.Desktop/ViewModels/Pages/RepositoriesPageViewModel.cs`
- Modified: `src/Enigma.GitClient.Desktop/ViewModels/Pages/ProfilesPageViewModel.cs`
- Modified: `src/Enigma.GitClient.Desktop/DependencyInjection/ServiceCollectionExtensions.cs`
- Modified: `tests/Enigma.GitClient.Desktop.UnitTests/CompositionRootTests.cs`
- Modified: `docs/roadmap.md`, `docs/plan/CODE-REVIEW-6E56.md`
- Created: `docs/done/CODE-REVIEW-6E56-PHASE04.md`

## Deviations & follow-ups

- The plan said to inject the six services into `AppWindows` directly. They went to
  `WindowHostBinder` instead. Injecting them directly would have taken `AppWindows` to twelve
  constructor parameters, which is SOLID-02's smell. Binding a window's hosts is a responsibility of
  its own.
- The two pages' constructors each gained a parameter (one locator became two factories). PHASE23 and
  PHASE27 shrink them.
- No Desktop test built these classes by hand, so none needed adapting.

## Build/test evidence

- Baseline: see `docs/done/CODE-REVIEW-6E56-PHASE01.md`.
- **This dev:**
  - build: 0 warnings, 0 errors.
  - Core unit: 1530 run, 1526 passed, 1 failed (the baseline's), 3 skipped.
  - Core integration: 449 run, 447 passed, 0 failed, 2 skipped (6 min 50 s).
  - Desktop: compiled, **not run**.
- **Container check**: a throwaway harness outside the repository builds `App.ConfigureServices` with
  `ValidateOnBuild` and `ValidateScopes`, as `CompositionRootTests.BuildProvider` does.
  - Result: `ValidateOnBuild: OK`.
  - Resolved: `IAppWindows`, `IInstanceLauncher`, `IHostRepositoryBrowser`,
    `IUnhandledErrorReporter`, `WindowHostBinder`, `RepositoriesPageViewModel`,
    `ProfilesPageViewModel`, `HistoryPageViewModel`.
  - It also listed the types whose constructors take `IServiceProvider`. In the app's namespaces these
    are exactly `AppWindows`, `ToolDialogService`, `ContainerPageFactory`, `ShellNavigation` and
    `StartNavigation`.
- **New tests (Desktop, compiled, not run)** in `CompositionRootTests`:
  - `OnlyTheFactories_TakeTheContainer`, a reflection guard over the app's namespaces. The harness
    showed that the XAML compiler's generated types take a provider too, so the guard is limited to
    `Enigma.GitClient.Desktop.*`.
  - `Container_ResolvesEveryViewFactory` (6 cases).
- **Fix budget:** 0 cycles.

## Documentation sweep

- Nothing stale: no user doc describes the composition root.

## Decisions taken at build time

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| Lifetime of the `Func<T>` registrations | Singleton delegates over the root provider | The consumers are singletons. The delegate resolves a transient view per call | Transient delegates (a new delegate per consumer for nothing) |
| `AppWindows`' six host services | A `WindowHostBinder` class | One reason to change, and it keeps `AppWindows` below the over-injection threshold | Twelve constructor parameters |
| Guarding the fix | A reflection test over constructors in the app's namespaces | It catches a locator coming back anywhere, not only in the five classes | A test per class |
