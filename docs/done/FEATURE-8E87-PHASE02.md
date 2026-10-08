# FEATURE-8E87-PHASE02 — Bulk delete for tags and remotes

**Item:** FEATURE-8E87 — Compact ref rows with multi-select
**Phase:** PHASE02 — Bulk delete for tags and remotes
**Branch:** `feature/feature-8e87-phase02-bulk-delete-tags-remotes`
**Run:** feature/2026-10-08-title-watcher-refs-release

## Summary

Several tags or remotes can be selected and deleted at once.

- **Selection:** both lists take `SelectionMode="Multiple"`, their `SelectedItems` bound to the page
  (`SelectedTags`, `SelectedRemotes`). Avalonia gives the gestures natively:
  - Ctrl+click, Shift+click and Ctrl+A;
  - a right-click outside the selection selects that line, and one inside keeps the selection.

  The selection is restored by name across every rebuild, as the single selection was.
- **The menus follow the selection.** A shared `LineSelection` (the count and `IsMultiple`) is
  referenced by every line, and its changes reach menus that are already built.
  - With several lines selected, only the deletes show: *Delete locally…* and *Delete from the
    remote…* for tags, *Remove…* for remotes.
  - With one, everything is back.
  - A line's delete acts on the whole selection when the line is part of it, and on the line alone
    otherwise.
- **The header and the keyboard:**
  - a *Delete (n)* button on Tags (deletes locally) and a *Remove (n)* button on Remotes;
  - red while there is something to delete, as the working tree's discard is;
  - disabled with nothing selected, when it just says *Delete* or *Remove*;
  - the Delete key on the list does the same, through a `KeyBinding`.
- **`Services/BulkDeletion.cs`**, shared by both pages and by the branches in PHASE04:
  - **`DeletionItem`:** a name, a note, detail lines, and a skip reason.
  - **`DeletionPlan`:** the one question.
    - `ForOne` keeps a single delete's own wording.
    - `ForMany` lists every item on a line of its own, with its note, details or "skipped: reason",
      then the warning.
  - **`DeletionOutcome`:** what went and what did not, with reasons. Its summary is a success when
    everything went, a warning when some did, and an error when none did.
  - **`DeletionBatch.RunAsync`:**
    - every item is tried under one `RunExclusiveAsync`, and a failure is recorded without stopping
      the batch;
    - the reason is the refusal's message, the sync failure's, or git's first meaningful line;
    - the reference state is read once at the end.
- **`ITagOperations`:**
  - `DeleteAsync(IReadOnlyList<string>)` and `DeleteRemoteAsync(IReadOnlyList<string>)`. The remote
    one asks the push guard once, and its question for several ends with *This changes "origin" for
    everyone who uses it.*
  - The single-name methods are batches of one, with today's titles, questions and failure
    messages, and the remote delete's success sentence. The history's tag badges go through them.
- **Remotes:** the same flow for removing several, each listed with its tracking references,
  followed by *Nothing on the remotes themselves is touched.* A single remove keeps its question.
- **Long questions scroll.** `ConfirmDestructiveAsync` caps a question of 14 lines or more at 560 px,
  so it scrolls inside its card, and gives the shared host its height back afterwards.

## Files / modules touched

**Created**

- `src/Enigma.GitClient.Desktop/Services/BulkDeletion.cs`
- `src/Enigma.GitClient.Desktop/ViewModels/Pages/LineSelection.cs`
- `tests/Enigma.GitClient.Desktop.UnitTests/BulkDeletionTests.cs`: 7 tests.
  - The question for several (notes, details, skipped, warning) and for one.
  - The three summaries.
  - The batch: every item tried past a refusal, under one hold, with one refresh; and git's first
    meaningful line.
- `docs/done/FEATURE-8E87-PHASE02.md`

**Modified**

- `src/Enigma.GitClient.Desktop/Services/TagOperations.cs`: the batch methods; the single ones on top
  of them.
