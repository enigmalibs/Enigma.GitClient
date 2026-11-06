# FEATURE-4F5C-PHASE04 — Choose an icon from the tile menu

**Item:** FEATURE-4F5C — Repository tiles: menu and icon
**Branch:** `feature/feature-4f5c-phase04-choose-icon`
**Run:** feature/2026-11-06-repo-tiles-history-search

## Summary

A repository tile's menu ends, after a separator, with:

- **Choose an icon…** (`ImageSquare`). It opens the file picker, titled "Choose an icon for <name>",
  with an "Images" filter (`*.png *.jpg *.jpeg *.bmp *.gif *.ico *.webp *.svg`) and "All", and makes
  the chosen file the tile's icon. A file that cannot be an icon is reported in an error bar, "The icon
  was not changed", with the reason, and the tile keeps the icon it had. A cancelled picker does
  nothing.
- **Use the default icon** (`ArrowCounterClockwise`), shown only while the tile has an icon. It puts the
  folder glyph back.

The icon is the repository's: every profile that lists it shows it, and it survives the repository
being added to a list again.

- `RepositoriesPageViewModel`: `ChooseIconCommand`, `ResetIconCommand`, and the file picker
  (`IFileDialogService`) injected.
- After a change the list is rebuilt from what the page holds: every entry is given a new record, so
  a tile whose icon was replaced by another of the same kind is drawn again.

## Files/modules touched

- Created:
  - `tests/Enigma.GitClient.Desktop.UnitTests/RepositoryIconChoiceTests.cs`
  - `docs/done/FEATURE-4F5C-PHASE04.md`
- Modified:
  - `src/Enigma.GitClient.Desktop/ViewModels/Pages/RepositoriesPageViewModel.cs`
  - `src/Enigma.GitClient.Desktop/Views/Pages/RepositoriesPageView.axaml`
  - `tests/Enigma.GitClient.Desktop.UnitTests/RepositoryTileMenuTests.cs` (the menu's two new items)
  - `tests/Enigma.GitClient.Desktop.UnitTests/Infrastructure/UiServiceDoubles.cs`
    (`RecordingFileDialogService`), `Infrastructure/TestServices.cs` (registered, `Files`)
  - `docs/roadmap.md`, `docs/plan/FEATURE-4F5C.md`: statuses.

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| Refreshing the tiles after a change | `Replace(_all)`, which looks every icon up again; the list itself is not read back from disk | The list did not change; the icon store is the only thing that did |
| A repository that has moved | Its icon can still be chosen | The icon is keyed by the path the list knows; it shows again if the folder comes back |
| A picker double for the tests | `RecordingFileDialogService`, as the folder picker's double | The real service needs a window's storage provider and would open a dialog |

## Deviations & follow-ups

- The README and the release notes do not mention the tile's menu or icons yet. The §7 sweep edits
  only what a dev made wrong, and nothing there is; the next release item describes them.

## Build/test evidence

- **Baseline:** see `docs/done/FEATURE-4F5C-PHASE01.md` (clean).
- **This dev:** build 0 warnings, 0 errors; tests 3668 run, 3665 passed, 0 failed, 3 skipped.
- **New tests** (`RepositoryIconChoiceTests`, 5):
  - a chosen picture becomes the tile's icon, the picker naming the repository and offering the eight
    kinds of file;
  - a cancelled picker changes nothing;
  - a damaged file is reported and the icon stays;
  - "Use the default icon" shows only with an icon, and puts the folder back;
  - the icon follows the repository into another profile's list, and survives a re-add.
- `RepositoryTileMenuTests` extended: the menu's full order, the new glyph, and both commands.
- **Fix budget:** 0 cycles.

## Documentation sweep

- README's start-window bullet is not made wrong by the new items; no edit (see *Deviations*).
