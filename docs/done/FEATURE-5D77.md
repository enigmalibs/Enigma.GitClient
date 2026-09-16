# FEATURE-5D77 — Settings, preferences & docs

**Item:** FEATURE-5D77 — Settings, preferences & docs
**Branch:** `feature/feature-5d77-settings-and-docs`
**Run:** feature/2026-09-15-enigma-git-client

## Summary

The preferences the rest of the client had been keeping per session now have a file, a page, and an
effect.

`AppSettings` is one versioned record holding everything the application remembers — theme, history
page size, first-parent history, date style, graph row height and lane width, the file list's shape
and auto-expand limit, the diff's shape, context lines, whitespace handling, tab width and wrapping,
the pull strategy, and the path to git. Every property has a default, and `Normalised()` clamps a
hand-edited file into a range the application can actually run on, because someone typing `0` for
the page size should not be the end of the history view.

`SettingsService` reads and writes it as plain JSON in the user's configuration directory. Writes
are debounced, so dragging a number does not rewrite the file forty times; a burst of changes writes
once. A file from a newer build is read for the keys this build understands rather than discarded,
an older one goes through a migration hook, and one that cannot be read at all is **moved aside**
rather than overwritten — it is the user's file, and it may be the only copy of something they
edited by hand.

The page is built from `SettingsCard` and `SettingsCardExpander`, grouped into Appearance, History
and graph, Changed files, Diff, Git and About, with a confirmed "Reset to defaults". Every group
takes effect immediately: the theme repaints, an open diff viewer changes shape and re-reads its
patch when the context changes, an open file panel switches between list and tree, and the graph's
row height and lane width move under the rows that are already drawn. The path to git is the one
exception, and the page says so where it is typed.

About carries the version, the git the client actually found, the Avalonia it draws with, the
licence, where the preferences live, and the product's two permanent exclusions in one sentence.

The documentation is finished: the README gains the preferences, the per-host token scopes (from
PHASE03) and a table of every file the client keeps about you, and `RELEASENOTES.md` is seeded with
the 1.0.0 entry.

## Files / modules touched

**Created — Core**

- `Configuration/AppSettings.cs` — `ThemePreference`, `DateDisplay`, `FilesView`, `DiffView` and the
  settings record with its defaults and `Normalised()`
- `Configuration/SettingsService.cs` — `ISettingsService`, `SettingsChangedEventArgs`, and the
  debounced, versioned, backing-up implementation

**Created — App**

- `ViewModels/Pages/SettingsDescriptions.cs` — what each choice is called in the interface

**Modified — App**

- `ViewModels/Pages/SettingsPageViewModel.cs` — the whole page
- `Views/Pages/SettingsPageView.axaml` — the cards, the About card and the reset
- `App.axaml.cs` — loads the settings before the window and applies the theme, then follows it
- `ViewModels/Panels/DiffViewerViewModel.cs`, `ViewModels/Panels/ChangedFilesPanelViewModel.cs`,
  `ViewModels/Pages/HistoryPageViewModel.cs`, `ViewModels/Pages/ChangesPageViewModel.cs`,
  `ViewModels/Pages/CommitRowViewModel.cs`, `Views/Pages/HistoryPageView.axaml` — every consumer
- `Services/SyncOperations.cs` — pulls with the strategy the user chose

**Modified — Core**

- `DependencyInjection/ServiceCollectionExtensions.cs` — the settings service, and the git path
  applied to `GitExecutableOptions`

**Modified — docs**

- `README.md` — the preferences, and where the client keeps your things
- `RELEASENOTES.md` — the 1.0.0 entry
- `docs/roadmap.md`, `docs/plan/FEATURE-5D77.md`

**Created — tests**

- `tests/Enigma.GitClient.Core.UnitTests/Configuration/SettingsServiceTests.cs` — 17 cases
- `tests/Enigma.GitClient.App.UnitTests/SettingsPageTests.cs` — 15 cases, including a rendered frame

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| Where the preference lives | Only in the store | The page's properties read and write `ISettingsService` rather than holding a copy, so a preference changed anywhere is the same preference and nothing has to be pushed back when the page closes |
| Out-of-range values | Clamped, not refused | A hand-edited file is the ordinary case, and a client that will not start because someone typed `0` is a worse client than one that quietly uses 50 |
| An unreadable file | Moved aside, defaults loaded | Never block startup on a file the user can delete, and never destroy what they wrote |
| Writing | Debounced, with a flush | A slider produces a change per pixel; the file is written once the user has stopped |
| The file list's default | Tree, which is what it already was | Adding a preference must not quietly change what the application does today — the test that caught it is the one that stages a whole directory |
| The theme at startup | Applied before the window is built | A window that paints dark and then flips to light is a worse first impression than one that starts right |
| "Follow the system" | Avalonia's `ThemeVariant.Default` | It is already the framework's word for the same thing; a third variant of our own would only have to be translated back |
| The path to git | Applied when the options are first resolved | git is found once per run, so changing the path takes a restart. The page says so rather than pretending otherwise |
| The pull strategy | Two options, and the page says why | Rebase is the product's one hard exclusion, and a preferences page is exactly where someone would look for it |
| A viewer's own toolbar | Changes that viewer only | The toolbar is a per-view adjustment; rewriting the preference behind every other pane from a toggle in one of them would be a surprise |

## Deviations & follow-ups

- **Deviation:** the plan listed "confirmation toggles for destructive actions". They are not here,
  deliberately: every destructive action in this client already confirms, and a preference that
  turns those off is the kind of setting that loses someone's work. The dialogs stay.
- **Deviation:** the plan described `AppSettings` as consolidating what earlier devs had persisted.
  In fact nothing was persisted before this item — the view modes and diff options lived for as long
  as the window did — so this dev defines them and wires them for the first time.
- **Follow-up:** `GitExecutablePath` takes effect on restart. Making it live would mean re-resolving
  the executable and invalidating the cached availability probe, which is a change to the git engine
  rather than to the settings.
- **Follow-up:** the settings page has no search box. With six groups it does not need one; with
  twelve it would.
- **Follow-up:** localisation. Every string here is English, as the plan says.
- **Line endings (recommendation only):** no CRLF churn observed. No action taken.

## Build/test evidence

```
dotnet build Enigma.GitClient.slnx --no-incremental
  0 Warning(s)
  0 Error(s)

dotnet test --solution Enigma.GitClient.slnx
  Test run summary: Passed!
  total: 1564  failed: 0  succeeded: 1564  skipped: 0
```

32 tests are new in this dev. Most of them change something on the page and then look at the thing
it governs — an open diff viewer, an open file panel, the history page's own metrics — because a
settings page that writes a file nobody reads is the way this feature usually fails. The rendered
frame is saved as `snapshots/settings-page-dark.png` and was looked at; the cards were a column of
their content's width until that picture said so.
