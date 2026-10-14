# FEATURE-68B9 — A clear button in the home filter

**Item:** FEATURE-68B9 — A clear button in the home filter
**Branch:** `feature/feature-68b9-home-filter-clear`
**Run:** feature/2026-10-14-home-filter-clear-release

## Summary

The start window's filter box has a clear button at its right edge: Escape, for the mouse.

- **Where:** inside the box (`TextBox.InnerRightContent` of `RepositoryFilter`), at its right edge,
  4 pixels in from the border. The text stops short of it, as Fluent lays the slot out.
- **When:** while the box holds anything, spaces included
  (`StringConverters.IsNotNullOrEmpty` on `SearchText`). That is `ClearSearchCommand`'s own rule.
  It is still there in the *No repository matches this filter* state, where it is most needed.
- **What:** `ClearSearchCommand`, the command Escape is bound to. The box empties, every repository
  comes back in the stored order, the grip returns, and the button goes.
- **Focus:** the button is not focusable. A click leaves the caret in the box, ready for the next
  filter, and the tab order is unchanged; the keyboard keeps Escape.
- **Look:** the app's other small clear buttons (the history's chips): a `toolbar` button with the
  12-pixel `X` (`ei:Icon … Classes="pill"`), its frame removed (`BorderThickness="0"`) so it does not
  draw a box in the box. The hover plate stays.
- **Accessibility:** tooltip *Clear the filter (Escape)*, automation name *Clear the filter*.

No ViewModel change: `ClearSearchCommand` already existed (FEATURE-8970 PHASE01).

## Files touched

**Modified**

- `src/Enigma.GitClient.Desktop/Views/Pages/RepositoriesPageView.axaml`: the button.
- `tests/Enigma.GitClient.Desktop.UnitTests/RepositoryFilterTests.cs`: four tests and their helpers.
- `docs/roadmap.md`, `docs/plan/FEATURE-68B9.md`: statuses.

**Created**

- `docs/done/FEATURE-68B9.md`

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| Removing the toolbar frame | `BorderThickness="0"` on the button, not a new style | The template binds the presenter's thickness to the button's, so one local value takes the frame away in every state (hover and pressed only change its brush); one control does not earn a theme style |
| Size | `Padding="5"` around the 12-pixel icon: 22 pixels, inside the box's 32 | Fits the box without growing it; the history's chip buttons use the same icon size |
| Vertical placement | `VerticalAlignment="Center"` | The slot is as tall as the box; centred, the `×` lines up with the text |
| Tooltip wording | *Clear the filter (Escape)* | The house names the key in brackets (*Next match (Enter)*); *filter* is the box's own word (*Filter repositories*, *Clear the filter to see every repository.*) |
| Where the tests find the button | Among the filter box's visual descendants, by automation name | Proves both that it lives inside the box and that it is named for assistive technology |
| Placement assertion | Inside the box, in its right half, and closer to the right edge than its own width | Says "at the right edge" without hard-coding Fluent's template padding, which this machine cannot check by running the test |

## Deviations & follow-ups

- **None from the plan.**
- **Follow-up:** the other filter boxes (Tags, Branches, the changed files) have no clear button; the
  history's search has one beside the box. Giving them the same in-box button would make the four
  alike.
- Line endings: the touched files are LF; no CRLF churn.

## Documentation sweep

Nothing became wrong. No user-facing document describes the home list's filter yet: FEATURE-8970 left
that to the release, and FEATURE-1CE1 (6.1.0, this run) writes it with the clear button in it. No
`CLAUDE.md` or `AGENTS.md` exists.

## Build/test evidence

- `dotnet build Enigma.GitClient.slnx -c Release`: **0 warnings, 0 errors**; a `--no-incremental`
  rebuild printed **no AVLN warning**.
- `Enigma.GitClient.Core.UnitTests` (Release): **1183 passed, 1 skipped, 0 failed**.
- `Enigma.GitClient.Core.IntegrationTests` (Release): **361 passed, 2 skipped, 0 failed** (3 min 30 s).
- Desktop tests added to `RepositoryFilterTests`, **compiled, not run**: the project's standing
  instruction is never to run the Desktop suite on this Windows machine. Run them on Linux before
  tagging.
  - `TheClearButton_ShowsOnlyWhileTheBoxHoldsSomethingToClear`: hidden when empty, shown for text and
    for spaces, hidden again.
  - `TheClearButton_SitsInsideTheBoxAtItsRightEdge`.
  - `ClickingTheClearButton_EmptiesTheBoxShowsEveryRepositoryAndKeepsTheCaretInTheBox`.
  - `ClickingTheClearButton_WhenNothingMatches_BringsTheListBack`.
- **Re-run on Linux** when the run was committed: `dotnet build` **0 warnings, 0 errors**;
  `Enigma.GitClient.Desktop.UnitTests` **1529 passed, 0 failed** (the four tests above included);
  `Enigma.GitClient.Core.UnitTests` **1183 passed, 1 skipped**; `Enigma.GitClient.Core.IntegrationTests`
  **363 passed**.
- Fix budget: 0 cycles.
