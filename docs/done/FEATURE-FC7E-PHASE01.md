# FEATURE-FC7E-PHASE01 — A commit details dialog

**Item:** FEATURE-FC7E — History: commit details, discard all
**Branch:** `feature/feature-fc7e-phase01-commit-details`
**Run:** bugfix/2026-09-28-changes-commit-details-release

## Summary

- **The diff view's header is one line:**
  - the blue back button;
  - a new details button (`Article`, "Commit details"), shown when the selection is a commit;
  - the subject, trimmed, with its full text as the tooltip.

  The author, date and hash line and the three-line description are gone from it. It is now as high
  as its buttons, where it used to grow to four lines.
- **The commit details dialog** ("Commit details", `Article` icon, one *Close* button) shows:
  - the title, as a heading;
  - the description, hidden when there is none;
  - labelled *Author* (`Name <email>`), *Date* (the full date, then how long ago it was in
    parentheses) and *Commit* (the full hash, monospace).

  Every value is a `SelectableTextBlock`, and the labels are plain `TextBlock`s. A drag selects one
  value, and nothing is a text box. The date is the author's, as the history's date column shows it.
- **How it is shown:**
  - `CommitDetailsViewModel` (from a `GitCommit` and "now") and `CommitDetailsView`;
  - `ICommitDetailsDialogService`, which shows them on the window's content dialog as About is shown,
    measuring "how long ago" on the application's `TimeProvider`;
  - `HistoryPageViewModel.ShowCommitDetailsCommand`, for the selected line, enabled for a commit only.
- **Escape under the dialog.** The dialog is opened from inside the diff view and does not take the
  focus, so the page's Escape now leaves an open dialog alone (`IsBehindOpenDialog`, from FEATURE-CC8E
  PHASE01). Before this, Escape would have put the diffs away under the dialog.

## Files / modules touched

**Created**

- `src/Enigma.GitClient.App/ViewModels/Dialogs/CommitDetailsViewModel.cs`
- `src/Enigma.GitClient.App/Views/Dialogs/CommitDetailsView.axaml`, `.axaml.cs`
- `src/Enigma.GitClient.App/Services/CommitDetailsDialogService.cs`
- `tests/Enigma.GitClient.App.UnitTests/CommitDetailsDialogTests.cs`:
  - the view model's fields;
  - no description;
  - the service's dialog and clock;
  - the view's selectable values and plain labels;
  - the description hidden when empty.
- `docs/done/FEATURE-FC7E-PHASE01.md`

**Modified**

- `src/Enigma.GitClient.App/ViewModels/Pages/HistoryPageViewModel.cs`:
  - the service and `ShowCommitDetailsCommand`;
  - `SelectedSha`, `SelectedAuthor`, `SelectedDate`, `SelectedBody` and `HasSelectedBody` removed —
    only the old header bound them.
- `src/Enigma.GitClient.App/Views/Pages/HistoryPageView.axaml` — the one-line header.
- `src/Enigma.GitClient.App/Views/Pages/HistoryPageView.axaml.cs` — the open-dialog guard on Escape.
- `src/Enigma.GitClient.App/DependencyInjection/ServiceCollectionExtensions.cs` — the service, as a
  singleton.
- `tests/Enigma.GitClient.App.UnitTests/HistoryPageTests.cs`:
  - the one-line header (a single text, the order back → details → subject, the height);
  - a click on the details button shows the selected commit;
  - no details for the uncommitted line;
  - Escape under an open dialog.
- `tests/Enigma.GitClient.App.UnitTests/ChangedFilesPanelTests.cs` — the header test asserts the
  subject and the details command instead of the removed properties.
- `docs/roadmap.md`, `docs/plan/FEATURE-FC7E.md` — statuses.

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| The header's removed properties | Deleted from the ViewModel | Nothing binds them any more; the dialog reads the row's `GitCommit` |
| The description's trailing newlines | Trimmed | A message's final newline would otherwise leave an empty line under the description |
| "Now" | The application's `TimeProvider` | The same clock as everything else that tells time. Tests pin it to 2026-01-01 00:00Z, so "3 hours ago" is exact. |
| The dialog's width | 560, in the host's 380–720 range | Room for a 72-column description and a full hash on one line. Longer values wrap. |
| Proving the Escape guard | The test was run once with the guard disabled, and it failed ("Escape put the diffs away under an open dialog") | It shows the test measures the guard |

## Deviations & follow-ups

- The plan had the header keep `SelectedSubject` only. The ViewModel's unused header properties were
  removed too, and one test was adapted (see the decisions above).
- Line endings: no CRLF churn.

## Documentation sweep

The README does not describe the diff view's header. No edit. The feature goes into the README's
features list and the release notes with FEATURE-10AA.

## Build/test evidence

- `dotnet build Enigma.GitClient.slnx`: 0 warnings, 0 errors.
- `dotnet test --solution Enigma.GitClient.slnx`: **2481 passed**, 0 failed, 0 skipped (9 new).
- Fix budget: no fix cycle. A test that re-parented the page without first taking it out of the
  window was corrected before the first suite run.
