# FEATURE-FC7E — History: commit details, discard all

**Status:** TODO
**Type:** FEATURE
**Branch:** one per phase, see below
**Run:** bugfix/2026-09-28-changes-commit-details-release

## Objective

- The History diff view's header no longer holds the commit's description; it is one line — the back
  button, a new details button, the subject.
- The details button opens a dialog with the commit's title, description, author name and email,
  date, how long ago that was, and full SHA, all selectable as text (not in text boxes).
- The same dialog opens from a history line's context menu.
- The history's *Uncommitted changes* line offers *Discard uncommitted files…*, confirmed with a red
  button.

## Context & constraints

- **The header** (`HistoryPageView.axaml`, `DiffPage`) is a grid of the back button and a stack of
  three lines:
  - `SelectedSubject`;
  - `SelectedAuthor` · `SelectedDate` · `SelectedSha`;
  - `SelectedBody`, up to three lines.
- **The commit** is `CommitRowViewModel.Commit` (`GitCommit`: `Subject`, `Body`, `Author`
  (`GitSignature`: `Name`, `Email`, `When`, `ToString()` = `Name <email>`), `Sha`), in memory — no git
  call is needed. `RelativeTime.Format` / `FormatAbsolute` format the date as the list does.
- **Dialogs.** Dialogs are shown by small services so no ViewModel builds a view —
  `IAboutDialogService` over `IContentDialogService`, with one *Close* button. `SelectableTextBlock`
  gives selectable, non-editable text.
- **The line menu** is data: `CommitRowViewModel.MenuEntries` builds `HistoryMenuEntry(Header, Command,
  Parameter, Icon)` items from `HistoryRowCommands`.
  - It starts with "Show what it changed".
  - A stash line gets its own short menu.
  - The uncommitted line (no `Commit`) gets "Stash all changes…".
- **Discarding.**
  - `IStagingService.DiscardAsync(paths)` restores tracked paths from HEAD and deletes untracked
    ones. It classifies a path by the index, so a staged rename's old path (gone from the index) would
    never be restored: a whole-tree discard needs its own operation.
  - Writes run through `IRepositoryContext.RunExclusiveAsync`, and operations report on the info bar
    (`StashOperations` is the model).
- **The red confirm.** It is FEATURE-CC8E PHASE02's `ConfirmDestructiveAsync`.

## PHASE01 — A commit details dialog

**Branch:** `feature/feature-fc7e-phase01-commit-details`
**Status:** TODO

### Steps

1. `ViewModels/Dialogs/CommitDetailsViewModel.cs` — built from a `GitCommit` and "now":
   - `Subject`, `Body`, `HasBody`;
   - `Author` (`Name <email>`);
   - `Date` (`FormatAbsolute`) and `DateWithAge` (`<date> (<relative>)`);
   - `Sha`.
2. `Views/Dialogs/CommitDetailsView.axaml` — the subject as a heading, the body below it, then labelled
   *Author*, *Date* and *Commit* lines.
   - Every value is a `SelectableTextBlock`; the labels are not.
   - The SHA is monospace; long values wrap.
3. `Services/CommitDetailsDialogService.cs` — `ICommitDetailsDialogService.ShowAsync(GitCommit)` shows
   it titled "Commit details", with the `Article` icon and one *Close* button. It is registered as a
   singleton.
4. `HistoryPageViewModel`: `ShowCommitDetailsCommand` for the selected line, enabled only when it is a
   commit.
5. `HistoryPageView.axaml`: the header becomes one line:
   - the blue back button;
   - a `toolbar` details button (`Article`, "Commit details"), shown when the selection is a commit;
   - `SelectedSubject`, trimmed, with its full text as the tooltip.

   The author/date/SHA line and the body leave the header.
6. Tests:
   - the view model's fields, a commit with no body, and the date with its age;
   - the service shows the view with one *Close* button;
   - the header's button opens it for the selected commit and is hidden for the uncommitted line;
   - the header holds no body or author line;
   - the values are `SelectableTextBlock`s.

### Acceptance criteria

- The History diff view's header is one line: back, details, subject.
- The details button opens a dialog with the title, the description (when there is one), `Name
  <email>`, the date followed by how long ago it was in parentheses, and the full SHA — each selectable
  and copyable, none in a text box.
- Build clean with zero warnings; the whole suite green.

## PHASE02 — Details from the line's menu

**Branch:** `feature/feature-fc7e-phase02-details-in-menu`
**Status:** TODO

### Steps

1. `HistoryRowCommands`: a `ShowDetails` command (`AsyncRelayCommand<CommitRowViewModel>`, a commit
   only), wired by `HistoryPageViewModel` to the same service.
