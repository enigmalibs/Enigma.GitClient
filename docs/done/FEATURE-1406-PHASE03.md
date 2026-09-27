# FEATURE-1406-PHASE03 — Browse repositories in a dialog

**Item:** FEATURE-1406 — Profiles that own their integrations
**Phase:** PHASE03 — Browse repositories in a dialog
**Branch:** `feature/feature-1406-phase03-browse-dialog`
**Run:** feature/2026-09-27-diff-profiles-release

## Summary

What a connected account can reach is now listed in a **dialog**, opened with *Browse repositories* on
the account's row, instead of in the Integrations page's right-hand pane. The page that held the
listing goes in PHASE04, so the listing had to move first.

- `HostRepositoriesDialogViewModel` holds the listing, moved out of `IntegrationsPageViewModel`:
  - the paged read, up to `PageLimit` pages;
  - All / Public / Private, which sends a new request;
  - the filter over what was read;
  - the summary, which now also states a failure, so it can be read inside the dialog (it is still
    reported on the bar too);
  - *Open on the host*, and *Clone*.
- *Clone* does not clone. It names the repository (`Picked`) and raises `CloneRequested`.
- `IHostRepositoryBrowser.BrowseAsync(account)` shows the dialog first and then reads the list into it,
  so a slow host never delays the dialog. It closes the dialog when *Clone* is pressed, and returns
  the repository picked.
- The caller runs the clone once the dialog is gone, because the clone's progress card and cancel
  would otherwise sit under it. The Integrations page does this with
  `RepositoriesPageViewModel.RunCloneAsync`, exactly as before.
- The Integrations page is now only its account list, with *Browse repositories* and *Disconnect* on
  every row. The listing pane is gone, and so is the page's hook on the toolbar refresh: the dialog
  reads when it opens.

## Files / modules touched

**Created**

- `src/Enigma.GitClient.App/ViewModels/Dialogs/HostRepositoriesDialogViewModel.cs` — the dialog's
  ViewModel, and `HostRepositoryRowViewModel` (moved)
- `src/Enigma.GitClient.App/Views/Dialogs/HostRepositoriesDialogView.axaml(.cs)` — the listing pane,
  760 × 480
- `src/Enigma.GitClient.App/Services/HostRepositoryBrowser.cs` — `IHostRepositoryBrowser` and its
  implementation
- `tests/Enigma.GitClient.App.UnitTests/HostRepositoriesDialogTests.cs` — 13 tests:
  - the listing, paging, the page limit, the filter and visibility;
  - a failure, stated in the dialog and on the bar;
  - an account without a token, which asks nothing of the host;
  - the rate-limit sentence, opening a repository, and cloning;
  - the browser: it shows the dialog and fills it, and it closes the dialog on *Clone* and hands the
    repository back;
  - a paint test.
- `tests/Enigma.GitClient.App.UnitTests/Infrastructure/FakeHostProvider.cs` — moved out of
  `IntegrationsPageTests.cs`, which goes in PHASE04

**Modified**

- `src/Enigma.GitClient.App/ViewModels/Pages/IntegrationsPageViewModel.cs` — the listing removed;
  `BrowseCommand` added; `IHostRepositoryBrowser` injected in place of `IAutoRefreshService`
- `src/Enigma.GitClient.App/Views/Pages/IntegrationsPageView.axaml` — the account list only
- `src/Enigma.GitClient.App/DependencyInjection/ServiceCollectionExtensions.cs` — the browser
  (singleton) and the dialog view (transient)
- `tests/…/IntegrationsPageTests.cs` — the listing, opening, paint and rate-limit tests moved to the
  dialog's tests
- `tests/…/AutoRefreshTests.cs` — "re-reads its repositories when asked" becomes
  `NoRefreshEverAsksAHostForItsRepositories`, which checks the rate-limit protection it stood for
- `tests/…/Infrastructure/UiServiceDoubles.cs` — `ScriptedContentDialogService.Hidden` counts the
  dialogs closed by code
- `docs/roadmap.md`, `docs/plan/FEATURE-1406.md` — statuses

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| Who runs the clone | The page, after `BrowseAsync` returns the pick | A clone cannot be run in a test (it clones into the user's home), so the dialog's part is proven apart. The page's call is the one it had before |
| Refresh inside the dialog | Reads when it opens; no toolbar hook | The dialog is modal, so the toolbar cannot be reached while it is open. Reopening it re-reads |
| A failure while the dialog is open | Said in the dialog's summary line and on the bar | The bar is drawn above the dialog host, but the dialog's own line is where the reader is looking |
| No provider for the account's kind | A warning, and no dialog | Nothing could fill it |

## Deviations & follow-ups

- The Integrations page's own *Browse → clone* call is not covered by a test: `RunCloneAsync` clones
  into `RepositoriesPageViewModel.DefaultParentDirectory()`, the user's home. The dialog and the browser
  are covered up to handing back the repository. The same call moves to the Profiles page in PHASE04.
- Documentation sweep: `README.md` already says integrations "browse and clone your repositories",
  which stays true, so nothing was edited.
- One fix cycle was used. A new test asserted that the fake provider records the token on a listing,
  but the fake records tokens only on validation, so the assertion was removed. No production code
  changed.

## Build / test evidence

- `dotnet build Enigma.GitClient.slnx`: 0 warnings, 0 errors.
- `dotnet test --solution Enigma.GitClient.slnx`: **2250 passed**, 0 failed. That is 2245 before, minus
  8 tests moved out of `IntegrationsPageTests`, plus 13 new dialog tests; one auto-refresh test was
  replaced.
