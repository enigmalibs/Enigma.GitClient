# CODE-REVIEW-6E56-PHASE20 — [Medium] XAML-03 Theme colour resources

## Summary

Five sites wrote colours as literals outside the theme dictionaries, so a palette change in
`Themes/Graph.axaml` missed them all, and in the light variant they kept dark-variant values:

- the checked-out badge's text, `#FFFFFF`;
- the avatar's initials, `#12151A`;
- the avatar tints, a C# copy of the dark variant of the graph lanes;
- the warning icon of the "git is required" dialog and of the "Something went wrong" dialog, each
  building `#E8A33D` in a service.

What changed:

- `Themes/Graph.axaml`, per variant, has two new colours, each with one shared brush whose colour
  follows the variant, as the file does for the lanes:
  - `RefBadgeHeadForegroundColor`: white in both variants, on the head badge's strong blue;
  - `AuthorAvatarInitialsColor`: `#12151A` on the dark variant's light lanes, white on the light
    variant's deeper lanes.
- `Themes/Controls.axaml`: the head badge's foreground and the avatar's initials use
  `DynamicResource` for these brushes.
- `AuthorAvatar` no longer holds a palette. A seed picks one of the same eight lanes as before (0–5,
  7, 8), and the tint *is* that lane's `GraphLaneNBrush`, looked up when the avatar joins the tree.
  Its colour follows a theme switch with nothing to look up again. `IndexFor` is unchanged, so an
  author keeps their colour.
- `ThemeBrushes.Warning` (Desktop, new, internal) is Enigma.Avalonia.Desktop's `EnigmaWarningBrush`,
  and both dialogs take it as their icon brush.

## Files/modules touched

- Created: `src/Enigma.GitClient.Desktop/Services/ThemeBrushes.cs`
- Created: `tests/Enigma.GitClient.Desktop.UnitTests/ThemeColourResourceTests.cs`
- Modified: `src/Enigma.GitClient.Desktop/Themes/Graph.axaml`
- Modified: `src/Enigma.GitClient.Desktop/Themes/Controls.axaml`
- Modified: `src/Enigma.GitClient.Desktop/Controls/AuthorAvatar.cs`
- Modified: `src/Enigma.GitClient.Desktop/Services/AppWindows.cs`
- Modified: `src/Enigma.GitClient.Desktop/Services/UnhandledErrorReporter.cs`
- Modified: `docs/roadmap.md`, `docs/plan/CODE-REVIEW-6E56.md`
- Created: `docs/done/CODE-REVIEW-6E56-PHASE20.md`

## Deviations & follow-ups

- **The warning icon's amber is the library's, `#C48832`**, slightly deeper than the old `#E8A33D`,
  and the same in both variants. That is the plan's choice ("the library's `Enigma*` warning brush
  when one exists"). It also matches the warning text the views already draw with that brush.
- **The light variant's avatars change.** Their tints are now the light lanes, the point of the
  finding, and their initials are white. White out-contrasts dark ink on those lanes: 3.42:1 against
  3.16:1 at worst, 4.59 against 4.10 on average, by WCAG relative luminance. The dark variant looks
  exactly as before.
- **The tints reference the lane brushes rather than copying their colours** into avatar keys. The
  plan allowed either. A reference cannot drift from the graph.
- `using Avalonia.Media` was removed from the two services, and `using System` from `AuthorAvatar`;
  none uses them any more.

## Build/test evidence

- Baseline: see `docs/done/CODE-REVIEW-6E56-PHASE01.md`.
- **This dev:**
  - `dotnet build Enigma.GitClient.slnx --no-incremental`: 0 warnings (`AVLN*` included), 0 errors.
  - Core unit: 1534 run, 1531 passed, 0 failed, 3 skipped.
  - Core integration: 449 run, 447 passed, 0 failed, 2 skipped (7 min 13 s).
  - Desktop: compiled, **not run** (this machine's standing rule).
  - No colour literal or `Color.Parse` remains in the four files of the five sites (grep).
- **Throwaway harness outside the repository** (Avalonia 12.1.4 headless platform, the application's
  own `App` styles). Every check passed:
  - both new keys resolve in both variants;
  - dark: the avatar of "Ada Lovelace" is tinted with lane 7 (`#8A9BF5`), its initials are drawn
    `#12151A`, and the head badge's text is white;
  - after a switch to light, the same avatar is `#4A5ECC` (lane 7, light), its initials white, and
    the badge's text white;
  - `EnigmaWarningBrush` is `#C48832` in both variants.
- **New tests (Desktop, compiled, not run)** in `ThemeColourResourceTests` (6 cases):
  - every new colour resolves in both variants;
  - the initials are dark ink on dark and white on light;
  - an avatar is tinted with its lane's brush and follows a theme switch;
  - the head badge takes its text colour from the theme;
  - the warning icon is the library's brush.
- **Fix budget:** 0 cycles.

## Documentation sweep

- Nothing stale: no user doc lists these colours.

## Decisions taken at build time

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| The avatar tints | The lane brushes themselves, lanes 0–5, 7, 8 | The same eight hues as before; one palette for the graph and the avatars; a theme switch recolours both | Eight avatar colour keys per variant (a copy that can drift) |
| Light-variant initials | White | Higher worst-case and average contrast on the light lanes than dark ink | Keeping `#12151A` everywhere |
| Where the warning brush is looked up | A small `ThemeBrushes` helper over `Application.Current` | Two services need it and have no control to search from. The brush instance follows the theme | `Color.Parse` of the library's value (still a literal); a new app-level key duplicating the library's |
| When the avatar resolves its tint | On joining the logical tree, and on each seed change | The theme's resources are reachable from there. Until then it is grey, never a stale colour | Resolving in the constructor (no resource scope yet) |
