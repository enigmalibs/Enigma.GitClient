# FEATURE-8EBF — Pastel, more visible diff colours

**Status:** DONE — see `docs/done/FEATURE-8EBF.md`
**Type:** FEATURE
**Branch:** `feature/feature-8ebf-pastel-diff-colours`
**Run:** feature/2026-09-27-infobars-dialogs-diff-release

## Objective

The colours that say *added*, *removed*, *changed* and the rest in the diff views are hard to see, in
both themes. Make them clearly visible, in pastel colours, without making the code they sit behind any
harder to read.

## Context & constraints

- All of them are theme-scoped `Color` keys in `src/Enigma.GitClient.App/Themes/Graph.axaml`, each
  behind one `SolidColorBrush`, so a theme switch recolours with no reload. Values change here; no key
  is added, removed or renamed, so `Styles.axaml`, `Controls.axaml` and the views are untouched.
- What they paint:
  - the **status chips** beside a changed file (`Border.status` + `added` / `deleted` / `renamed` /
    `conflicted`, *modified* by default) — `DiffStatus{Added,Modified,Deleted,Renamed,Conflicted}Color`,
    with one letter colour, `DiffStatusForegroundColor`;
  - the **line tints** of added and removed rows — `Diff{Added,Removed}LineColor`;
  - the **word tints** behind the changed stretches — `Diff{Added,Removed}WordColor`;
  - the **+ / − markers** — `Diff{Added,Removed}MarkerColor`, which the minimap also draws with.
- Measured today (WCAG relative-luminance contrast): the line tints are **1.04:1** against the window
  background in both themes — invisible; the dark theme's are even darker than the background. The
  chip letters are **3.3–3.4:1** on the light theme's green and amber chips and 4.2:1 on the dark
  amber one — under the 4.5:1 text minimum.
- `DiffViewerTests.DiffColours_AreDistinctAndKeepTheTextReadable` guards: the four tints distinct,
  the text (`EnigmaForegroundBrush`: Dark `#BCBEC4`, Light `#1E1F22`) at ≥ 4.5:1 on every tint, and
  each word tint ≥ 1.6:1 against its line tint. Those guards stay.
- The dark theme's text is dim (`#BCBEC4`), which caps how light a tint behind it can be: the line
  tints there are pastel hues washed into the dark background, while the chips and markers — which
  carry no code — are true pastels.

## The palette

Chosen in CIE LCh (hue from a pastel family: mint 150°, rose 25°, sky 255°, lavender 300°, apricot
80°), lightness set by the contrast budget, chroma as high as the gamut allows at that lightness.

| Key | Dark (was) | Dark | Light (was) | Light |
|---|---|---|---|---|
| `DiffStatusAddedColor` | `#2E7D46` | `#9CDAAB` | `#2E9E56` | `#83C594` |
| `DiffStatusModifiedColor` | `#2A5A9E` | `#9AD2FE` | `#3574F0` | `#6DBEF5` |
| `DiffStatusDeletedColor` | `#9E3B32` | `#FEBBB8` | `#D14328` | `#F79E9C` |
| `DiffStatusRenamedColor` | `#6B4A9E` | `#D0C4FE` | `#8E46AC` | `#BAADEF` |
| `DiffStatusConflictedColor` | `#9E6B1E` | `#ECC68D` | `#C2801B` | `#D6B075` |
| `DiffStatusForegroundColor` | `#F2F4F8` | `#1E1F22` | `#FFFFFF` | `#1E1F22` |
| `DiffAddedLineColor` | `#0C1F13` | `#11301B` | `#E6F8EA` | `#CDECD3` |
| `DiffRemovedLineColor` | `#2C1514` | `#441F1F` | `#FDEBE8` | `#FFDCDA` |
| `DiffAddedWordColor` | `#205230` | `#285335` | `#86D29D` | `#85C394` |
| `DiffRemovedWordColor` | `#83352B` | `#703B3A` | `#F59D8E` | `#F29F9C` |
| `DiffAddedMarkerColor` | `#57C77E` | `#95DCA7` | `#1E7A3C` | `#007037` |
| `DiffRemovedMarkerColor` | `#F2725B` | `#FEBBB8` | `#C03A22` | `#A63D42` |

Computed contrast with these values — text on line / word tint: Dark 7.7 / 4.75, Light 13.0 / 8.0;
word vs line: 1.63 / 1.62; **line tint vs background: 1.15 / 1.20** (was 1.04); chip letter: ≥ 10.2
(Dark), ≥ 8.1 (Light); chip vs background: ≥ 10.2 / ≥ 1.9; marker vs its line tint: 8.9 / 4.9.

## Decisions

| Decision | Rationale |
|---|---|
| Scope: status chips, line tints, word tints, markers (and so the minimap) | Everything that says added / removed / changed / renamed / conflicted in the diff views |
| One dark letter on every chip, both themes | Pastel fills need a dark letter; it lifts the light theme's chips above 4.5:1 |
| Markers stay deep in the light theme | They are glyphs, not fills: a pastel `+` on a pastel row would be the one thing on the row you could not read |
| Keep every existing guard; add visibility guards | "More visible" becomes testable, and readability cannot regress |

## Steps

1. `Themes/Graph.axaml` — the values in *The palette*, both variants; update the diff-tint comment
   (tints are pastel and visible against the background, the word tint still the loud one; the chips
   carry a dark letter).
2. `App.UnitTests/DiffViewerTests.cs` — alongside the existing guard, for both themes:
   - each line tint ≥ 1.12:1 against `EnigmaBackgroundColor` (the current values fail it);
   - each marker ≥ 4.5:1 against its line tint;
   - the five chip colours distinct; the chip letter ≥ 4.5:1 on every chip; every chip ≥ 1.5:1
     against `EnigmaBackgroundColor`.
3. Run the existing diff and changed-files tests (the chips' classes and the word-tint brush are
   asserted there) — the keys are unchanged, so they must pass as they are.

## Acceptance criteria

- The values in *The palette* are in place, in both themes, and follow a theme switch.
- The existing colour guard and the new ones pass in both themes.
- Build clean with zero warnings; the whole suite green.

## Out of scope

- The conflict page's ours / theirs / base panes, the hunk band, the selection bar, the graph lanes
  and the ref badges.
- A colour preference or a colour-blind palette switch.

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| Which colours are "the diff colours" | Chips, line and word tints, markers | The draft names removed / added / changed / etc. in the diff views: rows, words and the file chips | Also the conflict panes (a different view, with its own ours/theirs palette) |
| How to reconcile "pastel" with readability | Pastel hues, lightness bound by WCAG AA against the diff text; dark-theme row tints as pastel washes | The text is the content; a tint that fails 4.5:1 trades readability for colour | Light pastel rows in the dark theme (fail 4.5:1 against `#BCBEC4`); brighter diff text (a theme change nobody asked for) |
| How to prove "more visible" | A line-vs-background contrast guard (≥ 1.12:1) and chip guards | Turns the complaint into a check the old values fail | Inspection only |
| Chip letters | One dark foreground | Reads on every pastel fill in both themes | Per-chip letter colours (five more keys for no gain) |
| Light-theme markers | Deep, not pastel | They are text glyphs on a tinted row and need 4.5:1 | Pastel markers (≈ 1.3:1 on their row) |
