# FEATURE-0ED1-PHASE02 — The active profile and Make active

**Item:** FEATURE-0ED1 — Profiles as cards, a clear active one, PHASE02 — The active profile and Make active
**Branch:** `feature/feature-0ed1-phase02-active-profile`
**Run:** feature/2026-10-23-search-escape-profile-cards

## Summary

On the Profiles page, the profile git commits as is unmistakable, and switching to another one is a named
action.

- **The active card.**
  - `Border.profilecard.active`, bound to `IsCurrent`, outlines the card in the accent
    (`ActionAccentBrush`).
  - Only the brush changes. The frame stays 1 px, so nothing on the page moves when another profile is
    made active.
- **It says so in words.** In the place of the action, the header shows a filled `CheckCircle` in the
  accent and **Active**, 13 px SemiBold, in the full foreground.
  - Its tip reads *Git commits as this profile: your global configuration has its name and email*.
  - Its automation name is *Active profile*.
  - It replaces the *Current* pill.
- **Make active.** Every other profile that has a name and an email shows a **Make active** button.
  - It is a `Button.toolbar` with the `UserSwitch` icon and the words, at the page's normal size, like
    *Add a profile* and *Connect an account*.
  - It replaces the 11 px *Use*.
  - Its tip says what it writes. Its automation name is *Make this profile active*.
  - The marker and the button share one place and never show together.
  - A profile without a name and email has neither, because switching to it would unset git's
    identity.
- **The label trims.** With the pill gone, the label is no longer in a horizontal panel. A long label
  now ends in an ellipsis instead of running under the actions.
- **The words follow.**
  - The empty state now ends *… switch between them with Make active.*
  - The repository section's button is *Copy from active profile*: its tip (*… of the active
    profile*) and its automation name (*Copy from the active profile*).
  - The start window's picker comment names *Make active*.
  - The README's Profiles bullet and its *what is written* paragraph follow.

## Files / modules touched

**Created**

- `docs/done/FEATURE-0ED1-PHASE02.md`

**Modified**

- `src/Enigma.GitClient.Desktop/Themes/Styles.axaml`: `Border.profilecard.active`.
- `src/Enigma.GitClient.Desktop/Views/Pages/ProfilesPageView.axaml`:
  - the card's `active` class;
  - the marker and *Make active* in place of the pill and *Use*;
  - the label out of its horizontal panel;
  - the empty state, and the copy button's text, tip and name.
- `src/Enigma.GitClient.Desktop/Views/Pages/RepositoriesPageView.axaml`: the picker's comment.
- `README.md`: the Profiles bullet (*copied from the active profile*) and the paragraph on what the
  client writes (*make a profile active on the Profiles page*).
- `tests/Enigma.GitClient.Desktop.UnitTests/ProfilesPageTests.cs`:
  - `TheViewMarksTheCurrentProfileAndOffersUseOnTheOthers` became
    `TheViewMarksTheActiveProfileAndOffersMakeActiveOnTheOthers`, over Work (active), Home, and a
    Default profile without a name and email. It checks:
    - only the active card has the `active` class and the accent outline, and every card keeps its
      1 px frame;
    - only it shows the marker, with *Active*, the check icon and the tip;
    - *Make active* shows only on Home, is wired to `UseProfileCommand` with Home's row, and is at
      least 28 px high;
    - no visible TextBlock reads *Current* or *Use*;
    - after switching, the outline, the marker and the button have all moved.
  - That test also writes `snapshots/profiles-active-dark.png` and `profiles-active-light.png`, with the
    theme pinned and then restored, as `ShellSnapshotTests` does.
  - `TheViewShowsTheRepositorySectionOnlyInARepositorysWindow` finds the copy button by its new name.
- `docs/roadmap.md`, `docs/plan/FEATURE-0ED1.md`: statuses. PHASE02 and the item are `DONE`.

## Deviations & follow-ups

- **The note after a switch stays *Using <label>*.** The plan said *<label> is now active*. The start
  window's picker makes the same switch and reports the same *Using <label>*
  (`RepositoriesPageViewModel`). Changing one would make the two notes about one act disagree, and
  changing both reaches past this item.
  - A note is a sentence, not a control. *Using Work — New commits are made as …* still reads right.
- **The word *Active* is in the full foreground, not the accent.** The plan said both icon and word in
  the accent. The accent on the dark card (#3574F0 on #2B2D30) is too faint for 13 px text.
  - The filled check carries the accent, and the word stays legible in both themes.
- **The snapshots.** Both were looked at.
  - Dark: Work outlined in blue with *✓ Active*; Home with *Make active*; Default with neither.
  - Light: the same, with the outline and the check reading clearly on the light card.
- **Focus after a switch.** *Make active* disappears from the card it was pressed on, as that card
  becomes the active one, so the keyboard focus is lost. A follow-up could move the focus to the new
  marker.
- Documentation sweep:
  - The README's Profiles bullet and the *what is written* paragraph named the old action and the old
    word. Both are edited (see above).
  - The README's *the current profile's* integration (in *Connecting a host*) is about the concept,
    not a control. It is still true and was left as it is.
  - `RELEASENOTES.md` is written by the release item.

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| The note after a switch | Unchanged, *Using <label>* | The start window reports the same switch in the same words |
| The colour of *Active* | Full foreground, SemiBold; the check in the accent | Legible at 13 px in both themes; the accent is still the cue |
| *Make active*'s look | `Button.toolbar` with the `UserSwitch` icon (the *Profiles* section's own) | The page's other labelled actions are toolbar buttons with an icon; a real target, not an 11 px chip |
| Where the marker sits | The action's column, with 8 px before the row's edit action | One place for "what this profile is" and "what you can do"; never both |
| Snapshots | Dark and light, written by the view test | The accent has to read on both; the existing page tests keep their snapshots the same way |

## Build/test evidence

- `dotnet build Enigma.GitClient.slnx`: 0 warnings, 0 errors.
- `dotnet test --solution Enigma.GitClient.slnx`: 3468 total, 3465 passed, 3 skipped, 0 failed. No
  fix cycle was needed.
  - The count is the same as PHASE01's, because one view test was rewritten rather than added.
