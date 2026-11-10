# BUG-FACC — App crashes at startup: DI cycle

**Status:** DONE
**Type:** BUG
**Branch:** `bugfix/bug-facc-startup-di-cycle`
**Run:** bugfix/2026-11-10-startup-di-cycle

## Objective

The application dies before its first window: `App.OnFrameworkInitializationCompleted` resolves
`IUnhandledErrorReporter`, and the container throws `InvalidOperationException: A circular dependency
was detected for the service of type 'IAppWindows'`. The application starts again, and the container
holds no cycle.

## Context & constraints

- The cycle: `AppWindows → IRepositoryOpener (RepositoryOpener) → IAutoRefreshService
  (AutoRefreshService) → ISyncOperations (SyncOperations) → IInstanceLauncher (InstanceLauncher) →
  IAppWindows`.
- Introduced by `86b71ca` (FEATURE-6693 PHASE06, push guard for submodule commits): `SyncOperations`
  took an optional `IInstanceLauncher` to offer "Open <submodule>". Microsoft.Extensions.DI resolves an
  optional parameter whenever the service is registered, which closed the loop.
- `InstanceLauncher` takes `IAppWindows` only to read `CurrentWindow` when `Launch` runs, to start the
  new instance on that window's screen (FEATURE-CA9D PHASE03). Nothing needs it at construction.
- One cycle, many symptoms: a scratch harness that mirrors `CompositionRootTests.BuildProvider`
  (`ValidateOnBuild`, `ValidateScopes`) lists 28 failing descriptors on `5d90409`, every chain through
  the same four services. `Container_ValidatesOnBuild` is the regression test that was already there;
  it never ran because the Desktop suite is not run on Windows here (BUG-6EAA).
- `AppWindows` already takes `IServiceProvider` and resolves what it needs when it needs it.
- No test constructs `InstanceLauncher` directly; `BuildStartInfo` (static) keeps its tests.

## Steps

1. `Services/InstanceLauncher.cs`: replace the constructor's `IAppWindows` with `IServiceProvider`.
   Resolve `IAppWindows` inside `Launch` with `GetRequiredService`, and add a comment explaining why
   the window service is resolved late (the cycle it would otherwise close). Update the XML docs.
2. Nothing else changes: `SyncOperations`, the registrations and the other launcher consumers stay
   as they are.

## Acceptance criteria

- The harness (same container options as `CompositionRootTests`) reports `ValidateOnBuild: OK` and
  resolves `IUnhandledErrorReporter`, `IAppWindows`, `IInstanceLauncher` and `ISyncOperations`.
- Another instance still opens on the screen of the window that asks: `Launch` reads
  `IAppWindows.CurrentWindow` exactly as before.
- The build has zero warnings; Core unit and integration suites are green against the baseline;
  the Desktop suite compiles (not run here: the machine's standing rule).

## Out of scope

- Re-arranging the services' dependency graph further (e.g. a separate "current window" service).
- Making the Desktop suite runnable on Windows (BUG-6EAA).

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| Item type | BUG, single phase | A startup crash with one root cause; one small reviewable commit | A FEATURE-6693 phase (that item is `DONE`; a crash deserves its own record) |
| Which edge to cut | `InstanceLauncher → IAppWindows` | It is the inverted edge: a process launcher depending on the window manager at the top of the graph. Cutting it frees every launcher consumer (sync, worktrees, submodules, the panels), so the next service that offers "open in a new window" cannot close the loop again | Dropping the launcher from `SyncOperations` (loses the PHASE06 "Open <submodule>" button, and the next consumer reopens the cycle); deferring in `AutoRefreshService` or `RepositoryOpener` (their edges are the legitimate ones) |
| How to defer | `IServiceProvider`, `IAppWindows` resolved inside `Launch` | Same pattern `AppWindows` already uses; no new registration or abstraction; one file | `Lazy<IAppWindows>` with a factory registration (a pattern new to the codebase for one consumer); a new "current window" service written by `AppWindows` (a new abstraction and a wider diff for a crash fix) |
| Regression test | The existing `Container_ValidatesOnBuild`, checked with the scratch harness before and after | It already fails on the cycle; an extra resolve test would only repeat it | A new `CompositionRootTests` theory for the startup services (redundant with `ValidateOnBuild`) |
