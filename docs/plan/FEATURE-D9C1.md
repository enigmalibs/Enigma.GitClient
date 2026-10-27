# FEATURE-D9C1 — The profile's identity in every repository

**Status:** DONE
**Type:** FEATURE
**Branch:** one per phase, see below
**Run:** feature/2026-10-27-repositories-local-identity

## Objective

On the start window's Repositories page, one button writes the selected profile's name and email into
the own configuration (`git config --local user.name` / `user.email`) of every repository on that
profile's list, so each of them keeps committing as that profile whatever git's global identity later
becomes.

Draft: *"Add a button in the home view where the repositories are listed. This button will set all
local git identity of the repositories to the global identity of the profile."*

## Context & constraints

- `ViewModels/Pages/RepositoriesPageViewModel.cs` — the page: `Profiles`, `SelectedProfile` (picking
  one already makes its identity git's **global** one, FEATURE-903B), `_all` (the profile's whole list,
  in the stored order) and `Repositories` (what the filter keeps), `RunBusyAsync`, `OnBusyChanged`,
  `Report`, `SelectedProfileNowAsync`, `ShowProfiles` / `_showingProfiles`. `OfferGlobalIdentityAsync`
  is the precedent for one repository: a plain question, *Use it* / *Not now*, *Not now* the default,
  `SetLocalAsync`, a warning on failure, nothing logged but the failure's kind.
- `Views/Pages/RepositoriesPageView.axaml` — the header: the Folders icon, "Repositories", the
  `ProfilePicker` combobox in the left group; Open, Clone, Create, a separator, the theme switch and
  About on the right (`Classes="toolbar"` icon buttons with a tooltip and an automation name).
- `Core/Identity/GitIdentityService.cs` — `GetLocalAsync` (only what the repository sets itself),
  `SetLocalAsync` (validates, trims, writes both keys), `GitIdentityRules.Validate`.
  `Core/Repositories/RepositoryLocator.DiscoverAsync` — a missing or malformed path is
  `NotARepository`; a folder **inside** another repository discovers that outer repository.
- `IdentityProfile.HasIdentity`, `.Identity`; `GitIdentity.IsSameAs`, `.Normalised()`, `ToString()`
  (`Name <email>`).
- `ProfilesPageViewModel.Describe(Exception)` — git's own words for a failure, without the command
  line (which would repeat the values).
- Core services: `Core/<Concept>/<Concept>Service.cs`, interface and class in one file, registered as
  singletons in `AddGitClientCore`. House MVVM: no source generators — `field` + `SetProperty`,
  explicit `…Command` properties, private handlers.
- Names and emails are never logged (house rule).
- Tests: Core unit tests under `tests/Enigma.GitClient.Core.UnitTests/Identity/`, integration tests under
  `tests/Enigma.GitClient.Core.IntegrationTests/Identity/` (`GitWorkspace`, `CoreTestHost`, a throwaway
  global configuration). Desktop: `RepositoriesPageTests`, `FakeGitIdentityService` (`LocalOf`,
  `SetLocalDirectly`, `LocalWrites`, `Failure`, `LockFailure`), `services.Dialogs.Script(...)`,
  `services.InfoBar.Shown`. Desktop tests are written and compiled, **not run**, on this Windows
  machine; the Core suites are run.

## Decisions

