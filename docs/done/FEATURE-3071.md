# FEATURE-3071 — A narrower navigation rail

**Item:** FEATURE-3071 — A narrower navigation rail
**Branch:** `feature/feature-3071-narrower-rail`
**Run:** feature/2026-09-29-rail-clone-history

## Summary

Both navigation rails were 96 wide. Each is now the narrowest multiple of 4 that keeps its window's
longest label on one line, with 2 to spare:

- **Repository window: 72.** Its longest labels are "Changes" and "Conflicts", 46 in Inter 11.
- **Start window: 88.** Its longest label is "Repositories", 64.4.

The space on each side of the widest label goes from 24.5 to 12.5 in the repository window, and from
15.3 to 11.3 in the start window. Both rails now leave about the same room around their labels.

## Files / modules touched

**Created**

- `tests/Enigma.GitClient.App.UnitTests/NavigationRailTests.cs` — for each rail (a theory over both
  resources), laid out in Inter in the real window:
  - the rail is its resource wide, and narrower than 96;
  - every label, *Conflicts* included, stays on one line with at least 2 to spare;
  - the rail is no wider than that needs: its tightest label has less than one more step of 4 to
    spare.
- `docs/done/FEATURE-3071.md`

**Modified**

- `src/Enigma.GitClient.App/App.axaml` — `RepositoryRailWidth` (72) and `StartRailWidth` (88), with
  the rule and the measurements behind them.
- `src/Enigma.GitClient.App/Views/MainWindow.axaml`, `Views/StartWindow.axaml` —
  `PaneSize="{StaticResource …RailWidth}"`.
- `docs/roadmap.md`, `docs/plan/FEATURE-3071.md` — statuses.

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| Where the widths live | `App.axaml`'s resources, beside `MonospaceFontFamily` | That's where the application's shared values live. `Styles.axaml` is a `Styles` file of selectors |
| Room kept to spare | 2 per label | Layout rounding at a fractional scale can take up to about a device pixel. Otherwise "Changes" would fit 68 with 1 to spare, too close |
| How the tests measure | The real layout: `LayoutInformation.GetPreviousMeasureConstraint` of each label, in windows given Inter from `avares://Avalonia.Fonts.Inter/Assets#Inter` | Measures the room the library's item actually gives a label, not padding and margins added up here. That sum was wrong the first time (see below) |
| Registering Inter for all tests | No; only these windows get it | It would change the text layout that other tests assert (the dialog wrap tests) |

## Deviations & follow-ups

- **Two widths instead of one.** The plan chose one shared width. That rested on an item chrome of 19,
  but the library's `NavigationItem` applies its `Margin="1"` twice: on the control, and through
  `{TemplateBinding Margin}` on its template's panel. So the chrome is 21. At that chrome a shared
  width is pinned at 88 by "Repositories", a label the repository window never shows. That rail
  would have lost only 8 of its 96, leaving the window where the rail is used nearly as wide as
  before. Each rail now follows the same rule on its own labels.
- The double margin looks unintended in Enigma.Avalonia.Desktop. It's harmless here; worth a look
  upstream.
- Line endings: no CRLF churn.

## Documentation sweep

No prose doc mentions the rail's width (README, RELEASENOTES, SECURITY, `docs/RELEASE.md`). No edit.

## Build/test evidence

- `dotnet build Enigma.GitClient.slnx --no-incremental`: 0 warnings, 0 errors.
- `dotnet test --solution Enigma.GitClient.slnx`: **2519 passed**, 0 failed, 0 skipped (6 new).
- Fix budget: no fix cycle. A throwaway measurement, before any fix, gave the Inter widths, and the
  first run of the new tests showed "Repositories" wrapping at 84. That is what exposed the 21 chrome.