- `src/Enigma.GitClient.Desktop/Services/ContentDialogServiceExtensions.cs`: long questions scroll.
- `src/Enigma.GitClient.Desktop/ViewModels/Pages/TagsPageViewModel.cs`:
  - `SelectedTags`, `Selection`, `DeleteSelectionLabel`, `DeleteSelectionCommand`;
  - the line commands acting on the selection;
  - the selection kept across rebuilds;
  - `TagRowViewModel.Selection`.
- `src/Enigma.GitClient.Desktop/ViewModels/Pages/RemotesPageViewModel.cs`:
  - `SelectedRemotes`, `Selection`, `RemoveSelectionLabel`, `RemoveSelectionCommand`;
  - the batch remove;
  - `RemoteRowViewModel.Selection`.
- `src/Enigma.GitClient.Desktop/Views/Pages/TagsPageView.axaml`, `RemotesPageView.axaml`: multiple
  selection, the key binding, the header button, the menus by selection.
- `tests/Enigma.GitClient.Desktop.UnitTests/TagsPageTests.cs`: 6 tests.
  - Several tags deleted after one question, with one refresh and the summary.
  - A partial failure: a tag deleted in a terminal meanwhile.
  - The button's label and state.
  - A line's menu acting on the whole selection.
  - The menu with several selected: only the deletes.
  - The Delete key.
- `tests/Enigma.GitClient.Desktop.UnitTests/RemotesAndSyncTests.cs`: 3 tests.
  - Several remotes removed after one question.
  - Several tags deleted from the remote, with the warning.
  - The remotes menu with several selected.
- `docs/roadmap.md`, `docs/plan/FEATURE-8E87.md`: statuses.

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| Which selection the commands use | A view-model list bound to the list's `SelectedItems`, kept beside the single `SelectedItem` | Avalonia keeps both in step; the single selection's tests and bindings are unchanged |
| A menu item's visibility by selection | Bound to the shared `LineSelection.IsMultiple`; the push uses a `MultiBinding` with `CanPush` | A computed row property raises nothing when the selection changes, so an open menu would go stale |
| A line's delete when the line is selected along with others | Deletes the whole selection | A right-click inside the selection keeps it, so the menu speaks for all of it |
| The confirmation's colour | Red (`ConfirmDestructiveAsync`) for every batch, a single delete included | One path, and a delete is what the red is for |
| A single delete's success | Silent, as before; the remote tag delete keeps its sentence | "The same path" without changing what a single delete says |
| A long question | Scrolls inside a card capped at 560 px | Fifty names would otherwise push the buttons off the window; `DialogMaxHeight` is restored, because the host is shared |
| The Delete key | A `KeyBinding` on the list | Bindings run from the focused line upwards, which is where a click leaves the focus |

## Deviations & follow-ups

- **Not in the plan, needed:** long questions scroll.
- **The header buttons are red** (`toolbar danger`) while enabled. The plan named the buttons but not
  their colour; the house's other button that loses something, the working tree's discard, is red.
- **Follow-up:** the remote tag delete asks the push guard once for the batch. The remote branch
  delete (PHASE04) keeps today's behaviour, which asks no guard; that difference already existed.
- I looked at the rendered snapshots: both headers show the disabled button with nothing selected.
- Line endings: the touched files are LF; no CRLF churn.

## Documentation sweep

Nothing to change: the README's tag line ("delete one here or on the remote…") is still true, and
deleting several is a 6.0.0 release-note item (FEATURE-AE9F).

## Build/test evidence

- `dotnet build Enigma.GitClient.slnx -c Debug --no-incremental`: 0 warnings, 0 errors.
- `dotnet test --solution Enigma.GitClient.slnx -c Debug`: 3000 total, 2999 passed, 1 skipped,
  0 failed.
- Fix budget: 0 cycles. During development, the Delete-key test first focused the list instead of a
  line. A probe showed the binding works from a focused line, which is where a click leaves the focus.
