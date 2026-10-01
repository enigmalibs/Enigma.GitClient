# FEATURE-85E2 — Enigma.Avalonia.Desktop 1.2.0

**Status:** TODO
**Type:** FEATURE
**Branch:** `feature/feature-85e2-enigma-avalonia-desktop-1-2-0`
**Run:** feature/2026-10-01-polish-release-5-1

## Objective

Move the solution to Enigma.Avalonia.Desktop **1.2.0**, which improves the info bars' colours: in the
Dark variant the four severity fills and their borders are lifted to softer, pastel tints on the
Light variant's hues, and the message line paints a new `EnigmaInfoBarMessageForeground` key that
keeps it readable on them.

## Context & constraints

- **Central Package Management:** the version lives in `Directory.Packages.props`
  (`<PackageVersion Include="Enigma.Avalonia.Desktop" Version="1.1.0" />`). The App project
  references the package without a version.
- **1.2.0's nuspec** still pins Avalonia 12.1.1, Avalonia.Themes.Fluent 12.1.1,
  CommunityToolkit.Mvvm 8.4.2 and Enigma.Core 1.0.0, the same as 1.1.0. So the version-coupled Avalonia
  set does not move, and no other package changes.
- **Backward compatible:** its release notes say no key is renamed or removed and every existing
  override still applies. The app overrides no `EnigmaInfoBar*` key: its two `InfoBar` hosts, in
  `MainWindow.axaml` and `StartWindow.axaml`, are plain.
- The `Directory.Packages.props` comment on the Avalonia set names the Enigma.Avalonia.Desktop
  version it is built against. It has to say 1.2.0.

## Steps

1. `Directory.Packages.props`: `Enigma.Avalonia.Desktop` → `1.2.0`. Bring the set's comment up to
   date: 12.1.1 is the exact set 1.2.0 is built against, as it was for 1.0.0 and 1.1.0.
2. Restore and build. Check that the restored package is 1.2.0 (`obj/project.assets.json`).
3. Test: the app's merged theme carries the new message brush key in both theme variants, which
   proves the 1.2.0 dictionary is the one merged and that nothing in the app shadows it.
4. Whole suite.

## Acceptance criteria

- The App project restores and builds against Enigma.Avalonia.Desktop 1.2.0. The Avalonia set stays
  at 12.1.1.
- `EnigmaInfoBarMessageForegroundBrush` resolves from the application's resources in Dark and in
  Light.
- Build clean with zero warnings; the whole suite green.

## Out of scope

- Moving the Avalonia set (12.1.3 is out). It moves only with what Enigma.Avalonia.Desktop is built
  against.
- Restyling the info bars in the app. The new colours are the library's.

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| Move Avalonia to 12.1.3 with it | No, held at 12.1.1 | 1.2.0 is built against 12.1.1, and the set moves as a whole, as the 5.0.0 notes record | Bumping the set in the same dev |
| How to prove the new colours arrive | A test that the 1.2.0-only key resolves in both variants | The pastel tints are the library's to test. What the app owns is that it merges that dictionary and shadows nothing | A pixel snapshot of the bar (brittle, and it tests the library) |
| Type | FEATURE | A dependency update that brings a visible improvement | BUG (nothing in the app is broken) |