| Question | Chosen | Why | Rejected |
|---|---|---|---|
| Which identity | The selected profile's own name and email | "The global identity of the profile" is what the profile makes global; the button sits by the picker, on that profile's list | git's current global identity (may have been changed in a terminal since) |
| Which repositories | Every repository on the profile's stored list, whatever the filter shows | The draft says "all"; the question gives the count | Only the rows the filter keeps |
| Profile without a name and email | The button is disabled | Nothing to write; an empty identity would unset what repositories have | Removing their own identities |
| Confirmation | One plain question, *Use it* / *Cancel*, *Cancel* the default | Many configuration files change, and an identity a repository set for itself is replaced | No question; a red, destructive question (no work is thrown away) |
| A repository that already has it | Read first, not rewritten, counted | No rewrite for nothing; two worktrees sharing one configuration count once written, once already | Writing anyway |
| Missing folder, not a repository, bare, or a folder inside another repository | Skipped, named in the result | A listed folder whose `.git` is gone must not write into the repository around it | Writing into whatever git discovers |
| One repository fails | The others go on; the failures are reported together | One locked configuration must not stop the rest | Stopping at the first failure |
| git not found | Stops and is reported | Every repository would fail the same way | Per-repository failure |
| Where the loop lives | A Core service, `IBulkLocalIdentityService` | Its behaviour is tested by suites that run here; the page only asks and reports | The loop in the ViewModel |
| Global identity | Untouched | The picker already sets it | Also writing it |
| The button | An icon-only toolbar button (`UserCheck`) just after the picker, with a tooltip and an automation name | The header has no room for a label at the window's narrowest; it acts on the picked profile | A labelled button among Open, Clone, Create |
| Breakdown | Two phases: the Core service, then the page | The squash, revert and reset precedent | One phase |

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| Identity written (draft: "global identity of the profile") | `IdentityProfile.Identity` of the profile selected when the command runs, read fresh from the store | The profile *is* the client's switch for the global identity; reading it fresh catches an edit made on the Profiles page | `IGitIdentityService.GetGlobalAsync` |
| Scope vs the filter | The whole stored list (`IRepositoryListStore.GetAsync`) | "All repositories"; a filter is a view, not a selection | The filtered rows |
| Question wording | Title "Use {label} in every repository"; "Make {identity} the name and email of the {n} repositories on {label}'s list?" + why + "A repository that has a name and email of its own gets these instead." | Same voice as the clone's offer | Listing every repository in the question (a long list needs a scrolling card) |
| Result notification | Success when nothing was skipped or failed; Info "Nothing to change" when every repository already had it; Warning naming skipped and failed repositories otherwise | One bar says the whole outcome | One bar per repository |
| Parallelism | One repository after the other | Simple; a few git calls each; fast for lists of tens | Parallel writes |
| Cancellation | The service honours a token between repositories; the page passes none | No progress overlay for a seconds-long job | A cancel button |
| Logging | Counts, and each failure's path and exception kind | Paths are already logged by the page; names and emails never | Logging nothing |

## PHASE01 — Bulk local identity in Core

**Branch:** `feature/feature-d9c1-phase01-bulk-local-identity`
**Status:** DONE

### Steps

1. `Core/Identity/BulkLocalIdentityService.cs`:
   - `enum BulkLocalIdentityResult { Written, AlreadySet, NotARepository, Failed }`;
   - `sealed record BulkLocalIdentityOutcome(string Path, BulkLocalIdentityResult Result, Exception? Failure)`;
   - `interface IBulkLocalIdentityService { Task<IReadOnlyList<BulkLocalIdentityOutcome>> SetLocalAsync(
     IReadOnlyList<string> workTreePaths, GitIdentity identity, CancellationToken cancellationToken = default); }`;
   - `BulkLocalIdentityService(IRepositoryLocator locator, IGitIdentityService identity)`.
2. `SetLocalAsync`:
   - validates the identity first (`GitIdentityRules.Validate` on the normalised value) →
     `ArgumentException` before anything is read or written;
   - for each path, in order (`cancellationToken.ThrowIfCancellationRequested()` before each):
     discover it; not found, bare, or found with a work tree that is not the listed path itself
     (full paths, separators trimmed, case-insensitive on Windows) → `NotARepository`;
     `GetLocalAsync` → `IsSameAs` the identity → `AlreadySet`; otherwise `SetLocalAsync` → `Written`;
   - a `GitCommandException`, `IOException` or `UnauthorizedAccessException` for one repository →
     `Failed` with the exception, and the next one is tried; `GitNotFoundException` and
     `OperationCanceledException` propagate;
   - returns one outcome per path, in the order given.
3. Register `IBulkLocalIdentityService` as a singleton in `AddGitClientCore`, beside `IGitIdentityService`.
4. Unit tests (`Core.UnitTests/Identity/BulkLocalIdentityServiceTests.cs`, in-test fakes of the locator
   and the identity service): written / already set (case-insensitive email) / missing / bare / a
   folder inside another repository / a failure in the middle leaves the rest written / an invalid
   identity writes nothing / outcomes keep the input order / git not found propagates.
