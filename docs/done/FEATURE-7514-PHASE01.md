# FEATURE-7514-PHASE01 — Dialogs from the history toolbar

**Item:** FEATURE-7514 — Branches, tags and remotes as dialogs
**Branch:** `feature/feature-7514-phase01-tool-dialogs`
**Run:** feature/2026-09-22-home-window-merges-refresh

## Summary

Branches, Tags and Remotes have left the rail, which now holds History and Changes (plus Conflicts
while a merge waits) with Integrations and Settings in its footer. They open as dialogs from three new
buttons on the History toolbar, between the search box and the refresh button.

Each dialog is the existing page — its view, its singleton ViewModel, its filters, drag and menus —
shown on a **tool dialog host** of its own. That host is a second `ContentDialog` in the repository
window, placed under the operations host. It needs its own because every branch, tag and remote
operation asks its questions through `IContentDialogService`, which drives one host and replaces its
content each time. On that host, the first "Delete this branch?" would have wiped out the Branches
dialog. On its own host, the question opens above the dialog, and the dialog is still there once the
question is answered.

`IToolDialogService` opens the dialog at once and then runs the page's `OnAppearingAsync`, so a page
that reads git as it appears (Remotes) fills in under the reader instead of delaying the dialog. A
page that fails to appear is logged and the dialog stays open, which is what the rail's navigation
did with the same failure. The history reloads after a dialog closes **only if** a reference or HEAD
moved while it was open. This is decided by comparing a `RepositoryStateStamp` taken before and after,
so reading the tags does not cost the selected line.

## Files / modules touched

**Added — App**

- `Services/ToolDialogService.cs` — `ToolDialog`, `IToolDialogHostWindow`, `IToolDialogService`,
  `ToolDialogService`
- `Services/RepositoryStateStamp.cs` — HEAD and every reference as one comparable value

**Modified — App**

- `Views/MainWindow.axaml` (+ `.cs`) — the `ToolDialog` host under the three overlay hosts, sized from
  the window (at most 1100 × 760, 48 px clear of each edge); implements `IToolDialogHostWindow`
- `Services/AppWindows.cs` — registers the tool host with the others
- `Navigation/ShellNavigation.cs` — Branches, Tags and Remotes removed from `ShellPage` and the rail
- `ViewModels/Pages/HistoryPageViewModel.cs` — `OpenBranchesCommand`, `OpenTagsCommand`,
  `OpenRemotesCommand`, and the reload-if-moved
- `Views/Pages/HistoryPageView.axaml` — the three toolbar buttons
- `DependencyInjection/ServiceCollectionExtensions.cs` — the tool dialog service

**Tests**

- `ToolDialogTests.cs` (new) — each dialog shows its page and ViewModel until closed and is freed
  afterwards; a question asked from inside a tool opens above it on the operations host and leaves it
  open with the same content; a second tool is not shown over the first; no host means an exception;
  a page that fails to appear is logged and the dialog stays open; the toolbar's three commands open
  their dialogs on a real repository; the dialog size follows the window; the stamp changes when a
  reference or HEAD moves and only then
- `MainWindowShellTests.cs` — the rail holds History and Changes

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| Open first or appear first | Open the dialog, then run the page's appearing step | Remotes reads git as it appears; waiting for it made the dialog late, and a failure left no dialog at all |
| A page that throws while appearing | Logged; the dialog stays open | The rail's navigation reports rather than throws; a toolbar button must not be able to take the window down |
| When the history reloads | Only when the stamp moved | A reload clears the selection; opening a dialog to look is not a reason to lose it |
| The card's title | None: the page's own header is the title | The pages already carry a header with their title and actions; a second one would repeat it |
| Size | From the window, at most 1100 × 760 | The library does not size a card to its window, and a list needs a bounded height to scroll |
| A second tool while one is open | Ignored | Replacing the dialog under the reader would lose what they were doing |

## Deviations & follow-ups

- The fix budget was used in full (3 cycles), all on the new tests: an analyzer error on a blocking
  call in the stamp test; a toolbar test that found the dialog empty because the Remotes page appeared
  before the dialog opened (fixed in the service: open first); and the same page throwing on the
  test's fictitious path (fixed in the service: log it; and in the test: a real repository). The
  last two changed the service for the better, and each is now covered by a test.
- `HistoryPageViewModel` now takes 15 constructor dependencies. That was already an over-injection
  smell before this dev. Splitting the row commands out of the page would be the follow-up.
- Recommendation only: line endings were not examined; nothing in this diff showed CRLF churn.

## Build/test evidence

- `dotnet build Enigma.GitClient.slnx`: 0 warnings, 0 errors.
- `dotnet test --solution Enigma.GitClient.slnx`: 1862 passed, 0 failed (10 new).
