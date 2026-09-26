# FEATURE-75F4-PHASE02 — The About dialog

**Item:** FEATURE-75F4 — A splash screen and an About box
**Branch:** `feature/feature-75f4-phase02-about-dialog`
**Run:** feature/2026-09-26-release-1-0-0

## Summary

An About dialog built like Enigma.MarkdownEditor's: titled "About" with the Phosphor `Info` glyph and
one **Close** button, it shows a 380-wide view with the 96-pixel icon, **Enigma git client**, "Version
1.0.0", a quieter selectable "Build 9a1b2c3" line (absent, not blank, when the build recorded no
revision), the copyright, and a **BUILT WITH** box listing what the application redistributes and
under which licence.

It opens from two places:

- an **Info** button at the end of the repository window's toolbar, as in MarkdownEditor;
- an **About Enigma git client** button at the foot of the Settings page's About card, which is in both
  windows — the start window's only way to it, since it has no toolbar.

`ProductVersion.From` (Core) splits the informational version into the version and a 7-character
revision — pure and tested — and `ProductInformation` now reads it once and exposes `Version`,
`BuildSha` and `Copyright` (from the assembly attribute `Directory.Build.props` stamps); `GetVersion()`
is kept for its callers and returns the same thing.

## Files / modules touched

**Created**

- `Core/Diagnostics/ProductVersion.cs`
- `App/ViewModels/Dialogs/AboutViewModel.cs` (with `CreditEntry`)
- `App/Views/Dialogs/AboutView.axaml`, `AboutView.axaml.cs`
- `App/Services/AboutDialogService.cs` (`IAboutDialogService`)

**Modified**

- `Core/Diagnostics/ProductInformation.cs` — `Version`, `BuildSha`, `Copyright` over `ProductVersion`; the
  fallback version trimmed to three components
- `App/DependencyInjection/ServiceCollectionExtensions.cs` — `IAboutDialogService`
- `App/ViewModels/MainWindowViewModel.cs`, `Views/MainWindow.axaml` — `OpenAboutCommand` and the toolbar
  button
- `App/ViewModels/Pages/SettingsPageViewModel.cs`, `Views/Pages/SettingsPageView.axaml` —
  `OpenAboutCommand` and the About card's button
- `App/Views/MainWindow.axaml`, `Views/StartWindow.axaml` — `HostDialog`'s `DialogMaxHeight` 600 → 640

**Tests**

- `Core.UnitTests/ProductVersionTests.cs` (new) — plain, with a long, short or blank revision, a
  pre-release, the fallbacks, and `Unknown`
- `Core.UnitTests/ProductInformationTests.cs` — `Version` has no revision and equals `GetVersion()`;
  `DisplayName`; the copyright is the one the build stamps
- `App.UnitTests/AboutDialogTests.cs` (new) — the ViewModel over the running build; the build and
  copyright lines appear only with something to show; the credits and their licences; the service
  shows the view with its title, icon and single Close button; the repository toolbar and the Settings
  page both open it; the realised view shows the name, the version, the build, the copyright and every
  credit, and hides the build and copyright lines when there is nothing; in either window the dialog
  shows everything without scrolling; a rendered frame (`snapshots/about.png`)

## Deviations & follow-ups

- **`HostDialog` is now at most 640 high, as MarkdownEditor's is.** At 600 the About content (484 px)
  got a 449-px viewport and scrolled — measured by the new test before the change. Only the maximum
  moved; every confirmation is far smaller and is unaffected.
- **The credits were read from the packages, not recalled**: every runtime package in the App's
  `project.assets.json` for `net10.0`, with the licence its nuspec declares. Rows: Avalonia (its twelve
  packages, MIT), SkiaSharp and HarfBuzzSharp (MIT), ANGLE (`Avalonia.Angle.Windows.Natives`, a BSD
  3-clause licence file, Windows builds only), CommunityToolkit.Mvvm (MIT), Microsoft.Extensions (the
  hosting, logging and configuration packages, MIT), BouncyCastle (through Enigma.Core, MIT), the
  Enigma libraries (MIT, `LICENSE.md` in each), Phosphor Icons (MIT, per Enigma.Icons.Phosphor's
  third-party notices) and Inter (the font, SIL OFL 1.1, carried by the MIT `Avalonia.Fonts.Inter`).
  Folded into their rows rather than listed: MicroCom.Runtime, Tmds.DBus.Protocol,
  Microsoft.IO.RecyclableMemoryStream, System.Diagnostics.EventLog and
  System.Security.Cryptography.ProtectedData (all MIT). `AvaloniaUI.DiagnosticsSupport` is excluded
  from non-Debug builds and does not ship.
- The captured frame was looked at: the same arrangement as MarkdownEditor's About view.

## Build/test evidence

- `dotnet build Enigma.GitClient.slnx` — 0 warnings, 0 errors.
- `dotnet test --solution Enigma.GitClient.slnx` — 2151 passed, 0 failed (28 new).

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| `CreditEntry`'s file | Beside `AboutViewModel`, in the same file | The house habit for a small record used by one ViewModel (`HistoryScopeOption`, `BranchDialogViewModels.cs`) |
| Error handling around `ShowAsync` | None beyond the library's | The only failure is an unregistered host, which both windows rule out and the tests cover |
| The ViewModel's base | `ViewModelBase` | Every ViewModel in the app derives from it |
| The Settings button's label | "About Enigma git client" | Says what opens; the card's own header already says "About" |
