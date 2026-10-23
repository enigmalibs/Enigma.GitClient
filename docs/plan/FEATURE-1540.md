# FEATURE-1540 — Larger account rows and row actions

**Status:** DONE — see `docs/done/FEATURE-1540.md`
**Type:** FEATURE
**Branch:** `feature/feature-1540-larger-profile-controls`
**Run:** feature/2026-10-23-larger-profile-controls

## Objective

In the Profiles page's *Profiles* expander, an account connected to a profile reads without effort, and
the actions on a profile and on an account are easy to hit:

1. **The account's mark is bigger.** The host's logo on the left of an account, the GitHub cat for a
   GitHub account, is drawn larger.
2. **The line under the account's name is bigger and lighter.** The host and user name read as a
   caption, not as a placeholder.
3. **Browse repositories is a real button.** It is bigger, and so is its text.
4. **Edit and remove are bigger**, on a profile (*Edit this profile*, *Delete this profile*) and on an
   account (*Disconnect this account*).

## Context & constraints

- **The page** is `Views/Pages/ProfilesPageView.axaml`.
  - `IntegrationRow` draws one account:
    - the logo is an `ei:Icon` with `Classes="row dim"`, so 16 px in the secondary foreground;
    - the name is at the inherited size (14);
    - the details (`Host · UserName`) are at 11 px, `faint` (the tertiary foreground);
    - then *Replace token* and *Move to…* (both conditional) and *Browse repositories*, all
      `Button.choice`;
    - then *Disconnect this account*, a `Button.toolbar.rowaction` holding an `Icon.row`.
  - `ProfileRow` draws one profile card. *Edit this profile* and *Delete this profile* are
    `Button.toolbar.rowaction` holding an `Icon.row`.
  - The page's `UserControl.Styles` already gives `ItemsControl.profiles` and
    `ItemsControl.integrations` row actions no frame and `7,6` padding, so they are 28 px square.
- **`Button.choice`** (`Themes/Styles.axaml`: 11 px, `8,2` padding, secondary foreground) is shared with
  the conflict resolution page and the repositories dialog. **`Icon.row`** (16 px) and the rule giving
  a row action's `Icon.row` the full foreground are shared with every list and tree in the app. Neither
  may change outside this page.
- **The card's own scale** (FEATURE-0ED1):
  - a 36 px monogram;
  - the label at 15 px SemiBold;
  - the identity at 13 px `dim`;
  - *Make active* and *Connect an account* as `Button.toolbar` at the page's normal size, about 30 px
    high.
- The `IntegrationRow` template also draws the *Earlier integrations* card's rows.
- **Tests pinned to the row:**
  - `ProfileIntegrationsTests.TheViewShowsEachProfilesIntegrationsAndTheEarlierOnes` counts the
    buttons by automation name, and renders `snapshots/profiles-integrations.png`.
  - `ProfilesPageTests.TheViewMarksTheActiveProfileAndOffersMakeActiveOnTheOthers` counts *Edit this
    profile* and *Delete this profile*, and renders `profiles-active-dark.png` / `-light.png`.
  - None of them pins these sizes.
- **Baseline** on `031d3da`: build 0 warnings; 3468 tests, 3465 passed, 3 skipped.

## Steps

1. `ProfilesPageView.axaml`, the `IntegrationRow` template:
   - The logo is 24 px and stays `dim`. It loses the `row` class, which is a 16 px row glyph.
   - The details are 13 px and `dim` (the secondary foreground), the same as the profile card's
     identity line.
2. The page's `UserControl.Styles`:
   - `ItemsControl.integrations Button.choice`: the page's normal type (14 px) and `10,5` padding, so
     about 30 px high, the same as *Make active* and *Connect an account*. It covers the row's three
     labelled buttons (*Browse repositories*, *Replace token*, *Move to…*), so one row never mixes two
     button sizes.
   - The profiles' and integrations' row actions: `8` padding, and their `Icon.row` drawn at 20 px, so
     each is 36 px square. That is the monogram's height and an account row's.
3. Tests (`ProfileIntegrationsTests`), a new view test over a profile with a GitHub account:
   - the logo is the GitHub logo, 24 px;
   - the details line is 13 px, in the secondary foreground;
   - *Browse repositories* is at 14 px and at least 28 px high;
   - *Edit this profile*, *Delete this profile* and *Disconnect this account* each hold a 20 px icon
     and are at least 36 px square;
   - a `Button.choice` outside the page keeps its 11 px.

   Then `profiles-integrations.png` is rendered again and looked at.

## Acceptance criteria

- An account's logo is clearly larger than before. The line under the account's name is larger and in
  the lighter, secondary grey.
- *Browse repositories* (and the row's other labelled buttons) is about the height of *Connect an
  account*, with text at the page's normal size.
- Edit and delete on a profile, and disconnect on an account, are visibly larger, and all three are the
  same size.
- Nothing outside the Profiles page changes size.
- Zero warnings; the whole suite is green.

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| Where the sizes live | The page's own `UserControl.Styles` and templates | `Button.choice`, `Icon.row` and the row-action rule are shared by other pages, which must not change | Editing `Styles.axaml`: the conflict page and the repositories dialog would grow too |
| The logo's size | 24 px (was 16), still `dim` | Clearly larger; below the profile's 36 px monogram, so the card keeps its hierarchy; about the height of the two lines beside it | 20 px: barely noticed. 32 px: competes with the monogram |
| The details line | 13 px, secondary foreground (`dim`), was 11 px tertiary (`faint`) | The card's identity line is already 13 px `dim`; secondary is the caption role and the lighter grey in the dark theme | 12 px: still small beside a 14 px name. The full foreground: no longer reads as a detail |
| Browse repositories | 14 px text, `10,5` padding, about 30 px high | The same height and type as *Make active* and *Connect an account* on the same card | 13 px: a third text size for a labelled button on one card. Restyling it as a `Button.toolbar` with an icon: not asked |
| *Replace token* and *Move to…* | The same size as Browse, through one `ItemsControl.integrations Button.choice` rule | They sit on the same row; two button sizes on one row look broken | Browse alone: mismatched buttons whenever a token problem or an earlier integration shows |
| Edit / delete / disconnect | A 20 px icon and 8 px padding, so 36 px square (was 16 px in 28 px); one size for profiles and accounts | Matches the monogram and an account row's height; one size for one role | 18 px icon at 30 px: barely different. 24 px icon: heavier than the labelled buttons beside it |
| The earlier integrations | They follow, the same template | One row shape for one thing | A second template: a copy to keep in step |
| The row actions' 0.55 resting opacity | Unchanged | Not asked; it is the app-wide row-action convention | Full opacity: a different change |
| Phasing | One dev | One XAML file and one view test; one reviewable commit | Three phases: three tiny commits on the same lines |

## Out of scope

- Every other page, the repositories dialog, and the app-wide `Button.choice`, `Icon.row` and
  row-action styles.
- The account name's size and colour, the profile card's header, *Make active* and *Connect an
  account*.
- The row actions' resting opacity.
