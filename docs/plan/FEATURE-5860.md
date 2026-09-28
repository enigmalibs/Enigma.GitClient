# FEATURE-5860 — Context menus that do more

**Status:** IN PROGRESS
**Type:** FEATURE
**Branch:** one per phase, see below
**Run:** bugfix/2026-09-28-history-stash-menus-release

## Objective

- The menus of the branches, tags and remotes lines open wherever the line is right-clicked, not only
  over its text.
- Copy a branch's or a tag's name from its badge in the history, and a commit's short or full hash from
  its line.
- Select a branch's or a tag's line in the history from the branches and tags dialogs.
- Icons on the important operations of every context menu in the application.

## Context & constraints

- **Whole-line menus.** `BranchesPageView`, `TagsPageView` and `RemotesPageView` put the row's
  `ContextMenu` on an inner `Grid` with no background, inside a `Border` that has
  `Background="Transparent"`: a panel with no brush is not hit-testable where it has no child, so only
  the text opens the menu (FEATURE-3030 PHASE01 fixed the same defect in the history). The Changes
  page's stash rows have the same shape.
- **History menus.** A branch badge has its own menu (`HistoryPageView.axaml`); a tag badge
  (`RefBadgeItem`) has none. The line's menu is data (`CommitRowViewModel.MenuEntries` →
  `HistoryMenuEntry(Header, Command, Parameter)`), rebuilt before it opens.
- **Clipboard.** `ISystemInterop.CopyTextAsync`.
- **Dialogs.** The branches and tags pages are shown over the history by `IToolDialogService.ShowAsync`
  (`HistoryPageViewModel.OpenToolAsync`), which reloads the history afterwards when a reference moved.
- **Icons.** Enigma.Icons.Avalonia (`ei:Icon`, `{ei:IconGeometry}`, Phosphor set); `MenuItem.Icon`.

## PHASE01 — A line's menu opens anywhere on it

**Branch:** `feature/feature-5860-phase01-whole-line-menus`
**Status:** DONE — see `docs/done/FEATURE-5860-PHASE01.md`

### Steps

1. Move each row's `ContextMenu` from the inner `Grid` to the row's transparent `Border` in the
   branches, tags and remotes pages, and give the Changes page's stash rows a hit-testable background
   (any other list row found with the same defect gets the same fix).
2. Headless tests: a right-click in the empty space of a branch, tag and remote line (between the
   columns, in the padding) opens that line's menu.

### Acceptance criteria

- Right-clicking anywhere on a branch, tag or remote line opens its menu.
- Build clean with zero warnings; the whole suite green.

## PHASE02 — Copy names and hashes from the history

**Branch:** `feature/feature-5860-phase02-copy-items`
**Status:** DONE — see `docs/done/FEATURE-5860-PHASE02.md`

### Steps

1. Branch badge menu: **Copy branch name** (the name the badge shows, e.g. `origin/main`).
2. Tag badge: a menu with **Copy tag name**.
3. Line menu (a commit, not the uncommitted line): **Copy short commit hash** and
   **Copy full commit hash**.
4. `HistoryPageViewModel` commands through `ISystemInterop.CopyTextAsync`.
5. Tests: each item copies the expected text (recording interop); the uncommitted line offers no copy.

### Acceptance criteria

- The four copy items exist where described and copy exactly the name or hash.
- Build clean with zero warnings; the whole suite green.

## PHASE03 — Select the line in the history

**Branch:** `feature/feature-5860-phase03-select-in-history`
**Status:** TODO

### Steps

1. `IToolDialogService`: `RevealInHistory(string sha)` closes the open dialog and records the commit;
   `ShowAsync` returns it (`Task<string?>`).
2. Branch rows and tag rows: **Select in the history** (branches and tags pages, through the dialog
   service).
3. `HistoryPageViewModel`: after the dialog closes, select the commit's line and bring it into view,
   loading further pages while it is not loaded yet; when the history does not contain it (a hidden
   branch), say so in an info bar.
4. `HistoryPageView`: scroll the selected line into view when asked.
5. Tests: from each dialog the dialog closes and the line is selected; a commit beyond the first page
   is found; a hidden branch's commit is reported.

### Acceptance criteria

- "Select in the history" on a branch or tag line closes the dialog, selects that commit's line and
  scrolls to it.
- Build clean with zero warnings; the whole suite green.

## PHASE04 — Icons on the important operations

**Branch:** `feature/feature-5860-phase04-menu-icons`
**Status:** TODO

### Steps

1. `HistoryMenuEntry` gains an optional icon; the line menu's container theme shows it.
2. Every context menu gets icons on its actions — history line, branch and tag badges, the drop menus
   (history and branches dialog), branches, tags and remotes dialogs, the Changes page's stash rows,
   the changed-files panel — one Phosphor glyph per kind of action (checkout, merge, fast-forward,
   pull, push, delete, create branch/tag, reset, stash, copy, open, show changes, fetch, rename/edit,
   upstream, visibility).
3. Tests: a representative item of each menu carries an icon.

### Acceptance criteria

- Every actionable item of every context menu shows an icon matching its action, readable in both
  themes.
- Build clean with zero warnings; the whole suite green.

## Out of scope

- Keyboard accelerators in menus; new actions beyond those listed.

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| Whole-line fix | Menu on the row's transparent `Border` | The border already paints the whole line; the smallest change, the one FEATURE-3030 used | A background on every inner grid |
| Tag badge menu | A new menu with "Copy tag name" only | What was asked; checkout/delete live in the tags dialog | Duplicating the tags dialog's actions |
| Hash item labels | "Copy short commit hash" / "Copy full commit hash" | Says what lands on the clipboard | "Copy short commit" (ambiguous) |
| Select in the history | Close the dialog, then select and scroll, loading more pages if needed | The history is under the dialog; the reader wants to see the line | Keeping the dialog open over the selection |
| Which items get icons | Every actionable item, one glyph per kind of action | A consistent column; "important operations" covers nearly every item | Only destructive items |
| Icons last | PHASE04 after the stash, drag and copy devs | New items get their icons in the same pass | Icons first, then retrofits |
