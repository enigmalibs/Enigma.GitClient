# FEATURE-0DED — Remove the splash screen

## Summary

The application starts with its first window (the start window, or the repository window for a path on
the command line) and no splash in front of it. The splash window, its one-second floor and the hand-over
that closed it are deleted. `App` now calls `IAppWindows.StartAsync(path, screen)` directly instead of
posting it at Background priority behind the splash, as before FEATURE-75F4. `StartAsync` loses its
splash parameter, its floor wait and its `finally`. The startup failure handling (info bar, safety net)
is unchanged.

## Files/modules touched

- Deleted:
  - `src/Enigma.GitClient.Desktop/Views/SplashWindow.axaml`
  - `src/Enigma.GitClient.Desktop/Views/SplashWindow.axaml.cs`
  - `src/Enigma.GitClient.Desktop/Views/SplashTiming.cs`
  - `src/Enigma.GitClient.Desktop/Services/SplashHandOver.cs`
  - `tests/Enigma.GitClient.Desktop.UnitTests/SplashScreenTests.cs`
- Modified:
  - `src/Enigma.GitClient.Desktop/App.axaml.cs`
  - `src/Enigma.GitClient.Desktop/Services/AppWindows.cs`
  - `src/Enigma.GitClient.Desktop/Services/UnhandledErrorReporter.cs` (comments)
  - `src/Enigma.GitClient.Core/Configuration/AppSettings.cs` (comment)
  - `src/Enigma.GitClient.Core/Diagnostics/ProductInformation.cs` (comment)
  - `Directory.Build.props` (comment)
  - `tests/Enigma.GitClient.Desktop.UnitTests/AppWindowsTests.cs`
  - `tests/Enigma.GitClient.Desktop.UnitTests/AppIconTests.cs`
  - `tests/Enigma.GitClient.Desktop.UnitTests/UnhandledErrorReporterTests.cs`
  - `tests/Enigma.GitClient.Desktop.UnitTests/Infrastructure/UiServiceDoubles.cs`
  - `docs/RELEASE.md`

## Deviations & follow-ups

- `ManualTimeProvider` stays: the plan allowed for removing it, but the safety net, the watcher and
  `TestServices` still use it.
- `Assets/app-256.png` stays (the About dialog shows it). `AppIconTests.TheSplashImage_…` is now
  `TheAboutImage_IsTheIconsLargestFrameByteForByte`.
- Follow-up: run the Desktop suite on Linux before tagging. It is not run on this Windows machine.
- Follow-up (out of scope): `TokenStoreTests.AfterTheResetOfAnUnreadableStore_SecretsAreStoredAndReadAgain`
  fails on Windows (see *Build/test evidence*). It needs a Windows-aware test, or a protector that the
  real store can read; that belongs in its own `BUG` item.

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| `using` directives left unused in `App.axaml.cs` | `Avalonia.Threading` and `Enigma.GitClient.Desktop.Views` removed | Only the splash and the dispatcher post used them |
| The reporter test's message text | "before the first window" instead of "under the splash" | The scenario is unchanged; only its name had pointed at the splash |

## Build/test evidence

- `dotnet build Enigma.GitClient.slnx -c Release`: 0 warnings, 0 errors. Desktop tests compiled.
- `dotnet test --project tests/Enigma.GitClient.Core.IntegrationTests -c Release`: 414 total, 412 passed,
  0 failed, 2 skipped (5 min 21 s).
- `dotnet test --project tests/Enigma.GitClient.Core.UnitTests -c Release`: 1,392 total, 1,389 passed,
  2 skipped, **1 failed**: `TokenStoreTests.AfterTheResetOfAnUnreadableStore_SecretsAreStoredAndReadAgain`.
  - It is **pre-existing and Windows-only**, not caused by this dev. This dev's whole diff under
    `src/Enigma.GitClient.Core` is two doc-comment lines, and the Core test projects are untouched.
  - The test resets the store through a fake "refusing" key protector, then reads the new key with
    the real one. On Windows that is DPAPI, which rejects a key it did not protect: `CryptographicException:
    The parameter is incorrect`. On Linux the real protector is a pass-through (mode 0600), so the test
    passes there.
  - It dates from BUG-3024 (2026-10-23), when the releases were tested on Linux.
- Desktop unit tests: compiled, not run (user rule for this Windows machine).
- `grep -ri splash src tests`: only the `App` comment that says there is no splash.

## Documentation sweep

- `docs/RELEASE.md`: the two version checks named the splash screen; they now name the About dialog only.
- README, `SECURITY.md`: no mention of the splash; unchanged.
