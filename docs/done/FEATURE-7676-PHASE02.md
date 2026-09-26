# FEATURE-7676-PHASE02 — Configurable diff font

**Item:** FEATURE-7676 — Diff panel defaults and typography (PHASE02)
**Branch:** `feature/feature-7676-phase02-diff-font`
**Run:** feature/2026-09-16-diff-panel-layout

## Summary

The face and the size a diff is drawn in are now preferences — `DiffFontFamily` (empty meaning the
application's own monospace stack) and `DiffFontSize`, which defaults to 14 where the styles used to
hard-code 12.

They are applied the way the theme is applied, not the way a ViewModel property would be. A new
`DiffTypography` derives the metrics from the two preferences and publishes five application
resources — the face, the text size, the line-number size, the gutter width and the marker width —
and `App` calls it once in `Initialize` with the defaults (so the styles never resolve a missing
key) and again from `ISettingsService.Changed`. The three diff styles then read those resources with
`DynamicResource`, which means one preference reaches every `DiffLineText` in the application at
once: both of the viewer's renderings *and* the conflict-resolution page's three panes, with no
binding added to any of the six row templates.

The two derived widths are the part that makes a larger face usable rather than merely larger. A
gutter fixed at 48 px clips a six-digit line number the moment the text grows, so the gutter and
marker columns are computed from the measured character width and published as resources too; the
row templates' `48,48,18,*` and `48,18,*` columns became `Auto`, and the styled `Width` on the
number and marker blocks drives them. A theory over four sizes from 8 to 32 asserts the six-digit
number keeps fitting.

Measuring turned out to need a guard. This machine's font collection lists a *Cascadia Mono* whose
glyphs cannot be created, and asking it for a typeface throws — so `TryMeasure` reports failure
instead, a family that cannot be realised is never offered in the picker, and a width that cannot be
measured falls back to an estimate rather than taking the settings page down.

The settings page's Diff card gained "Font" — a dropdown of this machine's monospace faces behind a
"Default" entry, built on first read because enumerating fonts needs a font manager the container is
sometimes resolved without — and "Font size", a 8–32 spinner. Only monospace faces are offered: the
diff expands tabs to a column grid, which means nothing in a proportional face.

## Files / modules touched

**Created — App**

- `Controls/Diff/DiffTypography.cs` — `DiffMetrics`, the measurement and the five published
  resources, plus `MeasureCharacterWidth`, `IsMonospace` and `MonospaceFamilies`

**Created — tests**

- `tests/Enigma.GitClient.App.UnitTests/DiffTypographyTests.cs` — 14 tests over the metrics, the
  resources, the clamping, the fallbacks and the font list

**Modified — Core**

- `Configuration/AppSettings.cs` — `DiffFontFamily` and `DiffFontSize`, trimmed and clamped in
  `Normalised()`

**Modified — App**

- `App.axaml.cs` — seeds the typography in `Initialize` and re-applies it on every settings change
- `Themes/Styles.axaml` — `diff|DiffLineText`, `TextBlock.diffnumber` and `TextBlock.diffmarker`
  take their face, size and width from the published resources
- `Views/Panels/DiffViewerView.axaml` — the three row grids' fixed gutter columns became `Auto`
- `Views/Pages/ConflictResolutionPageView.axaml` — the same, for its line template
- `ViewModels/Pages/SettingsPageViewModel.cs` — `DiffFont`, `DiffFontSize`, the lazily built
  `DiffFonts` list and the `DefaultFontLabel` constant
- `Views/Pages/SettingsPageView.axaml` — the Font and Font size rows in the Diff card

**Modified — tests**

- `tests/Enigma.GitClient.Core.UnitTests/Configuration/SettingsServiceTests.cs` — the two new
  defaults, the round trip and the clamping of a hand-edited size and an untrimmed family
- `tests/Enigma.GitClient.App.UnitTests/SettingsPageTests.cs` — four tests over the font list, the
  "Default" label, a stored family this machine lacks, and the size preference

**Modified — docs**

- `README.md`, `RELEASENOTES.md` (the preference lists), `docs/roadmap.md`,
  `docs/plan/FEATURE-7676.md`

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| Where `MeasureCharacterWidth` lives | On `DiffTypography`, not on `DiffLineText` as `BUG-1D34-PHASE01` planned | This phase needed the primitive first, and the metrics source is the natural owner of it; the control will consume it rather than grow its own copy |
| Seeding the five resources | `App.Initialize` calls `Apply(AppSettings.Defaults)` | A second copy of the numbers in `App.axaml` is a second thing to drift; deriving them once means the seeded values are the real ones by construction |
| A face the font manager cannot realise | Reported as a failure, not thrown | A machine with a broken font entry must still open a settings page; the family is dropped from the picker and the width falls back to an estimate |
| Line-number size | Two below the text size, floored at 9 | Keeps the gutter quieter than the code at every size, which is what the old 11-against-12 pairing was doing by hand |
| Gutter width | Six digits of the line-number face plus the style's own 8 px padding | Six digits is a million-line file; a fixed width is exactly what broke when the face grew |
| Which faces the picker offers | Monospace only, "Default" first, plus a stored family this machine lacks | The diff's column grid assumes a fixed advance, and a dropdown that cannot show what is in force shows nothing at all |
| Scope of the preference | Every `DiffLineText`, conflict page included | One preference producing two different-looking code views would be a bug report of its own |

## Deviations & follow-ups

- **Deviation from the plan (`BUG-1D34-PHASE01`, step 2).** That phase planned
  `DiffLineText.MeasureCharacterWidth`; it is `DiffTypography.MeasureCharacterWidth` instead, for
  the reason above. The next dev consumes it rather than re-declaring it.
- **Deviation from the plan (this phase, step 3).** The plan said to seed the resources in
  `App.axaml`; they are seeded from `App.Initialize` instead, which removes the duplicated numbers.
- **Follow-up — the application's monospace stack resolves to nothing on this machine.** It names
  Cascadia Mono, JetBrains Mono, DejaVu Sans Mono, Consolas, Menlo and `monospace`; `fc-list` here
  reports none of them (the installed monospace faces are *Adwaita Mono*, *FreeMono* and the
  *JetBrainsMono Nerd Font* family), so the default diff face falls through to the proportional UI
  font. This predates this work — it is what the styles have always resolved — and this phase is
  precisely its workaround, since the picker now offers the faces that *are* installed. Worth a
  small follow-up to widen the stack (a `… Nerd Font Mono`, `Liberation Mono`, `Noto Sans Mono`
  entry) so the default is monospace on a stock Linux box.
- **Observation.** One test asserting "the application's stack is monospace" was withdrawn for the
  same reason: which of five faces a build agent has is not this codebase's business. What is
  asserted instead is that the stack always measures, and that every face the picker offers is
  monospace.
- **Line endings (recommendation only):** no CRLF churn observed in the touched files. No action
  taken.

## Build/test evidence

```
dotnet build Enigma.GitClient.slnx
  0 Warning(s)
  0 Error(s)

dotnet test --solution Enigma.GitClient.slnx
  Test run summary: Passed!
  total: 1605  failed: 0  succeeded: 1605  skipped: 0
```

Twenty-two tests are new. Two fix cycles were needed, both in measurement rather than in the
feature: the first found that enumerating the system fonts can throw on a family whose glyphs cannot
be created, which is now handled rather than propagated; the second withdrew the environment-
dependent assertion described above. `SyncServiceTests.FetchAsync_ReportsItsProgress` failed once
during the first cycle and passed on both subsequent runs without anything being changed near it —
it reads `git fetch`'s progress output, which is timing-dependent; nothing in this dev touches sync.

## Documentation sweep

- `README.md` — the "Preferences that stick" line listed the diff's shape and context; it now names
  the font as well.
- `RELEASENOTES.md` — the same list under **Preferences**, which now names the font family and size.
