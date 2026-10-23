# FEATURE-687C — Release 6.8.0

**Item:** FEATURE-687C — Release 6.8.0
**Branch:** `feature/feature-687c-release-6-8-0`
**Run:** feature/2026-10-23-release-6-8-0

## Summary

Release **6.8.0** is cut in the repository, with 6.7.0 (FEATURE-3789) as the template and its
minor-release notes as the shape. It carries three items, all `DONE`:

- **BUG-9DE3**: an error nothing caught no longer ends the application. It is logged and shown in a
  *Something went wrong* dialog, and the application carries on. Work started without being awaited
  reaches the same net.
- **BUG-3024**: stored tokens that can no longer be read can be reset, from connecting, the Profiles
  page's band, a browse or the error dialog. Each integration's line shows its token's state and
  offers *Replace token*. The repository window says when git falls back to its own sign-in.
  Connecting an account already listed replaces its token.
- **BUG-E74A**: two stale Desktop tests. Tests only, so the user-facing notes do not describe it.

**Edits:**

- `Directory.Build.props`: `<Version>6.8.0</Version>`.
- `RELEASENOTES.md`: a dated `## 6.8.0 — 2026-10-23` section on top:
  - a summary;
  - *Errors no longer close the application*;
  - *Tokens that can no longer be read*;
  - *Git's own sign-in*;
  - *Connecting an account again*;
  - *Upgrading from 6.7*;
  - *Dependencies*;
  - *Version*.
- `README.md`: the callout names 6.8 and sums up 6.7 after it. 6.6's summary goes; the pointers for
  5.9, 5.1.0 and 4.x stay.
- `SECURITY.md`: 6.8.x is supported; 6.7.x is not.
- `docs/RELEASE.md`: 6.8.0 is added to the releases without an MSI profile.

## Files / modules touched

**Modified**

- `Directory.Build.props`
- `RELEASENOTES.md`
- `README.md`
- `SECURITY.md`
- `docs/RELEASE.md`
- `docs/roadmap.md`, `docs/plan/FEATURE-687C.md`: statuses.

**Created**

