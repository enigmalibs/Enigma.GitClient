# FEATURE-D977-PHASE02 — Centring command bar panel

**Item:** FEATURE-D977 — Repository strip and command bar, PHASE02 — Centring command bar panel
**Branch:** `feature/feature-d977-phase02-command-bar-panel`
**Run:** feature/2026-10-23-strip-and-command-bar

## Summary

`CommandBarPanel` (`Controls/`), the layout PHASE03 puts the History bar on. It has three slots, by
position: start, centre, end. `Spacing` defaults to 10.

- **The start** hugs the left edge at its desired width.
- **The centre** sits at the bar's true centre when the start fits on its left and the end's `MinWidth`
  on its right. Otherwise it moves as little as it must: never over the start, and leaving the end its
  minimum while there is room for that.
- **The end** hugs the right edge. It is given the width right of the centre, up to its `MaxWidth`, or
  its desired width when it has none, so the search block will be as wide as before on a wide bar and
  is the first thing to give way on a narrow one. Narrower than its minimum, it is anchored at its left
  and runs past the bar's edge rather than over the commands.
- Every slot is as tall as the bar, and its own vertical alignment places it. A hidden slot takes no
  room and no spacing. A fourth child is measured and arranged at nothing, as the remarks say.

The rule is one formula, not a set of cases:

- the centre's left is the true centre, clamped between `start + spacing` and
  `width − min(endMin, room) − spacing − centre`;
- the end is whatever is right of the centre, up to its maximum.

## Files / modules touched

**Created**

- `src/Enigma.GitClient.Desktop/Controls/CommandBarPanel.cs`
- `tests/Enigma.GitClient.Desktop.UnitTests/CommandBarPanelTests.cs`: 7 cases.
  - True centre with room.
  - The end takes the room right of the centre, up to its maximum.
  - A wide start moves the centre right by exactly the spacing.
  - A wide end shrinks to its minimum before the centre moves left.
  - A bar too narrow puts the centre after the start, and the end runs past the edge.
  - The bar is as tall as its tallest slot and centres the others; unbounded, it asks for every slot's
    desired width.
  - A hidden end takes no room.
- `docs/done/FEATURE-D977-PHASE02.md`

**Modified**

- `docs/roadmap.md`, `docs/plan/FEATURE-D977.md`: statuses.

## Deviations & follow-ups

- **No deviation from the plan.** No view uses the panel yet; PHASE03 does.

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| How the slots are named | By position, documented in the remarks | Three children in XAML read as start, centre, end; attached properties would add API for no gain |
| The end's upper bound when it has no `MaxWidth` | Its desired width, measured unbounded | An end with nothing to stretch gets what it asks for, no more |
| An end narrower than its `MinWidth` | Arranged from its left edge at its minimum, so it overflows the bar's right edge | Avalonia centres a child arranged in less than its minimum, which would have drawn it back over the commands |
| Spacing next to an empty slot | None | A hidden start or end must not push the centre off true |

## Build/test evidence

- `dotnet build Enigma.GitClient.slnx`: 0 warnings, 0 errors.
- `dotnet test --solution Enigma.GitClient.slnx`: 3443 total, 3440 passed, 3 skipped, 0 failed (+7 new).
  - The first run failed 7 of the new cases: the test windows were laid out but never shown, and a
    headless window that is not shown lays itself out at its own default size (1024 × 768) whatever it
    was measured at.
  - Cycle 1 shows each test window, and disposes it afterwards: green. The panel itself was unchanged.