5. Integration tests (`Core.IntegrationTests/Identity/BulkLocalIdentityServiceTests.cs`, real git):
   three repositories — none, another identity, the same one — a missing path and a plain folder:
   `git config --local` afterwards holds the identity in all three, the outcomes are `Written`,
   `Written`, `AlreadySet`, `NotARepository`, `NotARepository`, and the global configuration is
   unchanged; a folder inside a repository is skipped and the outer repository's configuration is
   unchanged.

### Acceptance criteria

- `IBulkLocalIdentityService` resolves from the container.
- Every listed repository ends with the identity as its own; nothing else is written — not the global
  configuration, not a repository around a listed folder.
- One repository's failure never stops the others; an invalid identity writes nothing.
- Build clean with zero warnings; the Core unit and integration suites green.

## PHASE02 — The button on the home page

**Branch:** `feature/feature-d9c1-phase02-home-identity-button`
**Status:** DONE

### Steps

1. `RepositoriesPageViewModel` takes `IBulkLocalIdentityService`; a new
   `AsyncRelayCommand UseProfileIdentityEverywhereCommand`, enabled while not busy, the selected profile
   has an identity and the list is not empty. Its can-execute is re-evaluated when the selection, the
   list or the busy state changes.
2. The handler, inside `RunBusyAsync`:
   - reads the selected profile now (`SelectedProfileNowAsync`) and its stored list; a profile without
     an identity or an empty list → nothing (the button was stale);
   - asks the question (decisions above); anything but *Use it* → nothing;
   - calls `SetLocalAsync` with the list's paths and the profile's identity;
   - reports one info bar from the outcomes, naming repositories by their listed name:
     - all `Written`/`AlreadySet`, at least one written → Success "Using {label} in every repository",
       "{n} repositories now commit as {identity}." (+ " {k} already did." when k > 0);
     - all `AlreadySet` → Info "Nothing to change", "Every repository on the list already commits as
       {identity}.";
     - any skipped or failed → Warning "Some repositories were left as they were", "{n} now commit as
       {identity}. Not found: a, b. Could not change c, d: {Describe(first failure)}" (the parts that
       apply);
     - `GitNotFoundException` → Error "Could not change the repositories' identity", its description;
   - logs counts and each failure's path and kind; never the name or email.
3. `RepositoriesPageView.axaml`: after `ProfilePicker`, a `Classes="toolbar"` button `Name="UseProfileIdentityEverywhere"`,
   `ei:Icon Kind="UserCheck"`, bound to the command, tooltip "Use this profile's name and email in every
   repository on its list", automation name "Use the profile's identity in every repository",
   `ToolTip.ShowOnDisabled="True"`.
4. The header comment and the remarks of the page say what the button does.
5. Desktop tests in `RepositoriesPageTests` (real repositories made with `RunGit`, the fake identity
   service):
   - the button is in the header, after the picker, bound to the command;
   - disabled for a profile without an identity, and for an empty list;
   - *Use it* writes the profile's identity into every listed repository (the filtered-out ones too),
     the global identity is untouched, one Success bar;
   - *Cancel* writes nothing;
   - every repository already set → Info "Nothing to change", no write;
   - a missing repository and a failed write → one Warning naming them, the others written.
6. README: the start-window feature bullet mentions the button; *Where your things are kept* lists it
   among the times git's configuration is written.

### Acceptance criteria

- With a profile that has a name and email and a non-empty list, the button asks once and, on *Use it*,
  every repository on the list commits as that profile; *Cancel* changes nothing.
- The button is disabled for a profile without an identity, for an empty list, and while the page is busy.
- One info bar reports the outcome, naming every repository skipped or not changed.
- Build clean with zero warnings; the Core suites green; the Desktop tests compiled (not run here).

## Out of scope

- Removing every repository's own identity (the reverse action).
- Choosing which repositories to change, or another profile's identity.
- The repository window's Profiles page; git's global identity.
- A progress overlay or a cancel button.
