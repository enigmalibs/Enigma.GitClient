# FEATURE-D9C1-PHASE02 — The button on the home page

**Item:** FEATURE-D9C1 — The profile's identity in every repository
**Branch:** `feature/feature-d9c1-phase02-home-identity-button`
**Run:** feature/2026-10-27-repositories-local-identity

## Summary

The start window's Repositories page has a button just after the profile picker (`UserCheck` icon,
tooltip "Use this profile's name and email in every repository on its list") bound to
`RepositoriesPageViewModel.UseProfileIdentityEverywhereCommand`:

- enabled while the page is not busy, the selected profile has a name and email and its list is not
  empty; re-evaluated when the selection, the list or the busy state changes;
- reads the profile and its whole stored list as they are now (the filter is ignored), asks once —
  "Use {label} in every repository", *Use it* / *Cancel*, *Cancel* the default — and, on *Use it*, hands
  every path to `IBulkLocalIdentityService` (PHASE01);
- one info bar from the outcomes: Success "Using {label} in every repository" ("{n} repositories now
  commit as {identity}." + "{k} already did."), Info "Nothing to change" when every repository already
  had it, Warning "Some repositories were left as they were" naming those not found and those that
  could not be changed (with git's reason for the first); git not found → an Error bar;
- logs one summary of counts and, per failure, the path and exception kind — never the name or email;
- git's global identity is not touched.

## Files/modules touched

**Modified — Desktop**

- `src/Enigma.GitClient.Desktop/ViewModels/Pages/RepositoriesPageViewModel.cs`
- `src/Enigma.GitClient.Desktop/Views/Pages/RepositoriesPageView.axaml`

**Modified — tests**

- `tests/Enigma.GitClient.Desktop.UnitTests/RepositoriesPageTests.cs`
- `tests/Enigma.GitClient.Desktop.UnitTests/Infrastructure/FakeGitIdentityService.cs`

**Modified — docs**

- `README.md`

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| What is inside the busy state | The whole handler, question included | No second run while the first asks; the create flow asks its identity question the same way |
| Order of the names in the warning | The list's order | The reader finds them where the list shows them |
| A failure for one repository in the Desktop tests | `FakeGitIdentityService.LocalWriteFailures` (by work tree) | `Failure` fails every call, which cannot show the others still being written |
| Repositories in the Desktop tests | Real ones (`IRepositoryService.InitAsync`), the identity service faked | The bulk service discovers each path with the real locator; the fake keeps the developer's configuration safe |
| The pre-existing `TokenStoreTests` failure | Recorded, not fixed | As in PHASE01: identical at the starting commit, unrelated |

## Deviations & follow-ups

- None from the plan.
- Run `dotnet test --project tests/Enigma.GitClient.Desktop.UnitTests` on Linux before tagging: the six
  new tests are compiled, not run here.
- **Pre-existing failure (unchanged):** `Core.UnitTests.Security.TokenStoreTests.AfterTheResetOfAnUnreadableStore_SecretsAreStoredAndReadAgain`
  (see PHASE01).
- Recommendation only: nothing in this diff showed CRLF churn.

## Documentation sweep

- `README.md`, *Features*, the start-window bullet: the button beside the profile and what it does.
- `README.md`, *Where your things are kept*: giving a profile's identity to every repository on its
  list is one of the times git's configuration is written.

## Build/test evidence

- `dotnet build Enigma.GitClient.slnx`: 0 warnings, 0 errors.
- `Core.UnitTests`: 1392 total, 1389 passed, 1 failed (the pre-existing token-store test), 2 skipped.
- `Core.IntegrationTests`: 414 total, 412 passed, 0 failed, 2 skipped (Windows file modes).
- `Desktop.UnitTests`: compiled with the solution, **not run** on this Windows machine (house rule);
  6 new tests in `RepositoriesPageTests` — the button after the picker, its enabled rule, *Use it*,
  *Cancel*, nothing to change, a missing repository and a failed write.
- Fix budget: 0 cycles used.
