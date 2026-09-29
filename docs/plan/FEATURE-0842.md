# FEATURE-0842 — History: checked-out line, tag focus

**Status:** DONE
**Type:** FEATURE
**Branch:** one per phase, see below
**Run:** feature/2026-09-29-rail-clone-history

## Objective

- The history already marks the checked-out commit with the current branch's blue badge and the
  graph node's white ring. The whole line should also carry a subtle tint, subtle but visible.
- When the *Create a tag* dialog opens, its name box has the keyboard focus.

## Context & constraints

- **The row.** `Views/Pages/HistoryPageView.axaml`: every history line is a
  `Grid Classes="commitrow"` with `Classes.match="{Binding IsSearchMatch}"`.
  `CommitRowViewModel.IsHead` ("HEAD resolves to this commit") is what `CommitGraphCell.IsHead`
  draws the ring from.
- **The washes.** `Themes/Styles.axaml`:
  - `Grid.commitrow` is `Transparent`, the row's hit area;
  - `Grid.commitrow.match` is `SearchMatchBrush`, with hover and selected variants.

  The wash is painted on the row, so it covers the container's hover and selected plates, and a
  washed row has to handle all three states itself. Styles later in the file win. Colours are
  theme-scoped in `Themes/Graph.axaml` (Dark and Light dictionaries), with one brush per key.
- **The colours.** The HEAD badge is `RefBadgeHeadColor` `#3574F0`. The library's colours:

  | Colour     | Dark      | Light     |
  |------------|-----------|-----------|
  | background | `#1E1F22` | `#F7F8FA` |
  | hover      | `#2E3035` | `#E8EAED` |
  | selection  | `#214283` | `#C4D8F8` |

- **The tests.** `HistoryPageTests.Search_WashesTheRowsItFound` asserts that every row is transparent
  before a search. With a HEAD wash, that stays true only for the rows that aren't HEAD.
- **The tag dialog.** `Services/TagOperations.CreateAsync` builds `CreateTagDialogView` and shows it
  on the host `ContentDialog`. This covers the history toolbar, the line's menu and the Tags dialog.
  The library's `ContentDialog` doesn't move the focus when it opens. The view names none of its
  controls; the name box's automation name is "Tag name". `ChangesPageView.TakeTheFocus` is the
  house pattern: posted from `Loaded`, guarded by `IsEffectivelyVisible`.

## PHASE01 — The checked-out line is washed

**Branch:** `feature/feature-0842-phase01-checked-out-line`
**Status:** DONE — see `docs/done/FEATURE-0842-PHASE01.md`

### Steps

1. `Themes/Graph.axaml`: add `HeadRowColor` and `HeadRowHoverColor` in both variants, each with its
   brush. They are the HEAD badge's blue over the background, far short of the selection:
   - Dark: ≈ 12% and 20%;
   - Light: ≈ 10% and 16%.
2. `Themes/Styles.axaml`:
   - `Grid.commitrow.head` → `HeadRowBrush`;
   - `ListBoxItem:pointerover Grid.commitrow.head` → `HeadRowHoverBrush`;
   - `ListBoxItem:selected Grid.commitrow.head` → `EnigmaSelectionBrush`, so a selected HEAD line
     looks like any selected line;
   - all declared before the search wash, so a HEAD line the search found shows the search wash.
3. `HistoryPageView.axaml`: add `Classes.head="{Binding IsHead}"` on the row `Grid`.
4. Tests (headless, real repository):
   - the HEAD line, and only it, carries `head` and is painted `HeadRowBrush`;
   - the HEAD line the search found is painted `SearchMatchBrush`;
   - `Search_WashesTheRowsItFound` checks "transparent before a search" on the lines that aren't HEAD.

### Acceptance criteria

- On the history, the checked-out line is tinted a subtle blue across its whole width, in both
  themes. Every other line is unchanged.
- The tint follows hover (a shade further) and selection (the ordinary selection). A search match on
  that line shows the search wash.
- Build clean with zero warnings; the whole suite green.

## PHASE02 — The tag name has the focus

**Branch:** `feature/feature-0842-phase02-tag-name-focus`
**Status:** DONE — see `docs/done/FEATURE-0842-PHASE02.md`

### Steps

1. `CreateTagDialogView.axaml`: `x:Name="NameBox"` on the name `TextBox`.
2. `CreateTagDialogView.axaml.cs`: on `Loaded`, post `NameBox.Focus()` when the view is effectively
   visible (the `TakeTheFocus` pattern).
3. Test (headless): a real host `ContentDialog` in a window shows the view, and after the dispatcher
   has run, the name box has the focus.

### Acceptance criteria

- Opening *Create a tag* from anywhere puts the caret in the name box: the name can be typed at once
  and Enter confirms it.
- Build clean with zero warnings; the whole suite green.

## Out of scope

- Moving the focus in the other dialogs (branch, stash, remote…).
- Any other marker of the checked-out line (bold text, a gutter bar).

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| The tint's hue | The HEAD badge's blue, lightly | The line then reads with the badge it carries | The search's yellow; a neutral grey (reads as hover) |
| How strong | ≈ 12% (dark) / 10% (light) over the background, with a hover shade | "Subtle but visible": visible against the plain rows, far short of the selection | The hover colour (confused with hover); a border (reads as a second selection) |
| Selected HEAD line | The ordinary selection colour | Selection has to look the same on every line | A HEAD-specific selection colour |
| HEAD line found by search | Search wash wins | The search is what the reader is doing now; the badge and ring still mark HEAD | HEAD wash wins |
| Which line | The `IsHead` row (the ringed one), detached HEAD included | That is the line checked out | Only when on a branch |
| Focus scope | The *Create a tag* dialog only | What was asked | Every dialog's first field |
| Phases | The wash · the focus | Two unrelated changes, one reviewable commit each | One dev |
