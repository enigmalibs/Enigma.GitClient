# FEATURE-E228 — Redraw the app icon's connections

**Item:** FEATURE-E228 — Redraw the app icon's connections
**Branch:** `feature/feature-e228-icon-elbow`
**Run:** feature/2026-10-14-app-icon-connections

## Summary

The application icon keeps its dark tile, its blue line of commits on the left, its green commit on
the right, and every dot where it was. What changed is how the line meets the dots:

- **Before:** the green commit hung off the middle blue commit on a horizontal stub, then dropped and
  ran back to the bottom commit on a diagonal — two joins, three angles.
- **After:** one rounded elbow. The line leaves the bottom (base) commit horizontally, turns a quarter
  circle of radius 32 (in the 256-unit drawing), and rises into the green commit from below. That is
  how the History graph draws a branch (`CommitGraphCell`: horizontal out of a node, vertical along a
  lane, a rounded turn, never a diagonal), so the icon now speaks the app's own graph language.
- **Kept exactly:** `#1E1F22` tile (240 units, corner 48), `#3574F0` trunk and nodes (r 26, the top one
  a ring from 48 px up), `#3FB950` node (r 22) at the same place, 18-unit strokes. The blue nodes now
  sit on an even 64-unit pitch (64 / 128 / 192; they were 66 / 126 / 190).
- **Small frames hinted, as before:** at 16 and 32 px the ring is filled and strokes (24) and nodes
  (28 / 24) are heavier, because a 13-unit ring and an 18-unit line are a pixel or less there.

For the first time the icon has a **vector source**: two SVG masters under `packaging/icon/`, each
with the regeneration recipe in its header comment. Re-rendering them reproduces all six committed
frames byte for byte.

## Files touched

**Created**

- `packaging/icon/enigma-git-client.svg` — the master for 48, 64, 128 and 256.
- `packaging/icon/enigma-git-client-small.svg` — the hinted master for 16 and 32.
- `tests/Enigma.GitClient.Desktop.UnitTests/AppIconTests.cs` — two tests.
- `docs/done/FEATURE-E228.md`

**Modified**

- `src/Enigma.GitClient.Desktop/Assets/app.ico` — six new frames, same layout (16 → 256, 32-bit, PNG).
- `src/Enigma.GitClient.Desktop/Assets/app-256.png` — the new 256 frame, byte for byte.
- `packaging/linux/icons/enigma-git-client-{16,32,48,64,128,256}.png` — the new frames, byte for byte.
- `docs/roadmap.md`, `docs/plan/FEATURE-E228.md` — statuses.

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| Elbow shape | A true quarter circle (`A 32 32`), not the graph cell's quadratic | A constant-radius turn is what the eye expects at icon size; the quadratic is the graph's cheap equivalent at row size. Same grammar, drawn for its size |
| Elbow radius | 32 | Turns fully before the green node's edge (y 150) and still leaves a visible horizontal run out of the blue node (x 118 → 136) |
| Draw order | Trunk, green line, blue nodes, green node, ring last | Every line starts and ends under a node, so no cap or join is ever visible; the ring is filled with the tile colour so the trunk does not show through it |
| Small-frame weights | Stroke 24, blue r 28, green r 24, ring filled | Compared side by side with the current 16/32 frames and two other weights: 18 left the green line faint at 16 px, 28 thickened the trunk until the nodes barely stood out from it |
| ICO packing | The rendered PNGs unchanged, smallest first, 32-bit, PNG entries | The existing file's exact layout; nothing re-encodes the frames, so the recipe in the SVG header reproduces them byte for byte |
| PNG optimisation | None | `rsvg-convert` writes larger PNGs than the old frames (the six total about 16 KB against 6.5 KB, plus a `bKGD` chunk). Pixels are what matter; optimising would put a third tool into the recipe |
| Where the tests read from | `avares://Enigma.GitClient.Desktop/Assets/…` | What the application actually loads; no repository-root locator is needed. The Linux copies, outside the assembly, were checked by hand (below) |
| What the second test also proves | The 256 frame decodes as an Avalonia `Bitmap` of 256 × 256 | The splash and the About dialog load it exactly that way |

## Deviations & follow-ups

- **None from the plan.**
- **Follow-up (optional):** the PNGs could be losslessly recompressed (e.g. `oxipng`) to get back to
  the old byte sizes; the recipe would then gain that step.
- **Follow-up (optional):** hicolor also takes a `scalable/apps/enigma-git-client.svg`; the master
  could be installed there by `install.sh`. Out of scope here.
- Line endings: the new text files are LF; no CRLF churn.

## Documentation sweep

Nothing became wrong. `README.md` and `docs/RELEASE.md` only say the installer puts the icon in
hicolor in six sizes, which is still true; no document describes the drawing. Release notes are
written by the next release. No `CLAUDE.md` or `AGENTS.md` exists.

## Build/test evidence

- `dotnet build Enigma.GitClient.slnx -c Release --no-incremental`: **0 warnings, 0 errors**, no AVLN
  warning.
- `Enigma.GitClient.Desktop.UnitTests` (Release): **1531 passed, 0 failed** — the 1529 before plus:
  - `TheIcon_HoldsSixSquare32BitPngFramesFrom16To256`;
  - `TheSplashImage_IsTheIconsLargestFrameByteForByte`.
- `Enigma.GitClient.Core.UnitTests` (Release): **1183 passed, 1 skipped, 0 failed**.
- `Enigma.GitClient.Core.IntegrationTests` (Release): **363 passed, 0 failed**.
- **Copies, checked by script:** each `packaging/linux/icons/enigma-git-client-<N>.png` equals the
  icon's `<N>` frame byte for byte, and so does `app-256.png`.
- **Reproducibility:** the six frames were re-rendered from the masters with `rsvg-convert` 2.63.2
  (cairo 1.18.6) and are byte-identical to the committed ones.
- **Looked at:** every frame enlarged next to the old one, the 256 frame on a dark and a light
  background, and at the 96 px the splash and About dialog use.
- Fix budget: 0 cycles.
