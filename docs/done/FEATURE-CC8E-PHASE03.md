# FEATURE-CC8E-PHASE03 — No Amend, no Sign off

**Item:** FEATURE-CC8E — Changes: way back, discards, commit box
**Branch:** `feature/feature-cc8e-phase03-no-amend-signoff`
**Run:** bugfix/2026-09-28-changes-commit-details-release

## Summary

The Changes page's commit box no longer has *Amend* or *Sign off*. It holds the message, the subject
counter with its long-lines hint, and a *Commit* button. The code behind the two options is gone at
every layer.

- **View:** the two check boxes are removed, and the button says *Commit*.
- **ViewModel:** `ChangesPageViewModel` loses:
  - `Amend`, `SignOff` and `CommitButtonText`;
  - `OnAmendChangedAsync`, the prefill from the last commit's message;
  - the amend clause of `CanCommit`, which is now a message, something staged and no conflict;
  - the "Commit amended" wording, and the resets of `Amend` on commit and on a repository change.
- **Core:** `CommitRequest.Amend` and `SignOff`, their `--amend` and `--signoff` arguments, the
  amend exemption of the nothing-staged refusal, and `ICommitService.GetLastCommitMessageAsync`, which
  only an amend used.

A commit with nothing staged is still refused, by the page (`CanCommit`) and by the engine.

## Files / modules touched

**Created**

- `docs/done/FEATURE-CC8E-PHASE03.md`

**Modified**

- `src/Enigma.GitClient.App/Views/Pages/ChangesPageView.axaml` — the commit row is the counters and the
  button.
- `src/Enigma.GitClient.App/ViewModels/Pages/ChangesPageViewModel.cs` — as above.
- `src/Enigma.GitClient.Core/Commits/CommitService.cs` — as above.
- `tests/Enigma.GitClient.App.UnitTests/ChangesPageTests.cs`:
  - the three amend and sign-off tests are deleted;
  - `CommitBox_HasNoAmendAndNoSignOff` is new.
- `tests/Enigma.GitClient.Core.IntegrationTests/Status/WorkingDirectoryServiceTests.cs` — the three
  amend, sign-off and last-message tests are deleted.
- `docs/roadmap.md`, `docs/plan/FEATURE-CC8E.md` — statuses; the item is `DONE`.

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| `SyncServiceTests`' own `git commit --amend` | Kept | It is test set-up driving git directly to rewrite a pushed commit, not the application amending anything |
| `CommitRequest.AllowEmpty`, `Author`, `StageEverythingFirst` | Kept | Not part of the request; their Core tests stay |

## Deviations & follow-ups

- None from the plan.
- Upgrade note, for FEATURE-10AA: amending or signing off now needs git itself (`git commit --amend`,
  `git commit --signoff`). This removal is why the release is MAJOR.
- Line endings: no CRLF churn.

## Documentation sweep

Neither the README nor any other prose document mentions amending or signing off. No edit.

## Build/test evidence

- `dotnet build Enigma.GitClient.slnx`: 0 warnings, 0 errors.
- `dotnet test --solution Enigma.GitClient.slnx`: **2472 passed**, 0 failed, 0 skipped (6 removed, 1
  new).
- Fix budget: no fix cycle.
