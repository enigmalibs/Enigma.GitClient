# FEATURE-52FB — App shell & repository opening

**Status:** TODO
**Type:** FEATURE
**Branch:** `feature/feature-52fb-<phase>-<slug>` (one per phase)
**Run:** feature/2026-09-15-enigma-git-client

## Objective

Bring up the Avalonia 12 desktop application: the Enigma Fluent theme, the `IHost` composition root,
the navigation shell with the three overlay hosts, and the first real workflow — opening, initialising
and cloning a repository, with a recent-repositories list.

## Context & constraints

- `enigma-avalonia-desktop` wiring is mandatory: `FluentTheme` in `Application.Styles`, the library
  dictionary as a `ResourceInclude` in `Application.Resources`, six services registered as singletons,
  three host controls as the **last** children of the window's root `Panel`, all five `RegisterHost` /
  `SetStorageProvider` calls before the window is shown.
- Compiled bindings are on: every view root and every `DataTemplate` carries `x:DataType`.
- ViewModels are explicit CommunityToolkit MVVM — no source generators.
- Views transient, ViewModels singleton (page state survives navigation).
- Long git operations run through `AsyncRelayCommand` with the `Overlay` service for progress and the
  `InfoBar` service for outcomes; nothing blocks the UI thread.

## PHASE01 — Avalonia shell, theme & DI

**Steps**

1. `Program.cs` — `[STAThread] Main` → `AppBuilder.Configure<App>().UsePlatformDetect()
   .WithDeveloperTools()` (Debug only) `.WithInterFont().LogToTrace()`.
2. `App.axaml` / `App.axaml.cs` — theme wiring, `Host.CreateApplicationBuilder` with
   `ContentRootPath = AppContext.BaseDirectory`, service registration, host start, window resolution,
   the five host registrations, and `host.StopAsync()` on `Exit`.
3. `ServiceCollectionExtensions.AddEnigmaServices()` (C# 14 extension block) plus
   `AddGitClientCore()` (the Core services from FEATURE-7CFD) and `AddViewsAndViewModels()`.
4. `MainWindow` — root `Panel`; a `NavigationView` rail (History, Changes, Branches, Remotes,
   Integrations in the footer, Settings in the footer); a title bar strip showing the open
   repository, its HEAD state and the in-progress operation; the three overlay hosts last.
5. `ContainerPageFactory` replacing the default `PageFactory` so pages resolve from DI, plus a
   `NavigationFailed` subscription that reports the phase through the InfoBar and the logger.
6. `IRepositoryContext` (singleton) — the currently open `RepositoryHandle`, its `HeadState`, a
   `RepositoryChanged` event, and a per-repository `SemaphoreSlim` write lock; every page observes it.
7. Application icon: a multi-resolution `Assets/app.ico` wired both as `<ApplicationIcon>` and as the
   window's `Icon`.
8. Startup health check: if `git` is missing or older than 2.20, the shell shows a blocking
   `ContentDialog` explaining what to install instead of failing later with a stack trace.
9. `Enigma*` `DynamicResource` brushes only — no hard-coded colours anywhere; window background set
   explicitly.

**Acceptance criteria**

- `dotnet build` clean, **zero warnings, including zero `AVLN` XAML warnings** (the warning count is
  read explicitly because `TreatWarningsAsErrors` does not promote them).
- The app starts on Linux and the window renders (verified headlessly by constructing the window and
  running one layout pass in a test, plus a manual smoke run).
- Unit tests: the DI container resolves `MainWindow`, `MainWindowViewModel` and every registered page
  ViewModel without throwing (a "container is valid" test that catches captive dependencies and
  missing registrations).
- Theme switching Dark ↔ Light at runtime changes the whole window in one step.

## PHASE02 — Open, init & clone repositories

**Steps**

1. Core: `IRepositoryService` — `OpenAsync(path)`, `InitAsync(path, bare: false, initialBranch)`,
   `CloneAsync(CloneRequest, IProgress<CloneProgress>, ct)` parsing `git clone --progress` stderr into
   percentages, and `ValidateCloneUrl`.
2. `IRecentRepositoryStore` — a versioned JSON file under the user config directory holding path,
   display name, last-opened timestamp and pinned flag; capped at 20 entries; missing paths are
   flagged rather than silently dropped.
3. Welcome page: recent repositories with their current branch, plus Open / Clone / Init actions.
4. Open uses `IFolderDialogService`; init asks for a folder and an initial branch name; clone asks for
   URL, target directory, optional branch and depth, and shows real progress in the `Overlay` with a
   working Cancel that kills the child process and removes a partial clone.
5. Opening a repository sets `IRepositoryContext` and navigates to History; every failure is reported
   through the `InfoBar` with git's own stderr message.

**Acceptance criteria**

- Integration tests: `InitAsync` creates a repository with the requested initial branch;
  `CloneAsync` clones a local source repository and reports monotonically increasing progress;
  cancelling mid-clone leaves no directory behind; cloning an invalid URL throws
  `GitCommandException` carrying git's message.
- Unit tests: the recent store round-trips, caps at 20, sorts by last-opened, survives a corrupt or
  future-versioned file by falling back to defaults; clone-URL validation accepts `https`, `ssh`,
  `git@host:path` and local paths and rejects anything else.
- Opening a non-repository directory shows an InfoBar error and leaves the previous repository open.

## Out of scope

- Graph, diff, branch/tag/merge UI — their own items.
- Any installer or packaging.

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| Shell layout | `NavigationView` rail + pages | The house control library's shell; gives a familiar GitKraken-like left rail for free | `DockingHost` (IDE-style docking is more than this app needs and complicates state persistence) |
| Host lifetime | `host.Start()` inside `OnFrameworkInitializationCompleted` | Avalonia's classic desktop lifetime runs the app; the host only supplies config, logging and services | `await host.RunAsync()` (never shows a window) |
| Page/VM lifetimes | Views transient, ViewModels singleton | Revisiting a page rebuilds the control but restores its state, and the visual tree does not leak | both transient (loses state); both singleton (leaks controls) |
| Recent repositories | Versioned JSON under the user config dir | No dependency, human-readable, trivially migratable | registry (Windows-only); a database (absurd) |
| Missing git | Blocking dialog at startup | A git client with no git must say so once, clearly, not fail per-command | silent degradation |
