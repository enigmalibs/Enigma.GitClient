# FEATURE-2474 — Row action icons stay readable

**Status:** DONE — see `docs/done/FEATURE-2474.md`
**Type:** FEATURE
**Branch:** `feature/feature-2474-row-action-icons`
**Run:** feature/2026-09-21-icons-tracking-dragging

## Objective

The icon-only actions on a list row — check out, delete, fetch, edit, pin — are legible on every row,
including the one the reader has selected.

## Context & constraints

- Every row action in the application is the same pair: a `Button` with the `toolbar` class holding an
  `ei:Icon` with the `row` class. It occurs on the branches page, the tags page, the remotes page and
  the repositories page. A header's button holds an `ei:Icon.toolbar` instead, which is what tells the
  two apart without a new class.
- `Button.toolbar` sets `Foreground` to `EnigmaForegroundSecondaryBrush`. The icon inside states no
  foreground of its own, so it **inherits** that secondary grey — and an inherited value is beaten by
  a style setter on the icon itself, which is what makes this a one-rule fix.
- `Styles.axaml` already raises quiet **text and leading glyphs** on a selected row: the selectors are
  `ListBoxItem:selected TextBlock.dim | .faint` and the same for `ei|Icon.dim | .faint`. A row action's
  icon carries neither class, so no rule has ever applied to it: on the selection plate it is grey on
  grey.
- The leading glyph of a row (`ei:Icon.row.dim`) stays quiet on purpose — it repeats what the row is,
  it is not a thing to press. Raising the actions and leaving the glyph dim is the hierarchy this
  page wants, not an inconsistency.
- **Baseline:** clean build, 1803 tests green.

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| Which icons the fix targets | `Button.toolbar ei|Icon.row` | That descendant pair is a row action everywhere in the application, and nowhere else; a header's icon carries `Icon.toolbar` and is untouched | A new `rowaction` class on every button (four views to edit, one more thing to remember); a rule per page (four copies of one decision) |
| How visible they become | The full `EnigmaForegroundBrush`, in every state | The request is "always well visible", and it is the one value the selection plate cannot swallow. It is also the only state-free answer: an action that is legible only while hovered is an action nobody finds | Secondary at rest and full when selected (still grey on the other rows); a lighter grey (a smaller version of the same problem) |
| Where the rule lives | `Themes/Styles.axaml`, beside the `dim`/`faint` selection rules | That file already owns "what a quiet thing does on a selected row"; this is the missing case | Each page's `UserControl.Styles` |
| Whether the leading glyph is raised too | No | It is decoration that repeats the row's kind; the existing `:selected` rule already brings it to full on the selected row, which is where legibility was at stake | Raising every row icon (the row loses its hierarchy) |

## Steps

1. `Themes/Styles.axaml`: add a rule `Button.toolbar ei|Icon.row` setting `Foreground` to
   `EnigmaForegroundBrush`, with a comment recording why an inherited value made the icon invisible on
   a selected row and why the selector is the pair rather than a new class.
2. Tests — `tests/.../BranchesPageTests.cs`, `TagsPageTests.cs`, `RemotesAndSyncTests.cs`: on each of
   the three pages, the icon inside a row's action button is painted with the full foreground brush,
   both on an unselected row and on the selected one; and the quiet leading glyph is still quiet on an
   unselected row, so the rule did not flatten the whole row.

## Acceptance criteria

- On the branches, tags and remotes pages, a row action's icon uses `EnigmaForegroundBrush` whether or
  not its row is selected.
- The page headers' icon-only buttons are unchanged.
- A row's leading glyph still reads as secondary on an unselected row.
- Build clean with zero warnings; the whole suite green.

## Out of scope

- Changing which actions a row offers, or their order.
- The hover and pressed plates, the icon sizes, the `Button.rowaction` opacity rule used elsewhere.
- Any change to the Enigma theme's brushes.
