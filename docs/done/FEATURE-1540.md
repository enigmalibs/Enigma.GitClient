# FEATURE-1540 — Larger account rows and row actions

**Item:** FEATURE-1540 — Larger account rows and row actions
**Branch:** `feature/feature-1540-larger-profile-controls`
**Run:** feature/2026-10-23-larger-profile-controls

## Summary

In the Profiles page's *Profiles* expander, an account reads without effort and the actions on a profile
and on an account are easy to hit. Every size is set by the page itself, so nothing else in the app
changes.

- **The account's logo** (the GitHub cat for a GitHub account) is 24 px, was 16. It stays in the quiet
  secondary grey, and sits below the profile's 36 px monogram.
- **The line under the account's name** (`github.com · octocat`) is 13 px in the secondary foreground
  (`dim`). It was 11 px in the tertiary one (`faint`). That is the lighter grey on the dark theme, and
  it matches the profile's own identity line.
- **Browse repositories** is at the page's normal type (14 px, was 11) with `10,5` padding. It is about
  30 px high, as tall as *Make active* and *Connect an account* on the same card.
  - The row's other labelled buttons, *Replace token* and *Move to…*, get the same size from the same
    rule, so a row never mixes two button sizes.
- **Edit and delete on a profile, and disconnect on an account**, draw a 20 px glyph (was 16) in a
  36 px square (was 28). That is one size for the three, at the monogram's height.
- The *Earlier integrations* rows share the account template, so they follow.

## Files / modules touched

**Created**

- `docs/done/FEATURE-1540.md`

**Modified**

- `src/Enigma.GitClient.Desktop/Views/Pages/ProfilesPageView.axaml`:
  - the page's `UserControl.Styles`:
    - the row actions' padding is `8`;
    - their `Icon.row` is drawn at 20 px;
    - a new `ItemsControl.integrations Button.choice` rule sets 14 px type and `10,5` padding;
  - the `IntegrationRow` template: the logo at `Size="24"` with `Classes="dim"` (no longer `row`), and the
    details at 13 px `dim`.
- `tests/Enigma.GitClient.Desktop.UnitTests/ProfileIntegrationsTests.cs`: the new view test
  `TheViewDrawsAnAccountAndTheRowActionsAtTheCardsScale`, over a profile with a GitHub account. It
  checks:
  - the logo is the GitHub logo, 24 px and `dim`;
  - the details line is 13 px in the secondary brush;
  - *Browse repositories* and its text are 14 px, and the button is at least 28 px high;
  - *Edit this profile*, *Delete this profile* and *Disconnect this account* each hold a 20 px icon
    and are at least 36 × 36 px;
  - outside the page, a `choice` button keeps 11 px and a row action's `Icon.row` keeps 16 px.
- `docs/roadmap.md`, `docs/plan/FEATURE-1540.md`: statuses. The item is `DONE`.

## Deviations & follow-ups

- None from the plan.
- **The snapshots.** Three were looked at:
  - `profiles-integrations.png` (dark): the cat, the lighter `github.com · octocat`, the 30 px *Browse
    repositories* and *Move to…*, and the 36 px pencil and bins. They line up down the card's right
    edge, on the profile rows and on the account rows.
  - `profiles-active-light.png`: the larger edit and delete read clearly on the light card.
  - `profiles-active-dark.png`, rendered again by the same run.
- The row actions keep the app-wide 0.55 resting opacity, which was not asked about. A follow-up could
  raise it on the cards if the larger glyphs still read as faint.
- Documentation sweep: the README, `SECURITY.md` and the other prose docs give no sizes for these
  controls, so nothing there was made wrong and nothing was edited. `RELEASENOTES.md` is written by the
  release item.
- Line endings: the touched files are LF throughout. No CRLF recommendation.

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| How the logo is sized | `Size="24"` on the icon, and `row` dropped from its classes | `row` names the app's 16 px row glyph. The page already sizes its other one-off icons inline (`LockSimple`, `Warning`) |
| How the row actions' glyphs are sized | Page styles on `Button.toolbar.rowaction ei\|Icon.row` under the two lists | The icons keep `row`, which the app-wide rule needs to give a row action's glyph the full foreground. A page style changes only their size |
| How "unchanged elsewhere" is proven | The same test draws a bare `choice` button and a row action in a window of their own | It shows the rules are scoped to the page, without depending on another page's view model |

## Build/test evidence

- `dotnet build Enigma.GitClient.slnx`: 0 warnings, 0 errors.
- `dotnet test --solution Enigma.GitClient.slnx`: 3469 total, 3466 passed, 3 skipped, 0 failed. No fix
  cycle was needed.
  - That is one test more than the baseline (3468 on `031d3da`), the new view test.
