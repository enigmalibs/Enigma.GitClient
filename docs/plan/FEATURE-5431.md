# FEATURE-5431 — A start window, one repository per window

**Status:** DONE
**Type:** FEATURE
**Branch:** one per phase, see below
**Run:** feature/2026-09-22-home-window-merges-refresh

## Objective

The application opens on a **start window**: the repositories page, plus Integrations and Settings.
When the user picks a repository, the start window gives way to the **repository window**, the "real"
application for that repository, as Visual Studio or Rider do with a project. Several instances of
Enigma.GitClient can run at the same time, each on its own repository.

## Context & constraints

- Today there is one `MainWindow` whose rail starts on `ShellPage.Repositories`; opening a repository
  navigates the same window to History (`RepositoriesPageViewModel` → `_shell.GoTo(ShellPage.History)`).
- Every page ViewModel and `IRepositoryContext` is a singleton, and the three Enigma host services
  are registered once against `MainWindow`'s host controls (`App.RegisterHosts`). The library allows
  `RegisterHost` to be called again: the last host wins.
- An Avalonia window that has been closed cannot be shown again, so windows that come and go must be
  built fresh each time (transient), while their ViewModels keep state (singletons).
- `RecentRepositoryStore` re-reads the file before every mutation (good for several processes) but
  writes it with `File.WriteAllText`; a second process reading at that moment sees half a document,
  treats it as corrupt and **moves it aside** — the recent list is lost. `SettingsService` has the same
  write shape.
- Baseline: clean build, 1819 tests green.

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| What "several instances" means | One process per repository window; running the app again gives another start window; "Open in a new window" starts a new process with the path as its argument | It is what the user asked for (instances), it is Visual Studio's model, and every singleton in the container stays valid — one process, one repository | One process with a DI scope per window (every singleton, the host services and the navigation would become per-window: a refactor the prompt did not ask for) |
| How the start window hands over | Windows are transient and built per showing; the next window is shown **before** the previous one closes; `ShutdownMode.OnLastWindowClose` | A closed window cannot be re-shown, and showing before closing keeps the application alive across the switch | Swapping the content of a single window (the user explicitly wants a first window) |
| Host services across windows | A window coordinator re-registers the dialog, overlay and info-bar hosts and both storage providers every time it shows a window | Supported by the library ("last host wins"), and nothing else changes for the services | Per-window service instances |
| The way back | "Close repository" in the repository window's strip closes the repository and returns to the start window | Visual Studio's "Close Solution" | Only quitting the app |
| Integrations and Settings | In the start window's rail, **and** kept in the repository window's footer | The prompt says they must "also" be in the start window | Removing them from the repository window |
| The start window's navigation | Its own `NavigationView` over a second, keyed `INavigationService` | Same control and page factory as the repository window, no second navigation mechanism | A hand-rolled tab strip |
| The git-availability check | Runs when the start window first opens | It is the first window now | Keeping it on the repository window |
| A path on the command line | `Enigma.GitClient.App <path>` opens straight into that repository; a path that is not a repository opens the start window and says so | Needed for "Open in a new window", and useful from a terminal | No arguments at all |
| Config files shared by several processes | Atomic writes — temporary file in the same directory, then `File.Move(…, overwrite: true)` — for the recent list and the settings | A concurrent reader must see the old document or the new one, never half of one | A cross-process lock file (heavier, and not needed for last-writer-wins files) |

## PHASE01 — Start window and repository window

**Branch:** `feature/feature-5431-phase01-start-window`
**Status:** DONE — see `docs/done/FEATURE-5431-PHASE01.md`

### Steps

1. A `StartWindow` (Views) + `StartWindowViewModel` (ViewModels): a rail with Repositories, and
   Integrations and Settings in the footer, driven by a keyed `INavigationService`; smaller than the
   repository window (about 1100 × 720); the three overlay hosts as the last children of its root panel.
2. A window coordinator service (`IAppWindows` / `AppWindows`) that owns the desktop lifetime's windows:
   `ShowStart()` and `ShowRepository()` build the window from the container, set its DataContext,
   re-register every host, show it, set it as the lifetime's `MainWindow`, and only then close the
   window it replaces. `ShutdownMode.OnLastWindowClose`.
3. `RepositoriesPageViewModel` asks the coordinator for the repository window instead of navigating the
   shell to History, for open, open-recent, create and clone.
4. The repository window (`MainWindow`) drops Repositories from its rail and starts on History; its strip
   gets a "Close repository" button that closes the repository in the context and asks the coordinator
   for the start window. Integrations and Settings stay in its footer.
5. `App.OnFrameworkInitializationCompleted` shows the start window — or, when the first argument is a
   folder that opens as a repository, the repository window directly; otherwise the start window with an
   info bar saying the path was not a repository. The git check moves to the start window's first open.
6. Views and windows are registered transient; their ViewModels stay singletons.
7. Tests: the coordinator swaps windows and re-registers hosts; opening a recent repository shows the
   repository window on History; "Close repository" returns to the start window with no repository
   open; the start window's rail holds Repositories, Integrations and Settings; the repository window's
   rail no longer holds Repositories; the command-line path is honoured and a bad one falls back. The
   existing shell tests are updated to the new starting page.

### Acceptance criteria

- The app starts on a window that holds Repositories, Integrations and Settings only.
- Picking a repository replaces it with the repository window, on History, for that repository.
- "Close repository" returns to the start window; closing the repository window exits the app.
- A folder path on the command line opens straight into it.
- Build clean with zero warnings; the whole suite green.

## PHASE02 — Several instances side by side

**Branch:** `feature/feature-5431-phase02-instances`
**Status:** DONE — see `docs/done/FEATURE-5431-PHASE02.md`

### Steps

1. An `IInstanceLauncher` service that starts another instance of this executable
   (`Environment.ProcessPath`, argument list — no shell), optionally with a repository path.
2. The recent-repository rows get "Open in a new window"; the repository window's strip gets a
   "New window" button that starts another instance on its start window.
3. `RecentRepositoryStore.Write` and `SettingsService`'s write go through a temporary file in the same
   directory and an atomic move, so a concurrent reader never reads half a document.
4. Tests: the launcher is asked for the right path (a recording double); the stores write atomically (no
   temporary file left behind, the document reads back whole); a second store instance over the same
   directory sees what the first wrote.

### Acceptance criteria

- Two instances can run at once, each on its own repository.
- "Open in a new window" leaves the current window as it is and starts another instance on that
  repository.
- The recent list survives two instances writing to it.
- Build clean with zero warnings; the whole suite green.

## Out of scope

- Several repositories inside one process.
- A single-instance mode or a "bring the existing window forward" behaviour.
- Live reloading of settings changed by another running instance (the last writer wins).
