# FEATURE-75F4 — A splash screen and an About box

**Status:** IN PROGRESS
**Type:** FEATURE
**Branch:** one per phase, see below
**Run:** feature/2026-09-26-release-1-0-0

## Objective

Give the client the two product surfaces Enigma.MarkdownEditor has, at the same size and in the same
style:

1. **A splash screen** while the application starts: the icon, the product's name and its version,
   on a borderless 420 × 260 window centred on the screen, held for at least a second.
2. **An About dialog**: the icon, the name, the version, the build it was cut from, the copyright and
   what the application is built with, in a 380-wide content dialog titled "About".

## Context & constraints

- **The reference implementation** is `Enigma.MarkdownEditor.Desktop`: `Views/SplashWindow.axaml`
  (420 × 260, `WindowDecorations="None"`, `ShowInTaskbar="False"`, `Topmost="True"`,
  `WindowStartupLocation="CenterScreen"`, `EnigmaSurfaceBrush` background, a 1 px
  `EnigmaBorderSubtleBrush` border, a 96-pixel icon from `Assets/app-256.png`, the name at 18
  SemiBold, "Version X" in the secondary foreground), `Views/SplashTiming.cs` (a 1 s floor,
  `RemainingDelay` never negative), `Views/AboutView.axaml` (width 380, the same icon, name, version,
  a selectable build line hidden when there is none, the copyright, a "BUILT WITH" box of credits),
  `ViewModels/AboutViewModel.cs`, `CreditEntry`, and `Services/AboutDialogService.cs` (title "About",
  the Phosphor `Info` glyph, one "Close" button).
- **Start-up here** builds and starts the host in `App.OnFrameworkInitializationCompleted`, loads the
  settings synchronously, applies the theme, then fires `IAppWindows.StartAsync(path)`, which opens
  the repository given on the command line (if any) and shows the start or repository window.
  `ShutdownMode` is `OnLastWindowClose`, so the splash must never be the last window closed while
  the start is still under way.
- The product's identity is `Core/Diagnostics/ProductInformation`: `Name = "Enigma.GitClient"` (also
  the HTTP user agent, through `HostHttp`, which cannot carry a space) and `GetVersion()`, which strips
  the `+<sha>` the SDK appends to the informational version.
- `Assets/app.ico` holds six PNG-encoded frames; the 256-pixel one is a byte copy away from
  `app-256.png`. `Assets/**` is already an `AvaloniaResource`.
- The repository window's strip carries toolbar buttons (new window, close, refresh, theme); the
  start window has only its rail. Both windows have the Settings page, whose *About* card shows the
  version, git, Avalonia, the licence, the preferences file and the scope statement.
- `HostDialog` is sized 380–720 wide, 600 high at most — room for a 380-wide About view.

## Decisions

| Decision | Rationale |
|---|---|
| `ProductInformation.DisplayName = "Enigma git client"` for the splash and the About box | The human name the launcher entry uses (FEATURE-B4C0); `Name` stays as it is because it is part of the user agent |
| The splash comes up after the settings are loaded, painted in the user's theme | A splash that paints dark and flips to light is the flicker it exists to hide; building the host and reading one JSON file costs a few milliseconds |
| The first window is shown when the splash's floor has passed, and the splash closes right after it | The MarkdownEditor hand-over: never a moment without a window, never a splash left over a working window |
| About opens from an Info button in the repository window's toolbar and from a button on the Settings page's About card | The toolbar mirrors MarkdownEditor; the Settings page is the start window's only way to reach it |

## PHASE01 — The splash screen

**Status:** DONE — see `docs/done/FEATURE-75F4-PHASE01.md`
**Branch:** `feature/feature-75f4-phase01-splash-screen`

### Steps

1. `Core/Diagnostics/ProductInformation.cs` — `DisplayName`.
2. `App/Assets/app-256.png` — the 256-pixel frame of `app.ico`, copied byte for byte.
3. `App/Views/SplashTiming.cs` and `App/Views/SplashWindow.axaml(.cs)` — as MarkdownEditor's, reading
   the name and the version off `ProductInformation` with `x:Static`.
4. `App/Services/AppWindows.cs` — `StartAsync` takes an optional splash to hand over: after the
   start is resolved (the command-line repository opened or refused), it waits out the splash's
   remaining floor, shows the first window, then closes the splash — and closes it on the way out if
   anything throws, so a failed start still ends.
