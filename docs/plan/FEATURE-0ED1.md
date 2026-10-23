# FEATURE-0ED1 — Profiles as cards, a clear active one

**Status:** DONE — see `docs/done/FEATURE-0ED1-PHASE01.md`, `docs/done/FEATURE-0ED1-PHASE02.md`
**Type:** FEATURE
**Branch:** one per phase, see below
**Run:** feature/2026-10-23-search-escape-profile-cards

## Objective

A modern, legible presentation of the profiles in the Profiles page's *Profiles* expander:

1. **Each profile is a card of its own.** It is framed, set apart from the next one, and holds its
   integrations, so it is clear which accounts belong to which profile.
2. **Larger type and a real mark.**
   - The label is a card title.
   - The name and email under it read without effort.
   - The small grey `UserCircle` becomes a monogram of the profile, which tells two profiles apart.
3. **The active profile is obvious, and switching is a named action.**
   - The profile git commits as is outlined in the accent colour and says **Active** with a check
     mark. This replaces the *Current* pill.
   - Every other profile that has a name and an email offers **Make active**, a real labelled button.
     This replaces the small *Use*.

## Context & constraints

- **The page** is `Views/Pages/ProfilesPageView.axaml`. The *Profiles* expander's `ItemsControl.profiles`
  lists `ProfileRowViewModel`s through the `ProfileRow` template:
  - a 16 px `UserCircle` (`Icon.row dim`);
  - the label at the default size, SemiBold, with a *Current* pill (`Border.pill`, 11 px) beside it;
  - the summary (`Name <email>`, or *No name or email*) at 11 px, `faint`;
  - a *Use* button (`Button.choice`, 11 px) while `CanUse`, then *Edit* and *Delete* row actions;
  - the integrations, indented 34 px under it, then the *Local only* line or the integration rows,
    then *Connect an account*.

  Profiles are 8 px apart (`Margin="0,4"`), with nothing between them.
- **The expander** (`SettingsCardExpander`, Enigma.Avalonia.Desktop 1.2.0):
  - a 14 px SemiBold header and a 12 px secondary description;
  - its content on `EnigmaSurfaceLowBrush`, with `16,12,16,16` padding.
- **Current** is `ProfileRowViewModel.IsCurrent`: the first profile whose identity is git's global one.
  `CanUse` is `!IsCurrent && Profile.HasIdentity`. `UseCommand` writes the global identity, selects the
  profile in the start window's picker, and reports *Using <label>*.
- **The monogram** exists: `Controls/AuthorAvatar` (initials and a stable colour from a seed), used by
  the history's author column. Its template is fixed at 18 px (radius 9, 9 px type).
- **Wording that names Current or Use:**
  - the empty-state text (*… switch between them with Use.*);
  - the repository section's *Copy from current profile* button, its tip (*… the profile marked
    Current*) and its automation name;
  - the start window's picker comment;
  - the README's Profiles bullet (*copied from the current profile*).
- **Tests pinned to today's row:**
  - `ProfilesPageTests.TheViewMarksTheCurrentProfileAndOffersUseOnTheOthers` finds the buttons named
    *Use this profile* and the TextBlocks reading *Current*;
  - `UsingAProfileMakesItTheGlobalIdentityInOneClick` reads the note *Using Home*;
  - `ProfileIntegrationsTests.TheViewShowsEachProfilesIntegrationsAndTheEarlierOnes` renders
    `snapshots/profiles-integrations.png`;
  - `TheViewShowsTheRepositorySectionOnlyInARepositorysWindow` finds *Copy from the current profile*.
- **Baseline** on `3aac49c`: build 0 warnings; 3455 tests, 3452 passed, 3 skipped.

## PHASE01 — One card per profile, larger type

**Status:** DONE — see `docs/done/FEATURE-0ED1-PHASE01.md`
**Branch:** `feature/feature-0ed1-phase01-profile-cards`

### Steps

1. `AuthorAvatar` can be drawn at another size.
   - Its template's disc fills the control, and takes the control's `CornerRadius` and `FontSize`.
   - The control theme keeps today's 18 px, radius 9 and 9 px type as setters, so the history's
     avatars do not change.
   - A role class, `AuthorAvatar.profile`, draws it at 36 px, radius 18, 14 px type.
2. `ProfileRowViewModel.Initials`: the label's monogram. It uses the same rule as a commit author's:
   the first letters of the first and last words, upper case, or `?` for an empty label. One helper
   serves both, rather than two copies of the rule.
3. `ProfilesPageView.axaml`, the `ProfileRow` template:
   - The profile is a card, `Border.profilecard` in `Themes/Styles.axaml`:
     - `EnigmaSurfaceBrush`, 1 px `EnigmaBorderSubtleBrush`, radius 6, padding `16,14`;
     - the cards 8 px apart, through the items panel's spacing.
   - Its header:
     - the monogram (36 px, seeded by the label);
     - the label at 15 px SemiBold;
     - the summary at 13 px in the secondary foreground (`dim`);
     - the *Current* pill and *Use* unchanged for now (PHASE02 replaces them);
     - then *Edit* and *Delete*.
   - Its integrations sit under a 1 px subtle rule inside the card, lined up with the label. The
     *Local only* line and *Connect an account* follow, as today.
