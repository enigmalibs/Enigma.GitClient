# FEATURE-7232-PHASE01 — Toolbar buttons that look enabled

**Item:** FEATURE-7232 — Toolbars, one refresh, flat file lists
**Branch:** `feature/feature-7232-phase01-toolbar-buttons`
**Run:** feature/2026-09-24-toolbar-refresh-theme-menus

## Summary

A toolbar button that can be pressed is now drawn as its glyph (or its text) in the full foreground
inside a one-pixel `EnigmaBorderBrush` frame, on every strip: the repository toolbar, the history
toolbar, the branches/tags/remotes dialogs, the changes page, the diff viewer, the changed-files panel,
the merge banner. One that cannot be pressed looks the way an enabled one used to: its glyph in the
secondary grey, no frame, and no longer faded to 35 % opacity.

The segmented and on/off toggles (`ToggleButton.segment`: list/tree, unified/side by side, whitespace,
wrap, ignore whitespace) follow the same rule: the full foreground in a frame, the selection plate
behind the one that is on, grey with no frame when disabled.

A row's own action (a `Button.toolbar` on a line of a list or a tree) keeps its frameless look — a
frame on every line would be noise — and, when it cannot be pressed, its icon now greys like any
disabled toolbar button instead of staying in the full foreground.

## Files / modules touched

**Modified — App**

- `Themes/Styles.axaml` — `Button.toolbar`: full foreground, frame, padding `6,5` (the frame's pixel
  out of the old `7,6`), frame and foreground restated on the presenter for `:pointerover` and
  `:pressed`; `:disabled` → secondary foreground, transparent frame, no opacity; `ListBoxItem
  Button.toolbar, TreeViewItem Button.toolbar` → no frame, padding `7,6`; `Button.toolbar:disabled
  ei|Icon.row` → secondary; `ToggleButton.segment` → the same frame and foreground, padding `5,4`,
  `:pressed`, `:checked` and `:disabled` states

**Tests**

- `App.UnitTests/ToolbarButtonLookTests.cs` (new) — enabled: full foreground, frame, opacity 1;
  disabled: grey, transparent frame, opacity 1, and back on re-enable; the button keeps its size;
  a text toolbar button follows the same foreground; a row action in a list has no frame and greys
  when disabled; segments unchecked, checked (selection plate) and disabled; on the realised
  repository window, the disabled refresh is grey and the enabled theme switch is framed
- `App.UnitTests/BranchesPageTests.cs` — `ABranchRowsActions_AreVisibleOnEveryRow` now expects a
  disabled action (the current branch cannot be checked out or deleted) to be grey, and every enabled
  one in the full foreground, selected or not

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| How row actions are told apart | By where they sit: a `Button.toolbar` inside a `ListBoxItem` or a `TreeViewItem` | Every row action in the application sits in one of the two; no class to remember on each |
| The start window's recent-repository cards | Their pin / forget / new-window buttons (an `ItemsControl`, not a list) take the frame | They are sparse card actions beside a filled "Open" button, where the frame helps; they were never dense list rows |
| The frame on the presenter | `BorderBrush` restated per state on `PART_ContentPresenter`; `BorderThickness` template-bound from the button | FluentTheme's state rules set the presenter's brushes directly; the thickness has to stay overridable by the row reset |
| Keeping the size | The frame's pixel comes out of the padding (`7,6` → `6,5`; segments `6,5` → `5,4`) | Strips keep their height; asserted by a test |

## Deviations & follow-ups

- None from the plan.
- Recommendation only: line endings were not examined; nothing in this diff showed CRLF churn.

## Documentation sweep

- Nothing to change: no README or release-notes sentence describes the toolbar buttons' look.

## Build/test evidence

- `dotnet build Enigma.GitClient.slnx`: 0 warnings, 0 errors.
- `dotnet test --solution Enigma.GitClient.slnx`: 1962 passed, 0 failed (7 new). First run: 1 failure
  (`ABranchRowsActions_AreVisibleOnEveryRow`, which asserted that the current branch's disabled actions
  stay in the full foreground — now intentionally grey); green after one fix cycle.