5. `App/App.axaml.cs` — show the splash once the theme is applied, then post the start at
   `DispatcherPriority.Background` so the splash paints first.
6. Tests: `SplashTiming.RemainingDelay` (before, at, after the floor; never negative); the splash
   window's size, chrome and text in a headless render; `AppWindows.StartAsync` with a splash closes
   it only after the first window is on screen, for a start window, a repository window and a path
   that is no repository.

### Acceptance criteria

- Starting the application shows the splash — icon, "Enigma git client", "Version …" — centred,
  borderless, for at least a second, then the start or repository window replaces it.
- The splash is painted in the stored theme.
- Build clean with zero warnings; the whole suite green.

## PHASE02 — The About dialog

**Status:** TODO
**Branch:** `feature/feature-75f4-phase02-about-dialog`

### Steps

1. `Core/Diagnostics/ProductVersion.cs` — a `readonly record struct ProductVersion(string Version,
   string? BuildSha)` with a pure `From(informational, fallback)`, as MarkdownEditor's; 
   `ProductInformation` gains `BuildSha` and `Copyright` and keeps `GetVersion()` on top of it.
2. `App/ViewModels/Dialogs/AboutViewModel.cs` and `CreditEntry` — the name, version, build line,
   copyright and credits, with the constructor seam MarkdownEditor has for tests.
3. The credits, each read from the licence the package itself declares: Avalonia, CommunityToolkit.Mvvm,
   Microsoft.Extensions (Hosting), the Enigma libraries, BouncyCastle (through Enigma.Core), Phosphor
   Icons, Inter.
4. `App/Views/Dialogs/AboutView.axaml(.cs)` — MarkdownEditor's view, width 380.
5. `App/Services/AboutDialogService.cs` — `IAboutDialogService.ShowAsync()` on `IContentDialogService`:
   title "About", the `Info` glyph, a "Close" button; registered in `AddGitClientApp`.
6. Entry points: `MainWindowViewModel.OpenAboutCommand` behind an Info toolbar button in the repository
   strip; `SettingsPageViewModel.OpenAboutCommand` behind an "About Enigma git client" button on the
   About card.
7. Tests: `ProductVersion.From` (plain, with a sha, a short sha, blank, a leading `+`, a fallback);
   the ViewModel's build and copyright lines appear only when there is something to show; the service
   shows the view with the right title, icon and single button; both commands open it; a headless
   render of the view shows the name, the version and every credit.

### Acceptance criteria

- The About dialog opens from the repository window's toolbar and from the Settings page in either
  window, shows the icon, "Enigma git client", the version, the build revision when there is one, the
  copyright and the credits, and closes with its one button.
- Build clean with zero warnings; the whole suite green.

## Out of scope

- A progress line on the splash.
- Renaming the product in the window titles or the Settings page.
- A licence viewer, links to the packages, or a "check for updates" button.

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| One item or two | One item, one phase each | Both are the product presenting itself, mirror the same reference, and share the product identity | Two separate items |
| The name on both surfaces | "Enigma git client" (`DisplayName`) | The launcher's name the draft fixes; the two surfaces that present the product by name should agree with it | "Enigma.GitClient" (a technical name); renaming `Name` (breaks the user agent, which cannot contain spaces) |
| When the splash appears | Once the host is started and the settings read | Painted in the right theme from its first frame, as MarkdownEditor's is | Before the host (paints in the default theme, then flips) |
| How the splash is handed over | `AppWindows.StartAsync` takes it and closes it right after showing the first window, after its floor | The window service is what knows when the first window is on screen; a closing splash is then never the last window | Closing it from `App` after `StartAsync` returns (it would wait for a start-up warning to be dismissed); a fixed timer (either a flash or a wait) |
| The icon | `Assets/app-256.png`, extracted from `app.ico` | Which frame Skia picks from an `.ico` is unspecified; the 256 frame is the only one worth showing at 96 px | The `.ico` itself |
| Where About opens from | The repository toolbar and the Settings page's About card | MarkdownEditor's toolbar button, plus the start window's only route | A rail item (the rails navigate to pages, not dialogs); the Settings page only |
| The Settings About card | Kept, with the new button | It carries what the dialog does not (the git found, the preferences file, the scope statement) | Replacing it with the dialog |
| The build line | The short commit from `+<sha>`, selectable, hidden when absent | The same as MarkdownEditor; the SDK stamps the revision in a git checkout | Always showing it (empty on a source drop) |