4. Tests:
   - `ProfilesPageTests`, a new view test:
     - each profile is in its own `profilecard`, and two cards are at least 8 px apart;
     - the label is 15 px SemiBold, the summary 13 px in the secondary brush;
     - the monogram is 36 px and shows the label's initials;
     - the integrations of a profile are inside its card.
   - Initials (`Work` → `W`, `Open source` → `OS`, `""` → `?`), and the author's rule unchanged.
   - The history's avatar is still 18 px.
   - The `profiles-integrations.png` snapshot is rendered again and looked at.

### Acceptance criteria

- Each profile is a framed card, set apart from the next. Its integrations are inside it.
- The label, the identity line and the mark are clearly larger than before, and the mark tells
  profiles apart.
- The history's avatars are unchanged.
- Zero warnings; the whole suite is green.

## PHASE02 — The active profile and Make active

**Status:** DONE — see `docs/done/FEATURE-0ED1-PHASE02.md`
**Branch:** `feature/feature-0ed1-phase02-active-profile`

### Steps

1. The active card:
   - `Border.profilecard.active`, bound to `IsCurrent`, is outlined in the accent (`ActionAccentBrush`).
     The frame keeps its thickness, so nothing moves when the active profile changes.
   - At the header's right, in the place of the action, it says **Active**: a filled `CheckCircle` and
     the word, in the accent.
   - The marker is named `ActiveMarker`, with the tip *Git commits as this profile: your global
     configuration has its name and email* and the automation name *Active profile*.
   - The *Current* pill goes.
2. **Make active** replaces *Use*:
   - a `Button.toolbar` with an icon and the words *Make active*, at the page's normal size;
   - shown while `CanUse`, so never beside the marker, and never on a profile without a name and email;
   - its tip says what it writes;
   - its automation name is *Make this profile active*.
3. The words follow (view-model names unchanged):
   - the empty state (*… switch between them with Make active.*);
   - *Copy from active profile*, its tip and its automation name;
   - the note after a switch, *<label> is now active*;
   - the start window's picker comment;
   - the README's Profiles bullet.
4. Tests:
   - `TheViewMarksTheCurrentProfileAndOffersUseOnTheOthers` becomes
     `TheViewMarksTheActiveProfileAndOffersMakeActiveOnTheOthers`:
     - the active card has the `active` class and the marker;
     - every other card has *Make active* and no marker;
     - a profile without a name and email has neither;
     - no TextBlock reads *Current*.
   - Pressing *Make active* switches: the outline, the marker and the button all move to the other
     profile.
   - The note, and the copy button's new name.
   - The snapshot is rendered again and looked at.

### Acceptance criteria

- The active profile is unmistakable: outlined, with a check mark and the word *Active*, and not shown
  by colour alone.
- Switching is a labelled *Make active* button on every other profile that can be switched to.
- *Current* and *Use* no longer appear on the page.
- Zero warnings; the whole suite is green.

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| The shape of the list | A framed card per profile, 8 px apart, its integrations inside | Clear separation; the accounts are visibly the profile's; the active state has a frame to live on | Rows with dividers: integrations still float between two lines. A table: integrations do not fit a grid |
| The mark | A 36 px monogram of the label, with a stable colour | Two profiles look different at a glance; the app already draws monograms | A larger `UserCircle`: the same picture on every card. The identity's initials: two profiles of one person would match |
| The monogram control | `AuthorAvatar` made size-able, with a role class | One monogram in the app; the history's stays as it is | A new `ProfileAvatar`: a copy of the same control |
| Sizes | Label 15 SemiBold; identity 13, secondary foreground | Above the expander's own 14/12 for a card title; secondary rather than faint, legible in the dark theme | 14/12: barely larger than today |
| How *active* is shown | An accent outline, plus a filled check and the word *Active* | Visible from across the page, and not colour alone | A pill (what was disliked). Colour alone: fails colour-blind readers |
| How to switch | A labelled *Make active* button on every other card | Names what it does, and pairs with *Active*; a real button size | The whole card clickable: an accidental click rewrites the global git identity, and the card holds other buttons. A radio button: stays ticked after a failed write |
| The word | *Active*, in every visible place; the view model keeps `IsCurrent` | One word for one idea on screen; renaming code buys nothing | *Current*: the word the user disliked. *In use*: reads worse as a button pair |
| A profile without a name and email | Neither marker nor button, as today | Its summary already says *No name or email*; switching to it would unset git's identity | A disabled button: one more control saying nothing new |
| Phasing | Cards and type first, the active state second | Each phase one reviewable commit; PHASE01 changes no behaviour | One dev: a larger diff mixing layout and behaviour |

## Out of scope

- The *Earlier integrations* card and the integration rows' own layout. They keep their template, and
  now sit inside a profile's card.
- The start window's profile picker, which already shows the profile you work as.
- The global identity and the repository's identity sections, beyond the copy button's wording.
