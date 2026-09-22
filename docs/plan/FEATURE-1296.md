# FEATURE-1296 — Automatic fetch and refresh

**Status:** TODO
**Type:** FEATURE
**Branch:** one per phase, see below
**Run:** feature/2026-09-22-home-window-merges-refresh

## Objective

While a repository is open, it is fetched and fully refreshed automatically, every 15 seconds by
default. The interval is configurable in the settings, and the automatic refresh can be turned off.

## Context & constraints

- `ISyncOperations.FetchAsync` shows a progress overlay and a success info bar. Doing that every 15
  seconds would make the window unusable, so the automatic fetch has to be quiet.
- `HistoryPageViewModel.ReloadAsync` clears every row and the selection. Running it every 15 seconds
  would throw the reader's place away four times a minute.
- `IRepositoryContext.RunExclusiveAsync` serialises writes behind a semaphore. A tick that waits
  behind a user's operation would pile up behind it.
- `AppSettings` is versioned (4). A new key read from an older file simply takes its default.

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| The setting | `AutoRefreshSeconds`, default 15, 0 = off, otherwise clamped to 5–3600 | One number covers "configurable" and "off"; a floor keeps a typo from fetching continuously | A separate on/off switch plus an interval |
| Schema version | Unchanged (4) | A missing key takes the default; nothing needs migrating | A version 5 with an empty migration |
| What a tick does | A quiet fetch (no overlay, no info bar), then a reference refresh, then — only when refs, HEAD or the working tree's dirty state changed — a history reload that keeps the selection by SHA | Nothing moves on screen when nothing changed | Reloading every page on every tick |
| Failures | Logged only. The next tick tries again | An offline laptop would otherwise get an error bar every 15 seconds | An info bar per failure |
| Overlap | A tick is skipped when the previous one is still running, or when the repository is busy with another operation (a non-waiting try on the write lock) | Ticks must never queue up behind the user | Waiting for the lock |
| Timer | A `TimeProvider`-driven periodic loop owned by a singleton service, started when a repository opens and stopped when it closes; the interval follows the setting live | Testable with a fake time provider, no UI-thread timer in a service | `DispatcherTimer` |
| Other pages | Whatever observes `StateRefreshed` (branches, tags, the shell's counters) follows automatically. The Changes page re-reads its status on the tick while it is the page on screen | "Full refresh", without re-reading pages nobody is looking at | Re-reading every page every tick |

## PHASE01 — The interval setting

**Branch:** `feature/feature-1296-phase01-interval-setting`
**Status:** TODO

### Steps

1. `AppSettings.AutoRefreshSeconds` (default 15) and its clamp in `Normalised()` (0 stays 0; anything
   else is clamped to 5–3600).
2. `SettingsPageViewModel.AutoRefreshSeconds` and a settings card under Git: "Fetch and refresh
   automatically every … seconds (0 turns it off)".
3. Tests: the default; the clamp (0, 1, 15, 99999); a file without the key reads the default; the page
   writes the setting.

### Acceptance criteria

- Settings shows the interval, 15 by default, and 0 turns the automatic refresh off.
- Build clean with zero warnings; the whole suite green.

## PHASE02 — The periodic fetch and refresh

**Branch:** `feature/feature-1296-phase02-auto-refresh`
**Status:** TODO

### Steps

1. `IRepositoryContext.TryRunExclusiveAsync` — the same as `RunExclusiveAsync`, but it returns without
   running when the lock is taken.
2. `ISyncOperations.FetchQuietlyAsync` — fetch with no overlay and no info bar; failures are logged and
   reported as `false`.
3. `IAutoRefreshService` / `AutoRefreshService` (singleton, `TimeProvider`): starts with the repository
   window, ticks at the configured interval, skips overlapping or busy ticks, fetches quietly, refreshes
   the context, and raises `Refreshed(changed)`.
4. `HistoryPageViewModel`: on a changed refresh, a soft reload that keeps the selected commit (by SHA)
   and asks the view to keep its scroll offset; on an unchanged one, nothing.
5. `ChangesPageViewModel`: re-reads its status on a tick while it is the page on screen.
6. Tests with a fake time provider: ticks at the interval; 0 means no ticks; a changed interval takes
   effect; a busy repository skips the tick; a failing fetch is quiet and the next tick runs; an
   unchanged refresh does not reload the history; a changed one keeps the selection.

### Acceptance criteria

- With a repository open, the application fetches and refreshes every 15 seconds by default, following
  the setting, and not at all at 0.
- Nothing on screen moves when nothing changed, and the selected commit survives a refresh that did
  change something.
- An automatic fetch never shows an overlay or an info bar.
- Build clean with zero warnings; the whole suite green.

## Out of scope

- Pausing while the window is inactive or minimised.
- File-system watching of the working tree.
- Backing off exponentially when the fetch keeps failing.
