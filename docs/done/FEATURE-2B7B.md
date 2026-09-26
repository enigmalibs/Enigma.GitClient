# FEATURE-2B7B — Release 1.0.0

**Item:** FEATURE-2B7B — Release 1.0.0
**Branch:** `feature/feature-2b7b-release-1-0-0`
**Run:** feature/2026-09-26-release-1-0-0

## Summary

Version **1.0.0** is cut, in the repository — the tag, the merges and the pushes are the user's, and
are printed below.

- `Directory.Build.props` states `<Version>1.0.0</Version>` once for the whole solution: Core and App
  ship as one application. The built assemblies carry `1.0.0.0` and the informational version
  `1.0.0+<commit>`, which the splash screen, the About dialog (with its build line), the Settings page
  and the hosting user agent read.
- `RELEASENOTES.md` — the section is dated, `## 1.0.0 — 2026-09-26`, in the repository's own themed
  shape, and now covers everything this run shipped: the tool dialogs on the window's background
  (*Working with the repository*), a new *The application* sub-section (start window, splash screen,
  About dialog), *Installing* (the Linux installer) and *Compatibility* (Linux and Windows, git 2.20 or
  newer, .NET 10). The hidden branches and the removed History controls were written by FEATURE-70C1
  and FEATURE-92A3's sweeps.
- `README.md` — the *what's new in 1.0* callout under the intro.
- `docs/RELEASE.md` — the `dotnet-release` runbook adapted to an app: pre-flight in Release, merge into
  `develop` then `main`, tag `X.Y.Z`, push, publish, install, verify; no pack or NuGet push, and why;
  why there is no MSI profile.
- `SECURITY.md` — GitHub private vulnerability reporting, 1.0.x supported, and a scope naming what is
  sensitive here: the git commands, the tokens, the providers, the configuration files, the installer.

## Files / modules touched

**Created**

- `docs/RELEASE.md`
- `SECURITY.md`

**Modified**

- `Directory.Build.props` — `<Version>1.0.0</Version>`
- `RELEASENOTES.md` — dated; *The application*, *Installing*, *Compatibility*; the tool-dialogs bullet
- `README.md` — the what's-new callout

## Runtime licence audit

What a published build redistributes — every runtime package in the App's `project.assets.json` for
`net10.0`, read from its own nuspec or licence file — and, for a self-contained build, the .NET runtime:

| Component | Licence | Ships |
|---|---|---|
| .NET runtime (self-contained builds) | MIT | Linux installer default |
| Avalonia 12.1.1 and its platform packages (Desktop, X11, Win32, Native, FreeDesktop, FreeDesktop.AtSpi, Skia, HarfBuzz, Themes.Fluent, Remote.Protocol, Fonts.Inter) | MIT | all builds |
| `Avalonia.Angle.Windows.Natives` (ANGLE) | BSD-3-Clause (licence file) | Windows builds |
| SkiaSharp 3.119.4 and its natives | MIT | all builds |
| HarfBuzzSharp 8.3.1.3 and its natives | MIT | all builds |
| Inter (the font, inside `Avalonia.Fonts.Inter`) | SIL OFL 1.1 | all builds |
| CommunityToolkit.Mvvm 8.4.2 | MIT | all builds |
| Microsoft.Extensions.* 10.0.12 (Hosting, Logging, Configuration, DI, Options, Http, …) | MIT | all builds |
| System.Security.Cryptography.ProtectedData, System.Diagnostics.EventLog 10.0.12 | MIT | all builds |
| Enigma.Avalonia.Desktop, Enigma.Core, Enigma.Icons, Enigma.Icons.Avalonia, Enigma.Icons.Phosphor 1.0.0 | MIT (`LICENSE.md`) | all builds |
| Phosphor Icons artwork (inside Enigma.Icons.Phosphor) | MIT (its third-party notices) | all builds |
| BouncyCastle.Cryptography 2.6.2 (through Enigma.Core) | MIT | all builds |
| MicroCom.Runtime, Tmds.DBus.Protocol, Microsoft.IO.RecyclableMemoryStream | MIT | all builds |
| AvaloniaUI.DiagnosticsSupport 2.2.3 | — | **not shipped**: excluded from non-Debug builds |

Every licence permits redistribution in binary form. The About dialog's *BUILT WITH* list is the
reader-facing form of this table. The application's own licence is MIT (`LICENSE.md`).

## Deviations & follow-ups

- **Follow-ups, none blocking the release:**
  - A Windows installer (the MSI profile `dotnet-release` offers for apps), when Windows packaging is
    wanted — first profile's `upgradeCode` then reused for ever.
  - The process does not end on `SIGTERM` (found in FEATURE-75F4-PHASE01): the generic host's console
    lifetime catches the signal and cancels the default termination while Avalonia's loop carries on.
    A logout or `kill` therefore leaves the process running. Worth a bug item.
  - Enable **Private vulnerability reporting** in the GitHub repository's *Settings → Security*, which
    `SECURITY.md` relies on.
- No dependency refresh, as planned: the Avalonia set is the one Enigma.Avalonia.Desktop 1.0.0 pins.
- Line endings: nothing to report — `.gitattributes` already normalises every text file to LF.

## Build/test evidence

- `dotnet build Enigma.GitClient.slnx -c Release` — 0 warnings, 0 errors; `dotnet test --solution
  Enigma.GitClient.slnx -c Release` — 2205 passed, 0 failed.
- `dotnet build Enigma.GitClient.slnx` — 0 warnings, 0 errors; `dotnet test --solution
  Enigma.GitClient.slnx` — 2205 passed, 0 failed.
- `strings src/Enigma.GitClient.App/bin/Release/net10.0/Enigma.GitClient.Core.dll` — `1.0.0.0` and
  `1.0.0+94e5736be5eca40bcd39db854bb1a18853b71a3c` (the run branch's tip at the time of the build), the
  recipe `docs/RELEASE.md` gives.
- `git merge-base --is-ancestor main develop` — true: the runbook's `main` merge is a plain merge.
- Nothing in this dev ran `git tag`, `git push`, `dotnet pack` or `dotnet nuget push`.

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| The release-notes heading | `## 1.0.0 — 2026-09-26` | The repository's `version — state` style, the state now being the date |
| Runbook branch flow | release branch → `develop` → `main`, then tag `main` | How this repository integrates: every earlier run was merged into `develop`, and `main` is the default branch |
| The version recipe | `strings … | grep -o 'X.Y.Z+[0-9a-f]*'` | The informational version sits after a length byte, so an anchored grep misses it; this prints what the About dialog reads |
