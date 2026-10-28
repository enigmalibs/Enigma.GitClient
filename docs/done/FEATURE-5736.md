# FEATURE-5736 — Release 6.12.0

**Item:** FEATURE-5736 — Release 6.12.0
**Branch:** `feature/feature-5736-release-6-12-0`
**Run:** feature/2026-10-28-new-app-icon

## Summary

Release **6.12.0** is cut in the repository. 6.11.0 (FEATURE-7CBA) is the template, and the notes take
the shape of a minor release. It carries one item, `DONE` on this run branch:

- **FEATURE-F7B5**: the new application icon, in the Soft palette, with ten `.ico` frames and eight
  Linux sizes.

The dependency check found that Enigma.Avalonia.Desktop had moved to 1.3.0, built against Avalonia
12.1.4. Under the house rule the coupled set therefore moves together: Avalonia 12.1.1 → 12.1.4, and
the library 1.2.0 → 1.3.0.

**Edits:**

- `Directory.Build.props`: `<Version>6.12.0</Version>`.
- `Directory.Packages.props`: the Avalonia set at 12.1.4 and Enigma.Avalonia.Desktop at 1.3.0. The
  comment above the group now names the set 1.3.0 is built against.
- `RELEASENOTES.md`: a dated `## 6.12.0 — 2026-10-28` section on top:
  - a summary;
  - *A new application icon*;
  - *Upgrading from 6.11*;
  - *Dependencies*;
  - *Version*.
- `README.md`: the callout names 6.12 and sums up 6.11 after it. 6.10's summary goes; the pointers for
  5.9, 5.1.0 and 4.x stay.
- `SECURITY.md`: 6.12.x is supported; 6.11.x is not.
- `docs/RELEASE.md`: 6.12.0 is added to the releases without an MSI profile.

## Files / modules touched

**Modified**

- `Directory.Build.props`, `Directory.Packages.props`
- `RELEASENOTES.md`, `README.md`, `SECURITY.md`, `docs/RELEASE.md`
- `docs/roadmap.md`, `docs/plan/FEATURE-5736.md`: statuses.

**Created**

- `docs/done/FEATURE-5736.md`

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| Avalonia 12.1.1 → 12.1.4 | Moved, with Enigma.Avalonia.Desktop 1.2.0 → 1.3.0 | The library moved: its 1.3.0 nuspec pins Avalonia and Avalonia.Themes.Fluent 12.1.4 and CommunityToolkit.Mvvm 8.4.2 (unchanged here). Its notes say it is backward compatible with 1.2.0: no API, key or value changes |
| Enigma.Core 1.0.0, new and transitive | Accepted, not pinned here | It comes in through the library's own dependency; the app does not use it directly |
| Enigma.Icons.Avalonia | Unchanged at 1.0.0 | Not outdated; the library still depends on 1.0.0 |
| `InfoBarNotificationTests`' comment naming 1.2.0 | Left | It says which version made that brush, which is still true: 1.3.0 changes no value |
| What the notes say about the icon's colours | The old and new hex values, and that the UI's own colours do not change | Taken from the SVGs and `Themes/Graph.axaml` (`#3574F0` stays the HEAD badge and the accent) |
| What *Upgrading from 6.11* promises | Every stored format unchanged; going back keeps everything | `git diff fc4dfc2 HEAD -G CurrentVersion` touches only the plan file; `AppSettings` 7, `RepositoryListStore` 1, `HostAccountService` 2, `TokenStore` 1 unchanged |
| `SECURITY.md` scope | Unchanged beyond the supported line | The installer writes two more PNGs into the same directories; no token, provider or configuration behaviour changed |
| MSI profile | Declined, as for every release since 1.0.0 | The packaging is the Linux installer |

## Deviations & follow-ups

- **A dependency move the plan only anticipated.** Step 7 held the Avalonia set back *unless*
  Enigma.Avalonia.Desktop moved with it. It did, so the set moved.
- **A flaky test, named and shown to predate this run:**
  `AutoRefreshTests.TheNextRefresh_RedrawsAPushMadeBetweenTwoRefreshes`. It is very likely the
  intermittent failure FEATURE-F7B5 could not name.
  - It fails about one run in seven to ten, alone or in the full suite. The assertion fails at
    `AutoRefreshTests.cs:807`: `main` is drawn without its upstream, as before the push.
  - It fails just as often on the run branch before the dependency move (Avalonia 12.1.1): 2 of 15
    runs of its class, built from a `git archive` of the branch.
  - The test waits for the first `RowsReplaced`, which can be an earlier redraw that read the refs
    before the push.
  - Not fixed here: it is a timing race in a test this release does not touch, and the fix (in the
    test or in the history's reload ordering) needs its own look.
  - Follow-up: a BUG item for it.
- Line endings: the touched files are LF, as `.gitattributes` asks; no CRLF churn.

## Dependency check

`dotnet list Enigma.GitClient.slnx package --outdated`, 2026-10-28:

| Package | Requested | Latest | Action |
|---|---|---|---|
| Avalonia, Avalonia.Desktop, Avalonia.Fonts.Inter, Avalonia.Themes.Fluent | 12.1.1 | 12.1.4 | Moved (coupled set) |
| Avalonia.Headless, Avalonia.Skia (tests) | 12.1.1 | 12.1.4 | Moved (coupled set) |
| Enigma.Avalonia.Desktop | 1.2.0 | 1.3.0 | Moved, with the set |
| Everything else | — | — | Up to date |

## Build / test evidence

- `dotnet build Enigma.GitClient.slnx -c Release`, on the moved set: succeeded, **0 warnings,
  0 errors** (AVLN included).
- `dotnet test --solution Enigma.GitClient.slnx -c Release --no-build` (Linux; Core unit, Core
  integration and Desktop):
  - five runs: three with **3588 total, 3585 passed, 0 failed, 3 skipped**, and two with 1 failed,
    the flaky `AutoRefreshTests` test above (named in the second one);
  - `AutoRefreshTests` alone, ten runs: 33 tests, 1 failed once (the same test).
- The built assemblies carry the version: `Enigma.GitClient.Core.dll` and
  `Enigma.GitClient.Desktop.dll` read `6.12.0+0b4bd74…`, the commit the build was cut from; the tagged
  build will show the release commit.
- No `git tag`, `git push`, `dotnet pack` or `dotnet nuget push` was run in this dev.
- Fix budget: 0 cycles used.

## Documentation sweep

The release's own edits are the documentation. Nothing else is stale; the repository has no
`CLAUDE.md` or `AGENTS.md`.
