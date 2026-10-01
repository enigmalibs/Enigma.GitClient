# FEATURE-85E2 — Enigma.Avalonia.Desktop 1.2.0

**Item:** FEATURE-85E2 — Enigma.Avalonia.Desktop 1.2.0
**Branch:** `feature/feature-85e2-enigma-avalonia-desktop-1-2-0`
**Run:** feature/2026-10-01-polish-release-5-1

## Summary

The solution now builds against **Enigma.Avalonia.Desktop 1.2.0**:

- in the Dark theme, the info bars' four severity fills and their borders are softer pastel tints, on
  the Light theme's hues;
- the message line is painted with the new `EnigmaInfoBarMessageForeground` key, which keeps it
  readable on those fills.

The Light theme is unchanged.

- `Directory.Packages.props`: `Enigma.Avalonia.Desktop` 1.1.0 → 1.2.0. The Avalonia set's comment now
  names 1.2.0 as the version that set is built against.
- **Nothing else moves.** 1.2.0's nuspec pins the same dependencies as 1.1.0: Avalonia and
  Avalonia.Themes.Fluent 12.1.1, CommunityToolkit.Mvvm 8.4.2 and Enigma.Core 1.0.0. The coupled Avalonia
  set stays at 12.1.1.
- **No app change was needed.** The app overrides no `EnigmaInfoBar*` key, and its two hosts (the
  repository window's and the start window's) are plain `InfoBar`s, so the new colours apply in both
  windows as they are.

## Files / modules touched

**Modified**

- `Directory.Packages.props`: the version and the set's comment.
- `tests/Enigma.GitClient.App.UnitTests/InfoBarNotificationTests.cs`: one new test,
  `TheRealBar_PaintsItsMessageWithTheLibrarysMessageBrush_InBothThemes`. A real info bar is shown in
  a headless window, and in Dark and then Light it checks two things:
  - `EnigmaInfoBarMessageForegroundBrush` resolves;
  - the message's `TextBlock` is painted in that brush's colour.
- `docs/roadmap.md`, `docs/plan/FEATURE-85E2.md`: statuses.

**Created**

- `docs/done/FEATURE-85E2.md`

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| What the test pins | The message `TextBlock`'s foreground equals the 1.2.0-only brush, in both variants | The key exists only in 1.2.0: `strings` finds it 0 times in 1.1.0's assembly and twice (`…Color`, `…Brush`) in 1.2.0's. So the test fails if the app ever merges 1.1.0's dictionary, or shadows the key itself. The tints themselves are the library's to test |
| Where the test goes | `InfoBarNotificationTests` | It already shows the real bar on a real host |

## Deviations & follow-ups

- **None from the plan.**
- The Avalonia set (12.1.1 → 12.1.3) is still held. 1.2.0 is built against 12.1.1.
- Line endings: no CRLF churn; the touched files are LF.

## Documentation sweep

Nothing in `README.md`, `docs/RELEASE.md` or `SECURITY.md` names the Enigma.Avalonia.Desktop
version. 5.0.0's *Dependencies* note in `RELEASENOTES.md` is history and stays. The move goes into the
5.1.0 notes with FEATURE-0C53. No edit.

## Build/test evidence

- `dotnet restore`: `obj/project.assets.json` resolves `Enigma.Avalonia.Desktop/1.2.0`.
- `dotnet build Enigma.GitClient.slnx`: 0 warnings, 0 errors.
- `dotnet test --solution Enigma.GitClient.slnx`: **2625 passed**, 0 failed, 0 skipped (1 new).
- Fix budget: 0 cycles used.
