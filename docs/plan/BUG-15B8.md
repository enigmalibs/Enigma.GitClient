# BUG-15B8 — The repository browser is cut off

**Status:** TODO
**Type:** BUG
**Branch:** `bugfix/bug-15b8-repository-browser-width`
**Run:** feature/2026-10-01-polish-release-5-1

## Objective

The dialog that browses a connected account's repositories, opened from a profile's integrations
(*Browse*), is cut on its left and its right: the filter, the visibility toggles and each row's
*Open* and *Clone* buttons cannot all be seen. It must be shown whole, in both windows.

## Context & constraints

- **The cause, read from the markup:** `HostRepositoriesDialogView` declares `Width="760"`, but the
  `HostDialog` it is shown in is `DialogMaxWidth="720"` in both `MainWindow.axaml` and
  `StartWindow.axaml`, and the card has its own padding inside that. A child wider than the card is
  centred in it and clipped on both sides. That is exactly "cut in the left and in the right". It has
  been like this since FEATURE-1406 PHASE03 (2.0.0).
- **The other views on `HostDialog`** declare widths that fit: About 380, Commit details 560, the forms
  `MinWidth` 460–560. Only the browser overflows.
- `HostDialog`'s bounds are shared by every question the app asks, in both windows. BUG-5349 kept them
  unchanged deliberately.
- `DialogQuestionWrapTests` shows the real `ContentDialog` in a headless window, which can measure
  the card.

## Steps

1. Confirm the cause with a headless test: the real view on a real 720-wide `ContentDialog`, with a
   few repository rows. Measure the view and the area the card gives its content.
2. Fix the view so it fits the card: a width no wider than the card's content area at
   `DialogMaxWidth="720"`, taken from that measurement and written down in a comment. The row's
   description `MaxWidth` (480) is checked against the narrower row so the *Open*/*Clone* buttons keep
   their room.
3. Tests (`HostRepositoriesDialogTests` or a new layout test class):
   - in a 720-wide host, the view lies entirely inside the card's visible content area: no part of
     it is left of the area or right of it;
   - each row's *Clone* button is inside it too, whole;
   - the filter box and the three visibility toggles are inside it.
4. Whole suite.

## Acceptance criteria

- In both windows (both hosts are 720), the browser dialog shows its title, filter, toggles,
  summary and every row's buttons in full, with nothing clipped at either side.
- No other dialog's size changes, and `HostDialog`'s bounds stay as they are.
- Build clean with zero warnings; the whole suite green.

## Out of scope

- Widening `HostDialog`.
- Reworking the browser's row layout beyond what fitting needs.

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| Widen the host, or fit the view | Fit the view | The host's bounds are every question's, in both windows, and a small window would clip a wider card anyway | `DialogMaxWidth="800"` on both hosts |
| A fixed width or an auto width | A fixed width that fits | An auto-sized card would change width as the host's list arrives under the reader | Removing `Width` and letting rows drive it |
| How the width is chosen | Measured in the headless test, not guessed | The card's padding is the library's. A guessed constant would drift | A round number with no check |
