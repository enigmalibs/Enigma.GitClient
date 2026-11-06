# FEATURE-4F5C-PHASE03 — Store and draw a repository icon

**Item:** FEATURE-4F5C — Repository tiles: menu and icon
**Branch:** `feature/feature-4f5c-phase03-icon-store`
**Run:** feature/2026-11-06-repo-tiles-history-search

## Summary

A repository can now have an icon of its own, which its tile draws in place of the folder glyph.
Choosing it from the tile's menu is PHASE04; this phase stores and draws it.

- **`IRepositoryIconStore` / `RepositoryIconStore`** (singleton):
  - keeps a copy of the chosen file in `<config>/repository-icons/<key>.<ext>`, where `<key>` is 16 hex
    digits of the SHA-256 of the repository's full path (upper-cased on Windows), so every profile that
    lists the repository shares it;
  - `SetAsync` refuses, with a `RepositoryIconException` whose message a user can read:
    - another kind of file;
    - a missing file;
    - a file over 10 MB (1 MB for SVG);
    - a file the tile could not draw.
    Nothing is written then.
  - `SetAsync` writes under a temporary name, moves the file into place, then removes the old icon of
    another kind. `ResetAsync` removes it.
- **`RepositoryIconImages`** (static, cached by path, write time and length):
  - pictures go through Skia, decoded 48 px wide: PNG, JPEG, BMP, GIF, ICO, WebP;
  - an SVG naming its own colours is drawn in them (a `DrawingImage` over its view box);
  - an SVG naming none (`currentColor` or no paint) becomes a one-icon `SvgIconSet` drawn by `ei:Icon`
    in the theme's foreground, like the application's own glyphs.
- **`ListedRepository.IconFile`** (`[JsonIgnore]`, so `repository-lists.json` is unchanged) is filled
  by `RepositoriesPageViewModel` when it builds the list. An icon that can no longer be read is left
  out and logged.
- **The tile:** a 24 × 24 square holding the folder glyph, the picture (`TileImage`) or the glyph
  (`TileCustomGlyph`), whichever applies.

## Files/modules touched

- Created:
  - `src/Enigma.GitClient.Desktop/Services/RepositoryIconStore.cs`
  - `src/Enigma.GitClient.Desktop/Services/RepositoryIconImages.cs`
  - `tests/Enigma.GitClient.Desktop.UnitTests/RepositoryIconTests.cs`
  - `docs/done/FEATURE-4F5C-PHASE03.md`
- Modified:
  - `src/Enigma.GitClient.Desktop/Services/RepositoryListStore.cs` (`ListedRepository.IconFile`)
  - `src/Enigma.GitClient.Desktop/ViewModels/Pages/RepositoriesPageViewModel.cs`
  - `src/Enigma.GitClient.Desktop/Views/Pages/RepositoriesPageView.axaml`
  - `src/Enigma.GitClient.Desktop/DependencyInjection/ServiceCollectionExtensions.cs`
  - `docs/roadmap.md`, `docs/plan/FEATURE-4F5C.md`: statuses.

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| An SVG with no colour of its own | Drawn by `ei:Icon` from a one-icon `SvgIconSet`, in the theme's foreground | Most icon files leave the colour to the text around them; drawn black, as SVG's default, it vanished on the dark theme. The control follows a theme switch with no reload |
| An uncoloured or unreadable paint (a gradient) in an SVG that names colours | Black | SVG's own default fill; mixing in the theme's colour would be a guess |
| The loader | Static, with a process-wide cache | A value converter in markup has no container; images are immutable and shareable, as the icon library's glyph cache is |
| Logging a stored icon that no longer reads | In the page ViewModel when it builds the list, which also drops `IconFile` so the folder shows | The draw path (a converter) has no logger, as `Icon.Render` has none |
| Undecodable data | Skia's `NullReferenceException` from `Bitmap.DecodeToWidth` caught at that one call and turned into `InvalidDataException` | That is how the decoder says "not a picture"; nothing wider is caught |
| The glyph's colour | The full foreground (`tile` class, no `dim`) | The user chose this icon to stand out; the folder glyph stays `dim` |

## Deviations & follow-ups

- The plan had the SVG's uncoloured layers take "the theme's foreground or a neutral grey" inside one
  `DrawingImage`. They now follow the theme only when the whole SVG is uncoloured (see above).
- GIF and WebP are taken by extension and decoded by Skia, but not covered by a test: the test
  platform has no encoder to make them.
- A forgotten repository's icon stays in the folder: it comes back if the repository is listed again
  (as planned, *Out of scope*).

## Build/test evidence

- **Baseline:** see `docs/done/FEATURE-4F5C-PHASE01.md` (clean).
- **This dev:** build 0 warnings, 0 errors; tests 3663 run, 3660 passed, 0 failed, 3 skipped.
- **New tests** (`RepositoryIconTests`, 11):
  - the key's shape and rule;
  - a stored copy outlives the original;
  - another kind replaces the old, and a reset removes it (other repositories untouched);
  - PNG, JPG, JPEG, BMP, ICO and SVG are taken;
  - six refusals with their reasons, the previous icon kept;
  - a picture is decoded 48 wide, ICO and BMP too;
  - a two-colour SVG keeps its colours and view box;
  - a `currentColor` SVG becomes a glyph set;
  - missing and damaged files give nothing, and a changed file is read again;
  - the three tiles (picture, glyph, none) draw the right one;
  - a stored icon damaged since leaves the folder glyph.
- **Fix budget:** 0 cycles. Two problems were caught by the new tests during implementation, before the
  first full run: Skia's `NullReferenceException`, and an xUnit1051 analyzer error.

## Documentation sweep

- Nothing in the README describes the tile's icon; PHASE04 adds the way to choose one. No edit.
