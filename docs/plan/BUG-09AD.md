# BUG-09AD — Scrollbar hides the diff's last line

**Status:** TODO
**Type:** BUG
**Branch:** `bugfix/bug-09ad-diff-bottom-padding`
**Run:** vibe/2026-10-06-diff-search-refs-release

## Objective

The diff's last line stays readable when the horizontal scrollbar grows under the pointer. Today the
Fluent scrollbar overlays the content and, expanded on hover, covers the last line even when the diff
is scrolled to its end.

## Context & constraints

- The diff editors are `DiffTextEditor` (AvaloniaEdit `TextEditor`): the unified one and the two
  side-by-side panes, all with `HorizontalScrollBarVisibility="Auto"`, a hidden vertical bar (the
  minimap replaces it) and `AllowScrollBelowDocument = false`.
- The Fluent `ScrollViewer` auto-hides its bars over the content; the expanded bar's thickness is the
  theme's scrollbar size.
- AvaloniaEdit's `TextEditor` template hosts `PART_ScrollViewer` (exposed as `DiffTextEditor.ScrollHost`);
  whether it template-binds `Padding` is to be checked at build time.
- The editors are styled through the `diff` class (`Themes/`), which is where a house-wide value
  belongs.

## Steps

1. Give every diff editor a bottom padding at least as tall as the expanded horizontal scrollbar —
   through the editor's `Padding` (style on the `diff` class) if the template binds it to the scroll
   viewer, else on `ScrollHost` in `OnApplyTemplate`. The padding sits outside the scrolled text, so
   the last line, scrolled to the end, ends above the bar.
2. Tests (headless): in the unified view and both side-by-side panes, scrolled to the end, the last
   line's bottom sits at least the expanded scrollbar's height above the editor's bottom edge.

## Acceptance criteria

- Scrolled to the end, the last line of a diff — unified, and both side-by-side panes — is not covered
  by the horizontal scrollbar, expanded or not.
- Commit diffs and working-tree diffs alike (one control).
- Release build clean with zero warnings; the whole suite green.

## Out of scope

- Turning off the scrollbar's auto-hide (it would take the bar's room permanently).
- `AllowScrollBelowDocument` (scrolls the last line to the top: far more than asked).

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| How | A bottom padding under the scrolled content | What was asked, and the bar keeps its overlay look | `AllowAutoHide=False` (permanent bar strip); scrolling below the document |
| How much | At least the expanded bar's thickness, read from the theme at build time | Exactly what the bar can cover | A guessed constant |
| Which views | Every diff editor (unified, side-by-side, commit and working tree) | One control, one defect | Working-tree diffs only |
