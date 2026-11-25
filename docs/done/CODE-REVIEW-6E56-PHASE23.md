# CODE-REVIEW-6E56-PHASE23 — [Medium] SOLID-02 Shared clone flow

## Summary

`ProfilesPageViewModel` took `RepositoriesPageViewModel` in its constructor so it could clone a
repository picked on a host, through the repositories page's `RunCloneAsync` and
`CloneParentDirectory`. One page depended on another page to reach a workflow both need.

What changed:

- `IRepositoryCloner` / `RepositoryCloner` (Desktop service, new, a singleton) holds the clone flow:
  - `RunCloneAsync(request)` runs the progress overlay and its working cancel, remembers the
    directory, offers the global identity, opens the clone, adds it to the selected profile's list,
    reports, and shows the repository window;
  - `CancelClone()`;
  - `CloneParentDirectory(profile)`, with `BaseDirectoryOf` and `DefaultParentDirectory` as statics;
  - `OfferGlobalIdentityAsync(repository, what)`, which *Create* shares.
  The code moved from the repositories page unchanged, apart from the list step: the service adds to
  the store, and the page shows it (below).
- `ProfilesPageViewModel` takes `IRepositoryCloner` instead of `RepositoriesPageViewModel`.
- `RepositoriesPageViewModel` takes `IRepositoryCloner` and drops `ISettingsService` and
  `IOverlayService`, which only the clone used. Its public members stay as forwards, for the tests
  that call them:
  - `RunCloneAsync` runs the clone through the service, with the page busy meanwhile, and shows the
    list as it then stands;
  - `CancelCloneCommand` cancels through the service;
  - `CloneParentDirectory` and `BaseDirectoryOf` forward.
  *Create* offers the identity through the service.
- Registered in `AddGitClientApp` beside `IRepositoryOpener`.

## Files/modules touched

- Created: `src/Enigma.GitClient.Desktop/Services/RepositoryCloner.cs`
- Modified: `src/Enigma.GitClient.Desktop/ViewModels/Pages/RepositoriesPageViewModel.cs`
- Modified: `src/Enigma.GitClient.Desktop/ViewModels/Pages/ProfilesPageViewModel.cs`
- Modified: `src/Enigma.GitClient.Desktop/DependencyInjection/ServiceCollectionExtensions.cs`
- Modified: `tests/Enigma.GitClient.Desktop.UnitTests/CompositionRootTests.cs`
- Modified: `docs/roadmap.md`, `docs/plan/CODE-REVIEW-6E56.md`
- Created: `docs/done/CODE-REVIEW-6E56-PHASE23.md`

## Deviations & follow-ups

- **The list step.** Before, the page added the clone to the list and replaced its own rows at once.
  The service now adds to the store, and the page shows what is stored (`ReloadListAsync`) after a
  clone it started. A clone started from the profiles page no longer refreshes the repositories
  page's rows behind the scenes: the page reads the store each time it is shown (`OnAppearingAsync`),
  so it is up to date when the reader comes back. The list store raises no change event the page
  could follow instead.
- **Busy.** A clone started from the profiles page used to set the *repositories* page busy. Now it
  does not: the overlay covers the window for the whole clone, so nothing behind it could be clicked
  either way. A clone the repositories page starts sets it busy, as before.
- **"Busy state and cancel stay observable on the page through the service."** Cancel does, through
  `CancelCloneCommand` and the overlay's own button. Busy is the page's own flag around its own
  clone, not a service property: nothing outside that page watches it.
- The service reports a failed identity write with `ProfilesPageViewModel.Describe`, a static of a
  page, as the repositories page did. Moving that one wording helper to a shared place is a follow-up
  for a later split of the profiles page (PHASE27).
- Tests were not adapted. `RunCloneAsync`, `CloneParentDirectory` and `CancelCloneCommand` kept
  their signatures on the page.

## Build/test evidence

- Baseline: see `docs/done/CODE-REVIEW-6E56-PHASE01.md`.
- **This dev:**
  - `dotnet build Enigma.GitClient.slnx --no-incremental`: 0 warnings, 0 errors.
  - Core unit: 1534 run, 1531 passed, 0 failed, 3 skipped.
  - Core integration: 449 run, 447 passed, 0 failed, 2 skipped (6 min 14 s).
  - Desktop: compiled, **not run** (this machine's standing rule).
- **Container check (throwaway harness outside the repository).** `App.ConfigureServices` was built
  with `ValidateOnBuild` and `ValidateScopes` on the headless platform with the app's styles. Every
  check passed:
  - the container validates;
  - `IRepositoryCloner` resolves to `RepositoryCloner`;
  - both pages resolve;
  - across all 10 page ViewModels, no constructor takes a page ViewModel;
  - with nothing else chosen, a clone goes to the user's home folder.
- **New tests (Desktop, compiled, not run)** in `CompositionRootTests`: no page ViewModel takes
  another page's ViewModel; the cloner is one shared service.
- **Existing tests kept (compiled):** the clone tests of `RepositoriesPageTests`, which call
  `page.RunCloneAsync` and `page.CloneParentDirectory`, and the profiles page's clone-from-host tests.
- **Fix budget:** 0 cycles.

## Documentation sweep

- Nothing stale: no user doc describes how a clone is wired.

## Decisions taken at build time

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| Where the identity offer lives | On `IRepositoryCloner`, used by *Create* too | The plan moves it with the clone. Both new-repository paths ask the same question | A separate identity service for one method |
| How the repositories page sees a new clone | `ReloadListAsync` after its own clone; the store on every showing otherwise | The flow is unchanged for the page's own clone. The store is the truth for the other | A "cloned" event on the service (a second channel for what showing the page already does) |
| The page's public members | Kept as forwards | Tests call them; the plan allows the forward | Moving every test to the service |
| Lifetime | Singleton | Like the other operations services; it holds only the clone in progress | Transient (two clones' cancels would not meet) |
