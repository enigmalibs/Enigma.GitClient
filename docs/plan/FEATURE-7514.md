# FEATURE-7514 — Branches, tags and remotes as dialogs

**Status:** TODO
**Type:** FEATURE
**Branch:** one per phase, see below
**Run:** feature/2026-09-22-home-window-merges-refresh

## Objective

Branches, Tags and Remotes stop being pages on the rail. They open as content dialogs from three
buttons on the History toolbar. The Branches dialog gains a manual merge section: a source and a
destination combo box with **Merge**, **Merge fast-forward** and **Clear** buttons.

## Context & constraints

- `IContentDialogService` drives **one** `ContentDialog` host, and every `ShowAsync` replaces its content
  and buttons. The branch, tag and remote operations raise their own confirmations and forms through
  that service (delete, rename, create, set upstream, add remote…), so a Branches dialog shown on the
  same host would be wiped out by the first confirmation it asks for.
- The three pages are `PageViewModelBase` singletons that do their subscription and first build in
  `OnAppearingAsync`; their views are self-contained `UserControl`s, including the branches list's
  pointer-driven drag and its drop menu.
- "Merge A into B" already exists: `IBranchDropOperations.DropAsync` checks B out when it is not
  current, then merges; `BranchDropOperations.CanDrop` is the policy (no remote destination, not into
  itself). The drop menu's "Merge" is `FastForwardMode.WhenPossible`, and its fast-forward item is
  `FastForwardMode.Only`.

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| Where the dialogs are hosted | A second `ContentDialog` in the repository window (the *tool dialog* host), placed **under** the operations dialog host and above the content, driven by an app service `IToolDialogService` | A confirmation raised from inside the Branches dialog then opens above it, on the other host, and the Branches dialog is still there when it closes | Reusing `IContentDialogService` (nested flows would destroy the dialog); separate OS windows (not "content dialogs") |
| What the dialog shows | The existing page view, bound to its existing ViewModel, with `OnAppearingAsync` / `OnDisappearingAsync` called around the showing | No second copy of three pages, and every behaviour — filters, drag, menus — comes along | Rewriting them as dialog-specific views |
| Dialog size | Set once on the host: wide and tall enough for a list (about 960 × 640, bounded by the window) | The library does not reset size properties between showings; a list needs a bounded height to scroll | Auto-size (a list would grow past the window) |
| The rail afterwards | History, Changes and — while a merge is waiting — Conflicts; Integrations and Settings in the footer | The prompt calls the three pages secondary | Keeping them in the rail as well |
| Manual merge: what the combos hold | Source: every branch, local and remote; Destination: local branches only | Mirrors the drop policy: a remote branch is changed by pushing, not by merging into it | Letting a remote be chosen and refusing afterwards |
| Manual merge: the two merges | **Merge** = `WhenPossible`, **Merge fast-forward** = `Only`, both through `IBranchDropOperations` | The same two merges the drop menu offers, with the same meaning | A `--no-ff` "Merge" (differs from every other merge in the app) |
| Manual merge: when the buttons are enabled | Both chosen and `CanDrop` accepts the pair; **Clear** whenever either is chosen | A button that would only report a refusal is a button that should not be pressable | Always enabled |
| Selections across refreshes | Kept by branch name, dropped when the branch is gone | The list rebuilds on every refresh | Resetting on every rebuild |

## PHASE01 — Dialogs from the history toolbar

**Branch:** `feature/feature-7514-phase01-tool-dialogs`
**Status:** TODO

### Steps

1. `IToolDialogService` / `ToolDialogService`: `RegisterHost(ContentDialog)` and
   `ShowAsync(ToolDialog which)` for `Branches`, `Tags`, `Remotes` — builds the page view from the
   container, attaches its ViewModel, calls `OnAppearingAsync`, shows it on its own host with a Close
   button and the page's title, and calls `OnDisappearingAsync` when it closes.
2. The repository window gets the tool dialog host between its content and the operations dialog host;
   the window coordinator registers it with the other hosts.
3. `ShellNavigation` no longer adds Branches, Tags and Remotes to the rail; `ShellPage` loses the three
   values.
4. The History toolbar gets three buttons — Branches, Tags, Remotes — each opening its dialog through
   commands on `HistoryPageViewModel`. When a dialog closes after changing something, the history
   reloads.
5. Tests: each button opens the matching dialog with its page and ViewModel; the rail no longer carries
   the three pages; a confirmation raised while the Branches dialog is open goes to the operations host
   and leaves the tool dialog open; the three views still lay out inside the dialog.

### Acceptance criteria

- The rail no longer shows Branches, Tags or Remotes.
- The History toolbar has three buttons that open them as dialogs, with everything they did as pages.
- A confirmation asked from inside one of these dialogs appears above it, and the dialog is still there
  afterwards.
- Build clean with zero warnings; the whole suite green.

## PHASE02 — A manual merge section

**Branch:** `feature/feature-7514-phase02-manual-merge`
**Status:** TODO

### Steps

1. `BranchesPageViewModel`: `MergeSources` (every branch) and `MergeDestinations` (local branches),
   `SelectedMergeSource` / `SelectedMergeDestination`, and `ManualMergeCommand`,
   `ManualFastForwardCommand`, `ClearManualMergeCommand`, with can-execute from the drop policy.
   Rebuilt with the list; the selections survive a rebuild by name.
2. `BranchesPageView`: a "Merge" section above the list with the two combo boxes and the three buttons.
3. Tests: the combos hold the right branches; the buttons are enabled only for a valid pair; Merge and
   Merge fast-forward call the drop operations with `WhenPossible` and `Only`; Clear empties both; a
   selection survives a rebuild and goes when the branch does.

### Acceptance criteria

- The Branches dialog has a source and a destination combo box and the Merge, Merge fast-forward and
  Clear buttons.
- The merges do what dropping the source on the destination does.
- Build clean with zero warnings; the whole suite green.

## Out of scope

- Merging tags or commits from the manual section.
- A merge preview.
