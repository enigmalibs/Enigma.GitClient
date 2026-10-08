# FEATURE-B313 — Window title says Enigma Git Client

**Item:** FEATURE-B313 — Window title says Enigma Git Client
**Branch:** `feature/feature-b313-window-title`
**Run:** feature/2026-10-08-title-watcher-refs-release

## Summary

The application's title now says **Enigma Git Client**:

- the start window: `Enigma Git Client`;
- the repository window: `<repository> — Enigma Git Client`, or `Enigma Git Client` with nothing open.

Both titles read `ProductInformation.DisplayName`, the name the splash screen, the About dialog and
the Linux launcher entry already show. `ProductInformation.Name` (`Enigma.GitClient`) is unchanged:
it is still the HTTP user agent's product token, which cannot carry a space.

## Files / modules touched

**Modified**

- `src/Enigma.GitClient.Desktop/ViewModels/StartWindowViewModel.cs`: `WindowTitle`.
- `src/Enigma.GitClient.Desktop/ViewModels/MainWindowViewModel.cs`: `WindowTitle`.
- `src/Enigma.GitClient.Core/Diagnostics/ProductInformation.cs`: `DisplayName`'s documentation names
  the windows' titles among its surfaces.
- `tests/Enigma.GitClient.Desktop.UnitTests/MainWindowShellTests.cs`: the repository window's two
  titles.
- `tests/Enigma.GitClient.Desktop.UnitTests/AppWindowsTests.cs`: the start window's own `Title`,
  read from the shown window, so the binding is covered too.
- `docs/roadmap.md`, `docs/plan/FEATURE-B313.md`: statuses.

**Created**

- `docs/done/FEATURE-B313.md`.

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| How the start window's title is tested | On the shown `StartWindow`, not only its view model | That also covers the `Title="{Binding WindowTitle}"` binding |

## Deviations & follow-ups

- None from the plan.
- Follow-up, not done: a few sentences still name the product `Enigma.GitClient`:
  - the Settings page's *About* card;
  - "Another instance of Enigma.GitClient could not be started.";
  - the rebase refusal messages.

  They are outside "the app title"; changing them is a wording decision of its own.
- Line endings: the touched files are LF; no CRLF churn.

## Documentation sweep

Nothing to change: no README, `CLAUDE.md` or prose document describes the window title. The release
notes come with FEATURE-AE9F.

## Build/test evidence

- `dotnet build Enigma.GitClient.slnx -c Debug`: 0 warnings, 0 errors.
- `dotnet test --solution Enigma.GitClient.slnx -c Debug`: 2899 total, 2898 passed, 1 skipped,
  0 failed. All three suites ran on Linux, the Desktop suite included.
- Fix budget: 0 cycles.
