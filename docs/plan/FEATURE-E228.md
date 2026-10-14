# FEATURE-E228 — Redraw the app icon's connections

**Status:** DONE — see `docs/done/FEATURE-E228.md`
**Type:** FEATURE
**Branch:** `feature/feature-e228-icon-elbow`
**Run:** feature/2026-10-14-app-icon-connections

## Objective

The application icon keeps everything it is liked for — the dark tile, the blue line of commits on
the left, the green commit on the right — and changes **how the line meets the dots**. Today the green
commit hangs off the middle blue commit on a horizontal stub, then drops and runs back to the bottom
commit on a diagonal. It becomes **one rounded elbow**: out of the base commit horizontally, a quarter
turn, and up into the green commit from below — the same way the History graph draws a branch.

## Context & constraints

- **Where the icon lives** — three copies of one set of frames, kept byte-identical by hand so far:
  - `src/Enigma.GitClient.Desktop/Assets/app.ico`: six PNG-compressed, 32-bit frames
    (16, 32, 48, 64, 128, 256), the `<ApplicationIcon>` and both windows' `Icon`;
  - `src/Enigma.GitClient.Desktop/Assets/app-256.png`: the 256 frame, byte for byte, shown at 96 px by
    the splash and the About dialog (FEATURE-75F4);
  - `packaging/linux/icons/enigma-git-client-<N>.png`: the six frames, byte for byte, installed into
    hicolor by `install.sh` (FEATURE-B4C0).
- **No vector source exists.** The frames were generated once (FEATURE-52FB) and only the rasters were
  committed. Measured from the 256 frame: tile 240 × 240 at (8, 8), corner radius 48, `#1E1F22`; blue
  `#3574F0` trunk at x ≈ 92, 18 units wide, nodes r 26 at y 66 / 126 / 190, the top one a ring
  (13 wide, hollow); green `#3FB950` node r 22 at (168, 128), its path 18 wide. The 16 and 32 frames
  are hinted: the ring is filled and the strokes are heavier.
- **The house rule for connections** (`Controls/Graph/CommitGraphCell.cs`): *"Connections are drawn
  as rounded elbows rather than diagonals … a curve that leaves and arrives vertically keeps every line
  traceable."* A line leaves or enters a node horizontally and travels along its lane vertically.
- `Assets/**` is an `AvaloniaResource`: anything placed there is embedded in the assembly.
- Tools on this machine: `rsvg-convert`, Python with Pillow. Neither becomes a project dependency.

## Design

In a 256-unit square, the same for every frame unless stated:

| Element | Geometry |
|---|---|
| Tile | `rect` 8, 8, 240 × 240, `rx` 48, `#1E1F22` |
| Blue trunk | x 92, from y 64 to y 192, 18 wide, `#3574F0` |
| Blue nodes | r 26 at y 128 and y 192; at y 64 a ring (r 19.5, 13 wide, filled with the tile colour) |
| Green node | r 22 at (168, 128), `#3FB950` |
| Green connection | from the bottom node's centre (92, 192) right to x 136, a quarter circle of radius 32 up to (168, 160), then up into the green node — 18 wide |

The small frames (16, 32) use a hinted variant: the ring filled, strokes 24 wide, blue nodes r 28,
green node r 24 — the same hinting the current frames have.

## Steps

1. `packaging/icon/enigma-git-client.svg` — the master for 48, 64, 128 and 256 — and
   `packaging/icon/enigma-git-client-small.svg` — the hinted master for 16 and 32. Each opens with a
   comment saying which frames it produces and how they are regenerated.
2. Render each frame at its exact size from its master (`rsvg-convert -w N -h N`), and write
   `Assets/app.ico` from the six PNGs, unchanged, as PNG-compressed 32-bit entries in size order — the
   current file's layout.
3. Replace `Assets/app-256.png` and `packaging/linux/icons/enigma-git-client-<N>.png` with byte copies
   of the new frames.
4. `tests/Enigma.GitClient.Desktop.UnitTests/AppIconTests.cs`, through `avares://`:
   - `app.ico` holds exactly six square, 32-bit, PNG-encoded frames: 16, 32, 48, 64, 128, 256;
   - `app-256.png` is byte for byte the icon's 256 frame.
5. Look at every frame, enlarged and next to the current ones, before calling it done.

## Acceptance criteria

- The green commit connects to the line of commits by one rounded elbow from the bottom blue commit;
  no diagonal, no horizontal stub from the middle commit.
- Background, both colours, the tile's size and corners, the three blue dots (the top one a ring from
  48 px up) and the green dot are unchanged in colour and place, save the blue dots' even 64-unit
  pitch.
- `app.ico` has the same six sizes and format; `app-256.png` and the six Linux PNGs are byte copies of
  its frames.
- The SVG masters are committed and reproduce the frames.
- Build clean (zero warnings, no AVLN warning); every suite green, `AppIconTests` included.

## Out of scope

- A scalable (`hicolor/scalable`) install, or any change to `install.sh` / `uninstall.sh`.
- A generator script or any new build tooling.
- The splash, the About dialog and the window chrome — they show the new icon unchanged.
- Release notes: written by the next release.

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| How the line meets the dots | One rounded elbow from the bottom (base) commit, up into the green commit from below | The app's own graph grammar (horizontal out of a node, vertical along a lane, a rounded turn); keeps every dot where it is, so only the connection changes — which is what was asked | A closed bracket from the bottom commit to the top ring (reads as a letter "D"); the green commit moved to the top right, forked from the bottom (a tall "U") or from the middle (a "Y") — both change the silhouette the user likes; keeping two connections (needs the horizontal stub back) |
| The dots | Kept: count, colours, sizes, the top ring; blue pitch evened to 64 / 128 / 192 | Only the connection was asked to change; the old pitch (60 then 64) was uneven by accident, not design | Moving or resizing the dots |
| Colours and tile | Kept exactly: `#1E1F22`, `#3574F0`, `#3FB950`, 240-unit tile, 48 corner | Explicitly liked | — |
| Small sizes | A hinted master for 16 and 32: ring filled, strokes 24, nodes 28 / 24 | At 16–32 px a 13-unit ring and an 18-unit line are a pixel or less; the current frames are hinted the same way | Downscaling the 256 drawing |
| Vector source | Two SVG masters under `packaging/icon/` | The next change starts from a source instead of a raster; outside `Assets/**`, so nothing is embedded in the assembly | No source (today's state); under `Assets/` (embedded for nothing) |
| Regeneration | A recipe in each SVG's header comment and in the done doc; no script | No new tooling the project does not have; the recipe is two commands | A Python/.NET generator committed to the repo |
| Tests | `AppIconTests`: six PNG frames at the right sizes, and `app-256.png` = the 256 frame | Pins the invariant three devs have kept by hand, at the moment it is most likely to break; uses the existing headless project | No test; a test reading `packaging/` (needs a repository-root locator the suites do not have — checked by hand instead) |
| Release notes | Untouched | Written at release time, as every release so far | An entry now |
| Breakdown | One item, one dev | One reviewable commit: two SVGs, eight rasters, one test class | — |
