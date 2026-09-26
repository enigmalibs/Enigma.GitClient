# FEATURE-5431-PHASE01 — Start window and repository window

**Item:** FEATURE-5431 — A start window, one repo per window
**Branch:** `feature/feature-5431-phase01-start-window`
**Run:** feature/2026-09-22-home-window-merges-refresh

## Summary

The application now opens on a **start window** with Repositories in its rail and Integrations and
Settings in its footer. Opening, creating or cloning a repository replaces it with the **repository
window**: the old main window, now starting on History, with no Repositories page in its rail. A
"Close repository" button in that window's strip closes the repository and goes back to the start
window. Closing either window ends the application. `Enigma.GitClient.App <path>` opens straight into
a repository; a path that does not open as one lands on the start window, with an info bar saying why.

Which window is on screen is owned by a new coordinator, `IAppWindows`. Windows are transient, because
Avalonia cannot show a window again once it has closed. Their ViewModels, and the pages, stay
singletons. On every swap the coordinator:

1. takes the ViewModel away from the outgoing window, which empties its content and frees the page it
   was showing — a control has one parent, and the page is about to go into the next window;
2. binds the new window;
3. re-registers the dialog, overlay and info-bar hosts and both storage providers — the library lets
   the last registration win;
4. shows the new window, and only then closes the old one, so `OnLastWindowClose` never sees a moment
   with no window at all.

The git-availability check moved out of the main window's ViewModel into the coordinator. It runs once,
when the first window opens, whichever window that is.

## Files / modules touched

**Added — App**

- `Services/AppWindows.cs` — `AppWindowKind`, `IHostWindow`, `IAppWindows`, `AppWindows` (the swap, the
  host registration, the command-line start, the one-time git check)
- `Services/RepositoryOpener.cs` — discovery → repository context → recent list, shared by the
  repositories page and the command line
- `Navigation/StartNavigation.cs` — `StartPage`, `IStartNavigation`, `StartNavigation` over a keyed
  `INavigationService` (`"start"`)
- `ViewModels/StartWindowViewModel.cs`
- `Views/StartWindow.axaml` (+ `.cs`) — rail, content, the three overlay hosts

**Modified — App**

- `App.axaml.cs` — `ShutdownMode.OnLastWindowClose`, start through the coordinator,
  `FirstArgument(args)`; `RegisterHosts` moved into the coordinator
- `DependencyInjection/ServiceCollectionExtensions.cs` — keyed start navigation, coordinator, opener,
  start window; `MainWindow` is now transient
- `Navigation/ShellNavigation.cs` — no Repositories page; starts on History
- `ViewModels/MainWindowViewModel.cs` — `CloseRepositoryCommand`; the git check and its three
  dependencies gone; `InitialiseAsync` only selects the first page
- `ViewModels/Pages/RepositoriesPageViewModel.cs` — opens through the opener and asks for the
  repository window instead of navigating the rail
- `Views/MainWindow.axaml` (+ `.cs`) — "Close repository" button; implements `IHostWindow`
- `README.md` — the features list says where a repository is opened from (sweep)

**Added / modified — tests**

- `AppWindowsTests.cs` (new) — the start window and its rail; the swap closes the old window and frees
  its page; going back and forth builds fresh windows and puts the *same* page objects into them;
  asking for the window already on screen keeps it; the command line opens a real repository, falls
  back on a folder that is not one (with a warning) and on no argument; "Close repository";
  `FirstArgument`
- `Infrastructure/UiServiceDoubles.cs` — `RecordingAppWindows`
- `Infrastructure/TestServices.cs` — registers the recording coordinator, so page tests leave no
  windows open on the headless platform
- `MainWindowShellTests.cs`, `RepositoriesPageTests.cs` — the rail without Repositories, the shell
  starting on History, opening a path asking for the repository window

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| How a page survives a window swap | Setting the outgoing window's `DataContext` to null before the new one binds | Its content binding goes to null and releases the page. The alternative, re-navigating on every showing, would rebuild every page and throw away its view state |
| Where the host controls are read from | An `IHostWindow` interface, implemented explicitly by both windows | The generated `x:Name` fields are internal and window-specific, and the coordinator must not know which window it has |
| Where the git check lives | The coordinator, on the first window's `Opened` | It must run once, whichever window comes first — a command-line start never shows the start window |
| The command-line start | Discovery before any window, then the right window straight away | Showing the start window first would flash it for a moment on every command-line start |
| A second "show the repository window" while it is shown | Ignored | A clone started from the repository window's own Integrations page asks for it again |
| Page tests and real windows | The shared test container swaps in a recording coordinator; the real one has its own tests | A page test that opened real windows would leave them open after it |

## Deviations & follow-ups

- The start window has no theme-toggle button; the theme is in its Settings page. The repository
  window keeps its toggle.
- Recommendation only: line endings were not examined; nothing in this diff showed CRLF churn.

## Build/test evidence

- `dotnet build Enigma.GitClient.slnx --no-incremental`: 0 warnings, 0 errors.
- `dotnet test --solution Enigma.GitClient.slnx`: 1833 passed, 0 failed (baseline 1819; 14 new).
