# FEATURE-0ED1-PHASE01 — One card per profile, larger type

**Item:** FEATURE-0ED1 — Profiles as cards, a clear active one, PHASE01 — One card per profile, larger type
**Branch:** `feature/feature-0ed1-phase01-profile-cards`
**Run:** feature/2026-10-23-search-escape-profile-cards

## Summary

Each profile in the Profiles page's *Profiles* expander is now a card of its own, in larger type, with a
monogram for its mark. How the active profile is shown, and how it is switched, are PHASE02's: the
*Current* pill and the *Use* button are still there, unchanged, inside the card.

- **The card.** `Border.profilecard` (`Themes/Styles.axaml`):
  - `EnigmaSurfaceBrush` on the section's lower surface, framed by a 1 px `EnigmaBorderSubtleBrush`;
  - radius 6, padding `16,14`;
  - two cards 10 px apart, through the items panel's spacing.
- **The header.**
  - **The mark** is a 36 px monogram of the label, in its stable colour: *W* for Work, *OS* for Open
    source. It replaces the 16 px grey `UserCircle`, which was the same picture on every card.
  - **The label** is 15 px SemiBold, from the default size.
  - **The name and email** are 13 px in the secondary foreground, from 11 px in the tertiary one.
- **The accounts.** A profile's integrations, its *Local only* line and *Connect an account* are inside
  its card, under a 1 px rule (`Border.profilerule`). They are lined up with the label, past the
  monogram.
- **The monogram control.** `AuthorAvatar` can now be drawn at any size:
  - its disc fills the control and takes the control's `CornerRadius` and `FontSize`;
  - its theme keeps 18 px, radius 9 and 9 px letters, so the history's author column is unchanged;
  - the role class `AuthorAvatar.profile` draws it at 36 px, radius 18, 14 px letters.
- **The initials.** The rule a commit author's avatar follows is now a public helper,
  `GitSignature.InitialsOf(text)`, and `GitSignature.Initials` calls it. The rule is the first letters
  of the first and last words, or `?`.
  - `ProfileRowViewModel.Initials` applies it to the label.
  - It uses the label rather than the identity, because two profiles of one person carry the same
    name.

## Files / modules touched

**Created**

- `docs/done/FEATURE-0ED1-PHASE01.md`

**Modified**

- `src/Enigma.GitClient.Core/History/GitSignature.cs`: `InitialsOf`. `Initials` delegates to it, with
  the same results.
- `src/Enigma.GitClient.Desktop/Themes/Controls.axaml`: the `AuthorAvatar` template is size-able.
- `src/Enigma.GitClient.Desktop/Themes/Styles.axaml`: `Border.profilecard`, `Border.profilerule`,
  `AuthorAvatar.profile`.
- `src/Enigma.GitClient.Desktop/ViewModels/Pages/ProfilesPageViewModel.cs`: `ProfileRowViewModel.Initials`.
- `src/Enigma.GitClient.Desktop/Views/Pages/ProfilesPageView.axaml`:
  - the `ProfileRow` template is a card;
  - the profiles' items panel spaces the cards;
  - the `gitcontrols` prefix, as `MainWindow.axaml` names the app's controls beside the library's.
- Tests:
  - `tests/Enigma.GitClient.Core.UnitTests/History/GitCommitTests.cs`: `InitialsOf_AnyText_FollowsTheAuthorsRule`
    (six cases). The author's cases are unchanged and still pass.
  - `tests/Enigma.GitClient.Desktop.UnitTests/HistoryPageTests.cs`: `AuthorAvatar_IsDrawnAtItsRolesSize`
    covers the default 18 px (radius 9, 9 px letters) and the `profile` role at 36 px (radius 18, 14 px
    letters). The disc fills the control.
  - `tests/Enigma.GitClient.Desktop.UnitTests/ProfilesPageTests.cs`: `TheViewPutsEachProfileOnACardOfItsOwn_InLargerType`.
    It checks:
    - two framed cards, at least 8 px apart;
    - the label at 15 px SemiBold;
    - the identity at 13 px in the secondary brush;
    - two 36 px monograms reading *W* and *OS*;
    - each card holding its own *Connect an account* and *Local only* line.
- `docs/roadmap.md`, `docs/plan/FEATURE-0ED1.md`: statuses.

## Deviations & follow-ups

- **The gap between cards** is 10 px, where the plan said 8. That is the spacing the expander's content
  already uses between its parts. The test asserts at least 8.
- **The rule** inside the card spans the card's width, rather than starting at the label. It parts the
  header from the accounts as the expander's own separator parts its header from its content.
- **Long labels.** A label still sits in a horizontal panel beside the *Current* pill, so it is not
  trimmed. PHASE02 removes the pill, and the label can then trim.
- **The snapshot.** `profiles-integrations.png` (written by `ProfileIntegrationsTests`) was rendered and
  looked at:
  - two framed cards, *W* in blue and *H* in violet, the 15 px labels;
  - Work's GitHub account inside Work's card, Home's *Local only* line inside Home's;
  - the *Earlier integrations* card unchanged.
- Documentation sweep: nothing the README or other docs say about the Profiles page was made wrong.
  Nothing was edited.

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| One initials rule or two | `GitSignature.InitialsOf`, public, used by both | One rule for every monogram in the app; the author's results are unchanged |
| Where the card's style lives | `Themes/Styles.axaml`, beside `Border.pill` | As the plan said; PHASE02's `.active` variant joins it there |
| The avatar at another size | A size-able template plus a role class | The icon scale's convention: a role says how large; the history's column is untouched |
| The prefix for the app's controls | `gitcontrols` | The page's `controls` is the library's, and `MainWindow.axaml` already uses `gitcontrols` for this case |

## Build/test evidence

- `dotnet build Enigma.GitClient.slnx`: 0 warnings, 0 errors.
- `dotnet test --solution Enigma.GitClient.slnx`: 3468 total, 3465 passed, 3 skipped, 0 failed. No
  fix cycle was needed.
  - That is +9 over FEATURE-715E's 3459: six initials cases, two avatar sizes, and the card test.