2. `CommitRowViewModel.MenuEntries`: "Show commit details" (`Article`) right after "Show what it
   changed", on every line that is a commit — stash lines included — and not on the uncommitted line.
3. Tests:
   - the entry is second on a commit line and on a stash line, and absent on the uncommitted line;
   - executing it shows the dialog for that line's commit, whichever line is selected.

### Acceptance criteria

- Right-clicking a history line that is a commit offers "Show commit details", which opens the same
  dialog for that commit.
- Build clean with zero warnings; the whole suite green.

## PHASE03 — Discard from the uncommitted line

**Branch:** `feature/feature-fc7e-phase03-discard-uncommitted`
**Status:** TODO

### Steps

1. Core, `IStagingService.DiscardAllAsync(repository)`:
   - every tracked path goes back to HEAD in the index and the work tree
     (`git restore --source=HEAD --staged --worktree -- .`), staged additions and renames included;
   - then every untracked file and directory is deleted (`git clean -f -d -- .`). Ignored files are
     kept.
2. App, `Services/DiscardOperations.cs` — `IDiscardOperations.DiscardUncommittedAsync()`:
   - it reads the status to name how many files it throws away;
   - it asks through `ConfirmDestructiveAsync` — "Discard uncommitted files", "Throw away every
     uncommitted change — *N files*, staged or not, untracked files included? This cannot be undone.",
     confirm button "Discard";
   - it runs `DiscardAllAsync` exclusively, and reports success or failure on the info bar.

   It is registered as a singleton.
3. `HistoryPageViewModel`: `HistoryRowCommands.DiscardUncommitted` for the uncommitted line — refused
   on an unborn HEAD and while a merge or another operation is in progress — reloading the history
   when it discarded.
4. `CommitRowViewModel.MenuEntries`: "Discard uncommitted files…" (`Trash`) after "Stash all
   changes…" on the uncommitted line.
5. Tests:
   - Core integration — modified, staged, staged-renamed, untracked (file and directory) and ignored
     files: every change is gone, the rename's source is back, and the ignored file stays;
   - App — the entry is on the uncommitted line only;
   - App — the question is red and names the count; confirming leaves the tree clean and reloads the
     history without the uncommitted line; cancelling changes nothing;
   - App — the command is disabled during a merge.

### Acceptance criteria

- Right-clicking the history's *Uncommitted changes* line offers "Discard uncommitted files…"; it asks
  a confirmation with a red *Discard* button, and confirming throws away every uncommitted change.
- It cannot run while a merge is in progress or before the first commit.
- Build clean with zero warnings; the whole suite green.

## Out of scope

- Editing a commit's message or author (the dialog is read-only).
- The committer, the parents or the signature in the dialog.

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| What stays in the header | Back, details button, the subject on one line | The header shrinks to one line, and the reader still sees which commit this is; everything else is one click away, in the dialog | Only the two buttons (loses the context); dropping just the body (still two lines) |
| The button's icon | Phosphor `Article` | Info already means About in the same window's strip; a document with lines reads as "the commit's text and details" | `Info` (two meanings side by side); `Receipt` |
| Where the dialog is built | An `ICommitDetailsDialogService`, as About is | No ViewModel builds a view; testable with the scripted dialog service | Building it inside `HistoryPageViewModel` |
| Selectable, not in text fields | `SelectableTextBlock` per value | Selectable and copyable, reads as text, as asked | Read-only `TextBox` (a text field); one block for everything (the labels would be selected too) |
| Which date | The author date, as the list's date column shows | The list and the dialog must agree | The committer date |
| The description when empty | Hidden | Nothing to say; a placeholder is noise | "No description" |
| The menu entry's reach | Every commit line, stashes included; not the uncommitted line | A stash is a commit with a message and an author; the uncommitted line has neither | Commits only |
| "Uncommitted files" | The history's *Uncommitted changes* line, discarding everything uncommitted | That line's menu is where uncommitted work is acted on ("Stash all changes…" is there); the Changes page's lines already have *Discard…* | A new menu on the Changes page |
| How to discard everything | A Core `DiscardAllAsync` (`restore --source=HEAD --staged --worktree`, then `clean -f -d`) | Whole-tree and rename-safe; `DiscardAsync` classifies by the index and misses a rename's source | Feeding every status path to `DiscardAsync` |
| When it is refused | Unborn HEAD; a merge or another operation in progress | No commit to go back to; discarding mid-merge would leave a merge that records nothing of the other side — *Abandon the merge* is the way out | Allowing it everywhere |
