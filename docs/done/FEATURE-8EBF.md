# FEATURE-8EBF — Pastel, more visible diff colours

**Item:** FEATURE-8EBF — Pastel, more visible diff colours
**Branch:** `feature/feature-8ebf-pastel-diff-colours`
**Run:** feature/2026-09-27-infobars-dialogs-diff-release

## Summary

The colours that mark added, removed, modified, renamed and conflicted content in the diff views are
now pastel and clearly visible in both themes. This is values only, in
`src/Enigma.GitClient.App/Themes/Graph.axaml`, for the twelve keys listed in the plan's
*The palette*. No key is added, renamed or removed.

- **Status chips** (A / M / D / R / U beside a changed file) are pastel fills — mint, sky, rose,
  lavender, apricot — with one dark letter (`#1E1F22`) in both themes. The letter is now at least
  8.1:1 on every chip. Before, the light theme's green and amber chips were 3.4:1 and 3.3:1 and the
  dark amber one 4.2:1, all below the 4.5:1 text minimum.
- **Line tints** of added and removed rows are pastel washes that stand out from the window
  background: 1.15:1 (Dark) and 1.20:1 (Light) against it, up from 1.04:1. The old dark tints were
  darker than the background itself.
- **Word tints** stay the loud layer: 1.63:1 and 1.62:1 against their line, and the diff text stays
  at 4.75:1 or better on them.
- **+ / − markers** are the pastel itself in the dark theme and a deep shade of the same hue in the
  light one. They are glyphs and need 4.5:1 on their row. The minimap draws with them, so it follows.

## Files / modules touched

**Modified**

- `src/Enigma.GitClient.App/Themes/Graph.axaml` — the twelve colour values in both variants. There is
  a new comment above the chips, and the tint comment is rewritten: pastel washes, the 4.5:1 budget
  that keeps the dark washes dark, and why the light markers are deep.
- `tests/Enigma.GitClient.App.UnitTests/DiffViewerTests.cs` — two new theories, each for Dark and
  Light:
  - `DiffTints_StandOutFromTheBackground_AndKeepTheirMarkersReadable`: line tint ≥ 1.12:1 against
    `EnigmaBackgroundColor`, and marker ≥ 4.5:1 on its line.
  - `StatusChips_AreDistinctVisibleAndReadable`: five distinct fills, letter ≥ 4.5:1 on each, and
    each chip ≥ 1.5:1 against the background.
- `docs/roadmap.md`, `docs/plan/FEATURE-8EBF.md` — statuses

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| The values | Exactly the plan's table | Computed and checked in LCh and WCAG luminance while planning; the build only confirms them |
| Proving the guards mean something | Ran the four new cases against `Graph.axaml` from the run branch (old palette), then put the new file back (not committed) | All four failed on the old values: tints 1.04:1, light chip letters 3.3–3.4:1, dark amber 4.2:1. All pass on the new ones. The existing readability guard passes on both |
| The chip comment | In the Dark dictionary only, like the file's other palette comments | The Light dictionary repeats the keys without commentary |

## Deviations & follow-ups

- None from the plan.
- Follow-up, not done: the conflict page's ours/theirs/base panes and the hunk band keep their
  colours (out of scope). If the new pastels make them look out of place next to the diff, they are
  the natural next palette pass.
- Line endings: no CRLF churn. The touched files are LF.

## Documentation sweep

`RELEASENOTES.md` 1.0.0 ("Colour-coded additions and deletions…", "Tints tuned for contrast in both
themes…") and the README's feature lines are still true, so nothing was edited. The 1.1.0 notes
belong to the release item.

## Build/test evidence

- `dotnet build Enigma.GitClient.slnx --no-incremental`: 0 warnings, 0 errors, no `AVLN` warnings.
- `dotnet test --solution Enigma.GitClient.slnx`: **2231 passed**, 0 failed (4 new cases), with no
  fix cycle. The existing diff, changed-files and snapshot tests pass unchanged.
