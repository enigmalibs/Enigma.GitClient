# FEATURE-1085 — Legible ref row details

**Status:** DONE — see `docs/done/FEATURE-1085.md`
**Type:** FEATURE
**Branch:** `feature/feature-1085-ref-row-details`
**Run:** feature/2026-10-21-ui-polish-release-650

## Objective

The words on the right of a branch's or a remote's line become a little lighter and a little bigger,
so they can be read in the dark theme:

- a branch's badges (*checked out*, *worktree*, *hidden*, ahead / behind counts, *local only*);
- a group's or folder's branch count;
- a remote's URL.

## Context & constraints

- These are `TextBlock`s with `Classes="rowdetail faint"`. In `Styles.axaml`:
  - `rowdetail` is `FontSize 11`;
  - `faint` is `EnigmaForegroundTertiaryBrush`: `#4E5157` on `#1E1F22` in dark, about 2:1 contrast.
- `dim` is `EnigmaForegroundSecondaryBrush`: `#6F737A` in both themes, about 3.5:1 in dark and 4.5:1
  in light.
- Both classes are raised to the full foreground on a selected row.
- The badge icons beside the text are `ei:Icon Classes="pill faint"`.
- The same `rowdetail faint` pair is used by the worktrees dialog and the changed-files panel. You
  named branches and remotes only.
- You asked for the same theme keys in both themes: no dark-only override.

## Steps

1. `Styles.axaml`: a `TextBlock.refdetail` class: `FontSize 12`, centred vertically. Its comment says
   why it is a step up from `rowdetail`.
2. `BranchesPageView.axaml`: every `rowdetail faint` becomes `refdetail dim` (group and folder counts,
   every badge's text), and every badge icon `pill faint` becomes `pill dim`.
3. `RemotesPageView.axaml`: the URL's `rowdetail faint` becomes `refdetail dim`.
4. Tests (Desktop suite, compiled): a headless test that a branch badge and a remote URL render with
   `EnigmaForegroundSecondaryBrush` at 12 px.

## Acceptance criteria

- In both themes, the branches' and remotes' right-hand texts use `EnigmaForegroundSecondaryBrush` at
  12 px.
- A selected row still raises them to the full foreground.
- The worktrees dialog and the changed-files panel are unchanged.
- The build is clean with zero warnings (AVLN included). The Core suites are green. The Desktop suite
  compiles.

## Out of scope

- Changing the theme's colours themselves (a library concern).
- The worktrees dialog and the changed-files panel. They are a follow-up if wanted.

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| How much lighter | One role up: tertiary → secondary (`dim`) | An existing theme key in both themes, as you asked; clearly lighter in dark, a little darker but still fine in light | A new colour; a dark-only override |
| How much bigger | 11 → 12 px | "A bit bigger"; still fits the 22 px line | 13 px, the name's size, which would flatten the hierarchy |
| How | A `refdetail` class in `Styles.axaml` | One place to tune; a local value would outrank every style | Changing `rowdetail` globally (it would change panels you did not name) |
| Scope | Branches (badges, counts) and remotes (URL), badge icons included | What you named; an icon fainter than its text would look broken | Every `rowdetail` in the application |
