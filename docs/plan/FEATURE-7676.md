# FEATURE-7676 — Diff panel defaults and typography

**Status:** TODO
**Type:** FEATURE
**Branch:** `feature/feature-7676-phase01-side-by-side`, `feature/feature-7676-phase02-diff-font`
**Run:** feature/2026-09-16-diff-panel-layout

## Objective

Open the diff panel side by side, and make the face and the size the diff is drawn in a preference
— with a bigger size than the 12 px the styles hard-code today.

## Context & constraints

- Asked for as "in the diff panel, show by default side by side" and "make the font family and font
  size configurable and by default have a bigger font size".
- `AppSettings.DiffView` (`Core/Configuration/AppSettings.cs`) defaults to `DiffView.Unified` and is
  persisted, so a default-only change reaches nobody who has already run the client — exactly the
  situation `FEATURE-0183-PHASE02` answered with a schema version and a migration. That precedent is
  followed rather than re-invented.
- `DiffViewerViewModel.Apply(AppSettings)` already takes the stored `DiffView` on at startup and on
  every change, so the viewer needs no new plumbing for the default itself.
- The typography is style constants today: `Themes/Styles.axaml` sets `FontFamily` to the
  `MonospaceFontFamily` resource and `FontSize` to 12 on `diff|DiffLineText`, 11 on
  `TextBlock.diffnumber` and 12 on `TextBlock.diffmarker`. The row templates hard-code the gutter
  and marker columns as `48,48,18,*` (unified) and `48,18,*` (per side), which a larger face
  overflows.
- `App.ApplyTheme` is the house pattern for "a preference the application itself applies": a static
  method hooked to `ISettingsService.Changed` in `OnFrameworkInitializationCompleted`. The diff
  typography follows it, so one preference reaches every `DiffLineText` in the application —
  including the conflict-resolution page — without a binding per row.
- `BUG-1D34` builds on this phase: its scroll arithmetic is in character columns and reads the
  character width this item measures.

## PHASE01 — Side by side by default

**Status:** TODO

### Steps

1. `AppSettings.DiffView` default `Unified` → `SideBySide`.
2. Bump `AppSettings.CurrentVersion` to 3 and add the version 3 case to `SettingsService.Migrate`: a
   file written before version 3 whose `DiffView` is still `Unified` — version 2's own default, and
   so a value nobody chose — is moved to `SideBySide`; a file that says `SideBySide` is already
   there, and a version 3 file is never touched.
3. Update the tests that assert the old default.

### Acceptance criteria

- A fresh install opens the diff panel side by side: `AppSettings.Defaults.DiffView` is
  `SideBySide`, and a `DiffViewerViewModel` built on a fresh settings store reports
  `IsSideBySide`.
- A stored version 2 file saying `"diffView": "Unified"` reads back as `SideBySide`, with its
  version brought to 3; one saying `SideBySide` is unchanged; a version 3 file saying `Unified` is
  left exactly as it is.
- The version 2 row-height migration still applies to a version 1 file — the two cases compose.
- `dotnet build` clean with zero warnings; the whole suite green.

## PHASE02 — Configurable diff font

**Status:** TODO

### Steps

1. Two preferences on `AppSettings`: `DiffFontFamily` (string, empty meaning the application's own
   monospace stack) and `DiffFontSize` (double, default 14). `Normalised()` trims the family and
   clamps the size to 8–32.
2. A `DiffTypography` static class in `App/Controls/Diff`: measures the advance width of one
   character for a face and size (cached), derives the line-number size, the gutter width and the
   marker width from it, exposes the result as `DiffTypography.Current`, raises `Changed`, and
   writes `DiffFontFamily`, `DiffFontSize`, `DiffLineNumberFontSize`, `DiffGutterWidth` and
   `DiffMarkerWidth` into `Application.Current.Resources`.
3. Hook it in `App.OnFrameworkInitializationCompleted` beside `ApplyTheme`, and seed the five
   resources with the current values in `App.axaml` so a design-time or test load has them.
4. `Themes/Styles.axaml`: the three diff text styles take their face and size from the new
   resources, and `TextBlock.diffnumber` / `TextBlock.diffmarker` take their `Width` from
   `DiffGutterWidth` / `DiffMarkerWidth`.
5. The row templates' fixed gutter columns become `Auto` — in `DiffViewerView.axaml` (both
   templates) and in `ConflictResolutionPageView.axaml`'s line template — so the styled widths
   drive them.
6. The settings page's Diff card gains "Font" (a ComboBox of the system's monospace faces, with
   "Default" first) and "Font size" (a spinner, 8–32). `SettingsPageViewModel` exposes both, and the
   font list is built lazily so the container can be resolved without a font manager behind it.

### Acceptance criteria

- With no preference set, the diff draws in the application's monospace stack at 14 px: the seeded
  resources and `DiffTypography.Current` agree, and the rendered line is measurably taller than it
  was at 12 px.
- Setting the size to 20 changes what a rendered `DiffLineText` measures, and widens the gutter so a
  six-digit line number still fits inside it.
- Setting a family the machine does not have does not throw: the text still draws, through
  Avalonia's own fallback.
- Clearing the family goes back to the application's monospace stack.
- The preference survives a restart, and resetting the preferences puts both back.
- The font list offers "Default" first and never offers a proportional face when the machine has at
  least one monospace one.
- `dotnet build` clean with zero warnings; the whole suite green.

## Out of scope

- A per-viewer font override in the diff toolbar — the prompt asked for a preference, not a second
  place to set one.
- Changing the application's other monospace text (short hashes, the hunk band) — they keep the
  `MonospaceFontFamily` resource they have always used.
- Line-height, letter-spacing or a theme for the diff text.

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| Reaching files that already say `Unified` | Schema v3 with a migration | The v2 `GraphRowHeight` migration is the house answer to exactly this, and a default-only change would be invisible to every existing user | default only; rewriting everyone's `DiffView` regardless of version |
| Where the typography is applied | Application resources, written by a static applier hooked to the settings | Mirrors `App.ApplyTheme`; one preference reaches every `DiffLineText`, the conflict page included, with no binding per row | a property per metric on `DiffRenderOptions`, bound in every template (six bindings × six sites, and the conflict page left behind) |
| The new default size | 14 | Noticeably bigger than 12 and still dense enough for a real patch at the 36 px row rhythm | 13 (barely a change); 16 (halves how much of a patch fits) |
| The gutter width at a larger size | Derived from the measured character width | Six-digit line numbers must not overflow, and a fixed 48 px breaks the moment the face grows | keeping 48; shared size groups, which jitter as rows realise under virtualisation |
| The family picker | A ComboBox of the monospace faces, "Default" first | Typing a face name blind is a bad settings experience, and a list of every installed font is worse | a free-text box; every system family |
| What an empty family means | The application's monospace stack | It is the only sane "I have no opinion", and it keeps the existing look as the default | storing the stack itself, which would freeze today's list into everyone's settings file |
