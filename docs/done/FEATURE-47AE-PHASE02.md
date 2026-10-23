# FEATURE-47AE-PHASE02 — Narrower search, clearer group gaps

**Item:** FEATURE-47AE — A lighter History command bar, PHASE02 — Narrower search, clearer group gaps
**Branch:** `feature/feature-47ae-phase02-search-and-separators`
**Run:** feature/2026-10-23-history-bar-polish

## Summary

The History command bar's search box is narrower, and its four command groups are set apart.

- **The search box** is 200 px at most, down from about 250 px on a 1920 px window and up to about
  340 px on wider ones. On a narrow window it gives way first, as before, down to 130 px.
  - It is narrower than before on every window wider than about 1820 px.
  - On narrower windows it was squeezed below 200 px, and it now gets some of the room theme and About
    gave back: at 1536 px it is 130 px, where it was about 100.
  - `CommandBarEnd` is now `MinWidth` 310 and `MaxWidth` 380. The search block's other parts (the counter
    and three buttons) take 180 px.
  - Its placeholder is *Search commits*, in the voice of the app's other search boxes (*Filter
    branches*, *Filter tags*, *Filter files*). It reads in full at both ends of the range.
  - What it searches, "by message, SHA or author", moved to the box's tip. Its accessible name already
    said so.
- **The groups.** The three separators between the groups take one style, `Border.commandseparator`
  (`Themes/Styles.axaml`): 1 px wide, with 16 px either side (from 6) and 8 px above and below, in
  the subtle border brush.
  - Two groups are now 34 px apart around their line, against 2 px between two commands of a group.
  - It is the same on a compact bar.
- **The compact threshold is unchanged.** Named commands need a window of about 1345 px, as before
  this item (about 1335 px). The 60 px the wider separators take is about what theme and About gave
  back. At 1280 px the bar is compact, and the search block stays on it.

## Files / modules touched

**Created**

- `docs/done/FEATURE-47AE-PHASE02.md`

**Modified**

- `src/Enigma.GitClient.Desktop/Themes/Styles.axaml`: `Border.commandseparator`.
- `src/Enigma.GitClient.Desktop/Views/Pages/HistoryPageView.axaml`:
  - the three separators take the class;
  - `CommandBarEnd`'s minimum and maximum widths;
  - the search box's placeholder and tip;
  - the comments on the commands and the end.
- `tests/Enigma.GitClient.Desktop.UnitTests/HistoryCommandBarTests.cs`:
  - New: `TheSearchBox_IsNarrow_AndItsPlaceholderReadsInFull`. On a 1920 px window the box is 200 px,
    its placeholder fits, and the tip says what it searches.
  - New: `TheGroups_AreSetApart_ByTheRoomAroundTheirSeparators`. Each separator carries the class and
    is 1 px wide, with 16 px plus the row's spacing either side. Two commands of a group are only the
    spacing apart.
  - The narrow-window test now checks that the placeholder fits, rather than "the box ≥ 90 px", and
    that the separators keep their 16 px.
  - The helper `AssertThePlaceholderFits` was added.
- `docs/roadmap.md`, `docs/plan/FEATURE-47AE.md`: statuses. PHASE02 and the item are `DONE`. The
  plan's PHASE02 gains an "amended at build time" note (see below).

## Deviations & follow-ups

- **The box's width (the plan said "about as wide as its placeholder needs, about 260 px, from about 345").**
  - Measured on the headless window, the box was about 250 px at 1920 px before this item. It reached
    345 px only on windows wider than about 1940 px.
  - The old placeholder (*Search messages, SHAs and authors*) needs a 257 px box.
  - A box as wide as that placeholder would therefore have been *no narrower* on a 1920 px window. The
    request was a narrower box.
  - So the box is 200 px at most, and the placeholder is the short *Search commits*. The detail it
    gave is in the tip.
- **The floor.** The plan said "about 100 px". *Search commits* needs 105 px of room plus the box's
  18 px of padding and border, so the floor is 130 px, with room for a wider system font.
- **Test bounds.** The plan said "≤ 280 px". The box is now asserted at exactly 200 px on a wide bar,
  and its placeholder is asserted to fit, rather than given a width.
- Documentation sweep:
  - The README says the history has "a search, by message, SHA or author", which is still true.
  - `RELEASENOTES.md` is written by the release item.
  - Nothing was edited.
- The README and release notes never quoted the old placeholder, and no test did.

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| How narrow | 200 px at most | Narrower than before on every window wider than about 1820 px, where it was too wide; room for a short SHA or a few words |
| The placeholder that no longer fits | *Search commits*, the detail in the box's tip | The app's voice for search boxes; a cut-off placeholder reads as a bug |
| The floor | 130 px (`MinWidth` 310) | The short placeholder fits with room to spare; the compact threshold stays where it was |
| The separators' room | 16 px either side, one style, same on a compact bar | Two groups read apart (34 px against 2); one place to tune |
| Where the numbers came from | A throwaway headless probe at 1920, 1536, 1340, 1330 and 1280 px, deleted before the commit | "Measured, not guessed", as the plan asked |

## Build/test evidence

- `dotnet build Enigma.GitClient.slnx`: 0 warnings, 0 errors.
- `dotnet test --solution Enigma.GitClient.slnx`: 3455 total, 3452 passed, 3 skipped, 0 failed, green
  on the first run (no fix cycle).
  - That is +2 over PHASE01's 3453: the box test and the separators test.
- Looked at in rendered frames:
  - **1920 px:** the four groups are clearly apart at the bar's centre, and the 200 px box reads
    *Search commits*.
  - **1280 px:** compact, icons only, the groups still apart, and the search block on the bar.