- `docs/done/FEATURE-687C.md`

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| MSI profile | Declined, as for every release since 1.0.0 | The packaging is the Linux installer; Windows has a publish script, not an installer |
| Avalonia 12.1.1 → 12.1.4 | Held back | The set moves only with Enigma.Avalonia.Desktop, still 1.2.0 (built against 12.1.1), which has no update |
| Texts the notes quote | Taken from the code | *Something went wrong*, *Details*, *Copy details* (`UnhandledErrorReporter`, `UnhandledErrorView.axaml`); *Reset stored tokens* and its *Cancel* (`TokenRecovery.ResetLabel`, `ConfirmDestructiveAsync`); *Replace token*, *Browse repositories* (`ProfilesPageView.axaml`); the notice's sentence and *Open Profiles* (`GitSignInNoticeViewModel`, `MainWindow.axaml`); "Token replaced for GitHub" (`ProfilesPageViewModel`); redaction to `***` (`ArgumentRedactor.Placeholder`) |
| "Logged … in the Event Viewer" | Said, for Windows | `Host.CreateApplicationBuilder` adds the EventLog provider on Windows at Warning and above, and the net logs at Error (BUG-9DE3 PHASE01's completion doc) |
| How the notes name the cause | "a Windows password reset" | The reported case: DPAPI cannot open a key sealed before an administrator reset of the password. A password *change* keeps it, so the notes do not say "change" |
| What *Upgrading from 6.7* promises | Every stored format unchanged; going back keeps everything except reset tokens | `AppSettings.CurrentVersion` 7, `RepositoryListStore` 1, `HostAccountService` 2 and `TokenStore` 1 are unchanged since `6.7.0` (`git diff 6.7.0 HEAD -G CurrentVersion` is empty) |
| The key file deleted by hand | Said under *Tokens that can no longer be read* | It is the one behaviour change a user could meet without a password reset (BUG-3024 PHASE01) |
| BUG-E74A in the notes | Left out | It changed two tests and nothing the user sees |
| README beyond the callout | Unchanged | BUG-3024 PHASE02 already corrected the integrations paragraph ("connect the account again … the integration already listed takes it"); nothing else became wrong |
| `SECURITY.md` scope | Unchanged | "stores and uses access tokens" already covers the reset |

## Deviations & follow-ups

- **No deviation from the plan.**
- **Manual check before tagging (Windows), carried from BUG-3024 PHASE04.** It was never performed,
  because DPAPI does not exist on Linux. Its automated equivalents are green.
  1. Back up `%APPDATA%\Enigma.GitClient`, close the application, and overwrite `tokens.key` with
     random bytes.
  2. Connect an account: the reset is offered, and the connect then works.
  3. Fetch in a repository whose integration had a token: the sign-in notice is shown once.
  4. Make a command fail unexpectedly: the dialog appears and the application keeps running.
  5. Restore the backup.
- **Follow-ups the carried items listed, not in this release:**
  - three background reads (`HostLinkService.RefreshAsync`, `ConflictResolutionPageViewModel.RefreshAsync`,
    `HistoryPageViewModel.LoadPageAsync` / `IsWorkingTreeDirtyAsync`) let `GitNotFoundException` reach
    the net when a repository's folder disappears while it is open (BUG-9DE3 PHASE02);
  - the sign-in notice is not shown for a clone started from the start window (BUG-3024 PHASE04);
  - the add-account dialog keeps its self-hosted hint while the instance is locked for *Replace token*
    (BUG-3024 PHASE03).
- Line endings: the touched files are LF, as `.gitattributes` asks; no CRLF churn.

## Dependency check

`dotnet list Enigma.GitClient.slnx package --outdated`, 2026-10-23:

| Package | Requested | Latest | Action |
|---|---|---|---|
| Avalonia, Avalonia.Desktop, Avalonia.Fonts.Inter, Avalonia.Themes.Fluent | 12.1.1 | 12.1.4 | Held (coupled set) |
| Avalonia.Headless, Avalonia.Skia (tests) | 12.1.1 | 12.1.4 | Held (coupled set) |
| Everything else (Core, Core tests) | — | — | Up to date |

## Build / test evidence

- `dotnet build Enigma.GitClient.slnx -c Release`: succeeded, **0 warnings, 0 errors** (AVLN included).
- `dotnet test --solution Enigma.GitClient.slnx -c Release --no-build`: **3430 total, 3427 passed,
  0 failed, 3 skipped** (pre-existing), across Core unit, Core integration and Desktop unit.
- After the README edit, the Core unit suite ran again, because `DisplayNameConsistencyTests` reads
  the README: 1334 total, 1331 passed, 0 failed, 3 skipped. No compiled project references
  `README.md`, `RELEASENOTES.md` or `SECURITY.md`.
- The built assemblies carry the version: `Enigma.GitClient.Core.dll` and `Enigma.GitClient.Desktop.dll`
  read `6.8.0+a88ea06…`. The suffix is the commit the build was cut from; the tagged build will show
  the release commit.
- No `git tag`, `git push`, `dotnet pack` or `dotnet nuget push` was run.

## Runbook (printed, not run)

```bash
# 0. On Windows, before tagging: the manual check above

# 1. Integrate: the run branch, which carries the crash/token run, into develop, then develop into
#    main
git switch develop
git merge --no-ff feature/2026-10-23-release-6-8-0
git switch main
git merge --no-ff develop

# 2. Tag (bare X.Y.Z, as 6.7.0)
git tag 6.8.0

# 3. Push
git push origin develop main
git push origin 6.8.0

# 4. Publish (per platform; always pass -r)
dotnet publish src/Enigma.GitClient.Desktop -c Release -r linux-x64 --self-contained true -o ./artifacts
# On Windows:
powershell -ExecutionPolicy Bypass -File packaging\windows\publish.ps1
```

## Documentation sweep

This dev is the documentation update: the README callout, `RELEASENOTES.md`, `SECURITY.md` and
`docs/RELEASE.md` are edited above. There is no `CLAUDE.md` in the repository.
