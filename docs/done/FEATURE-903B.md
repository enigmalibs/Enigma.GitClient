# FEATURE-903B — The home picker switches the identity

**Item:** FEATURE-903B — The home picker switches the identity
**Branch:** `feature/feature-903b-picker-switches-identity`
**Run:** vibe/2026-10-05-profiles-graph-fetch-release

## Summary

Choosing a profile in the start window's picker now does what the Profiles page's **Use** does, as well
as showing that profile's repositories: git's global identity becomes the profile's name and email, and
an info bar says "Using {label} — New commits are made as {identity}.".

- A profile without a name and email only switches the list: writing its empty identity would unset
  git's own.
- A pick of the profile git already commits as writes nothing and says nothing.
- The page putting its remembered profile back when it is shown never writes.
- Writes are chained one after another; a pick superseded before its turn writes nothing.
- A failed write is an error info bar ("Could not switch the git identity", git's reason); the list stays
  switched; the log names the exception's kind only.
- The Profiles page's **Use** also selects the profile used, so the picker shows the profile git commits
  as. A failed Use leaves the selection alone.

## Files / modules touched

**Modified**

- `src/Enigma.GitClient.Desktop/ViewModels/Pages/RepositoriesPageViewModel.cs`: `IGitIdentityService`
  injected; `SelectedProfile` starts `UseIdentityAfterAsync`; `PendingIdentityWrite` (internal, for the
  tests); remarks.
- `src/Enigma.GitClient.Desktop/ViewModels/Pages/ProfilesPageViewModel.cs`: `IProfileSelection`
  injected; Use selects the profile once git has its identity.
- `src/Enigma.GitClient.Desktop/Services/ProfileSelection.cs`: remarks.
- `src/Enigma.GitClient.Desktop/Views/Pages/RepositoriesPageView.axaml`: the picker's comment and
  tooltip (the automation name is unchanged).
- `tests/Enigma.GitClient.Desktop.UnitTests/RepositoriesPageTests.cs`: five tests.
- `tests/Enigma.GitClient.Desktop.UnitTests/ProfilesPageTests.cs`: two tests.
- `README.md`: the sentence on when git's configuration is written.
- `docs/roadmap.md`, `docs/plan/FEATURE-903B.md`: statuses.

**Created**

- `docs/done/FEATURE-903B.md`

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| How writes are serialised | A chained `Task` field (each pick awaits the previous one) rather than a `SemaphoreSlim` | Everything runs on the UI thread; nothing to dispose on a singleton that is not `IDisposable` |
| Reading git's identity first | `GetGlobalAsync` before writing, to skip a pick of the current profile | A pick that changes nothing must not rewrite `~/.gitconfig` |
| The picker's accessible name | Unchanged ("The profile whose repositories are listed"); the tooltip says what a pick does now | The name is still true and tests find the picker by it |
| Testing the supersede guard | Not tested directly | The fake identity service completes synchronously, so two picks never overlap in a test; the guard is a one-line selection check |

## Deviations & follow-ups

- **Test gate on this Windows host (environment, not this dev).** The suite cannot run green on Windows
  (BUG-6EAA, abandoned): `AtomicFileTests.AReaderRacingTheWriter_AlwaysReadsAWholeDocument` loops
  forever, the Desktop suite never finishes, and every Desktop test that builds a repository with
  objects fails in `TestServices.Dispose` (`Directory.Delete` refuses git's read-only objects). Mid-run
  the user asked to skip the Desktop unit tests for this session. This run's gate is therefore: a
  zero-warning build of everything (the Desktop tests included); the Core unit and integration suites in
  full, that one Core test excluded; and only the Desktop classes covering the dev, under a timeout,
  with the teardown refusals counted apart. A teardown refusal can hide an assertion failure in the same
  test (the `using` disposal throws last), so it is not proof of a pass.
- Follow-up: fixing BUG-6EAA would give Windows a real green suite.
- Line endings: no CRLF churn; the touched files are LF.

## Documentation sweep

- `README.md`, *Configuration*: git's configuration is now also written when a profile is picked in the
  start window — the sentence said "only … on the Profiles page".
- No `CLAUDE.md` or `AGENTS.md`. The feature list's picker bullet is still true and was left alone.

## Build/test evidence

See the run's gate above.

- `dotnet build Enigma.GitClient.slnx -c Release`: 0 warnings, 0 errors.
- `Enigma.GitClient.Core.UnitTests`: 1124 total, 1123 passed, 0 failed, 1 skipped (Windows DPAPI).
- `Enigma.GitClient.Core.IntegrationTests`: 344 total, 342 passed, 0 failed, 2 skipped (file modes on
  Windows).
- Desktop, targeted (`RepositoriesPageTests`, `ProfilesPageTests`, `ProfileSelectionTests`,
  `CompositionRootTests`, `ApplicationBootstrapTests`, `ProfileIntegrationsTests`): 156 total, 153
  passed, 0 real failures; 3 teardown refusals in pre-existing tests that build a repository
  (`CloningIntoTheWorkspace_OpensTheCloneAndRemembersIt`,
  `Creating_CommitsAReadmeAsTheFirstCommitAndOpensTheRepository`,
  `ACloneThatWorked_IsWhereTheNextCloneIsSuggested`) — the same three fail the same way before this
  dev's change.
- The seven new tests pass: `PickingAProfile_MakesItsNameAndEmailGitsGlobalIdentity`,
  `PickingAProfileWithoutANameAndEmail_OnlySwitchesTheList`,
  `PickingTheProfileGitAlreadyCommitsAs_WritesNothing`, `AFailedSwitch_IsReportedAndTheListStaysSwitched`,
  `ShowingThePage_NeverWritesTheIdentity`, `UsingAProfile_ShowsItsListInTheStartWindow`,
  `AFailedUse_LeavesThePickedListAlone`.
- Fix budget: 0 cycles used.
