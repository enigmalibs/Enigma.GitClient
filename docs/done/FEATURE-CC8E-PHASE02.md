# FEATURE-CC8E-PHASE02 — Red discards, one plain question

**Item:** FEATURE-CC8E — Changes: way back, discards, commit box
**Branch:** `feature/feature-cc8e-phase02-red-discards`
**Run:** bugfix/2026-09-28-changes-commit-details-release

## Summary

- **A red for what cannot be undone.** `ActionDangerColor`, with hover and pressed tints and a white
  foreground, is defined in both themes (#D13438 dark, #C42B1C light). The theme package's own error
  red is a text colour, too light to carry white text.
- **The *Discard everything* button is red.**
  - `Button.toolbar.danger` is a red fill with a white glyph, with its own hover and pressed tints.
  - When it cannot be pressed (nothing unstaged), it is the ordinary disabled toolbar button, so red
    always means there is something to lose.
- **One plain question, confirmed in red.**
  - `ContentDialogServiceExtensions.ConfirmDestructiveAsync(title, message, confirmText)` (a C# 14
    extension block on `IContentDialogService`) asks a yes or no: the message, a red confirm button,
    and *Cancel* as the default.
  - The red is the `danger` class, put on the shared host for that one question and removed in a
    `finally`. The library's service resets content and buttons between dialogs, but not classes.
  - `contentDialog|ContentDialog.danger /template/ Button#PART_PrimaryButton` paints the confirm
    button, in its normal, pointer-over and pressed states.
- **Every discard asks it.**
  - *Discard everything*: "Throw away every change in *N files*? This cannot be undone." There is no
    repository name to type any more.
  - The one-row *Discard…*: the same question it asked before, now red.
  - `ConfirmTextDialogViewModel`/`View`, used only by *Discard everything*, are deleted with their DI
    registration.

## Files / modules touched

**Created**

- `src/Enigma.GitClient.App/Services/ContentDialogServiceExtensions.cs`
- `tests/Enigma.GitClient.App.UnitTests/DestructiveConfirmationTests.cs`:
  - only the confirm button confirms (4 theory rows);
  - on a real rendered host, the confirm button is red, the red goes on dismissal, and the next
    question's confirm button is the accent blue again.
- `docs/done/FEATURE-CC8E-PHASE02.md`

**Modified**

- `src/Enigma.GitClient.App/ViewModels/Pages/ChangesPageViewModel.cs` — both discards ask through
  `ConfirmDestructiveAsync`. The private `ConfirmAsync`, the typed-name flow and three usings it needed
  are gone.
- `src/Enigma.GitClient.App/Views/Pages/ChangesPageView.axaml` — *Discard everything* is
  `toolbar danger`, named `DiscardAll`.
- `src/Enigma.GitClient.App/Themes/Styles.axaml` — the `danger` toolbar button states and the dialog's
  red confirm button.
- `src/Enigma.GitClient.App/Themes/Graph.axaml` — the four `ActionDanger*` colours per theme, and their
  brushes.
- `src/Enigma.GitClient.App/DependencyInjection/ServiceCollectionExtensions.cs` — no
  `ConfirmTextDialogView`.
- `tests/Enigma.GitClient.App.UnitTests/ChangesPageTests.cs`:
  - the two typed-name tests are replaced by one-question-red-and-cancelled and
    discards-once-confirmed;
  - new: a one-file discard is red;
  - new: the toolbar button is `danger` and bound.
- `tests/Enigma.GitClient.App.UnitTests/ToolbarButtonLookTests.cs` — the danger look in every state and
  plain when disabled; the colours in both themes.
- `docs/roadmap.md`, `docs/plan/FEATURE-CC8E.md` — statuses.

**Deleted**

- `src/Enigma.GitClient.App/ViewModels/Dialogs/ConfirmTextDialogViewModel.cs`
- `src/Enigma.GitClient.App/Views/Dialogs/ConfirmTextDialogView.axaml`, `.axaml.cs`

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| The message's form | A plain string, as every other question the page asks | The dialog shows it as it did before, and the tests read it the same way |
| Proving the red | A real `ContentDialogService` over a rendered host, reading the confirm button's plate | The scripted dialog double builds a fresh dialog per question, so it cannot show that the shared host loses the red |
| Red on a disabled button | No — the ordinary disabled look | A red button that cannot be pressed would warn about nothing |

## Deviations & follow-ups

- None from the plan.
- Follow-up, unchanged: the other irreversible questions (delete a branch, a tag or a stash; hard
  reset) keep the accent confirm button. They can adopt `ConfirmDestructiveAsync` in one line each.
- Line endings: no CRLF churn.

## Documentation sweep

No document mentions the typed repository name or the button's colour. No edit. The release notes are
FEATURE-10AA's.

## Build/test evidence

- `dotnet build Enigma.GitClient.slnx`: 0 warnings, 0 errors.
- `dotnet test --solution Enigma.GitClient.slnx`: **2477 passed**, 0 failed, 0 skipped. That is 12 new
  tests, less the 2 typed-name tests they replace.
- Fix budget: no fix cycle.
