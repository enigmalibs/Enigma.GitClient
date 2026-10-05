# FEATURE-903B — The home picker switches the identity

**Status:** DONE — see `docs/done/FEATURE-903B.md`
**Type:** FEATURE
**Branch:** `feature/feature-903b-picker-switches-identity`
**Run:** vibe/2026-10-05-profiles-graph-fetch-release

## Objective

In the start window, choosing a profile in the Repositories page's picker does what the Profiles
page's **Use** does as well as showing that profile's repositories: git's global identity becomes the
profile's name and email.

## Context & constraints

- `ViewModels/Pages/RepositoriesPageViewModel.cs`: `SelectedProfile`'s setter calls
  `IProfileSelection.Select(id)` and `ShowListOfAsync(id)`, and is ignored while `ShowProfiles` puts
  back the profile it has just read (`_showingProfiles`). Its remarks say it never changes the identity:
  that was FEATURE-711F's decision ("a combobox that rewrites `~/.gitconfig` is a hidden side effect"),
  which the user now reverses.
- `ViewModels/Pages/ProfilesPageViewModel.cs`: `OnUseProfileAsync` refuses a profile without an
  identity (`HasIdentity`), then `IGitIdentityService.SetGlobalAsync` and an info bar
  "Using {label} / New commits are made as {identity}.".
- `Services/ProfileSelection.cs`: its remarks say picking a list never writes git's configuration.
- `IdentityProfile.Matches(GitIdentity)` says whether a profile is git's identity now.
- Names and emails are never logged (house rule in `ProfilesPageViewModel`).
- Tests: `RepositoriesPageTests` (picker), `ProfilesPageTests`, `FakeGitIdentityService` (`Global`,
  `GlobalWrites`, `Failure`).

## Steps

1. `RepositoriesPageViewModel` takes `IGitIdentityService`. When the user picks a profile (not when the
   page puts one back), after selecting it and showing its list:
   - a profile without a name and email: nothing more — writing it would unset git's identity;
   - git's global identity already is the profile's: nothing more;
   - otherwise `SetGlobalAsync(profile.Identity)` and an info bar "Using {label}" / "New commits are made
     as {identity}.", as the Profiles page's Use says it.
   - Writes are serialised, and a pick superseded while it waited writes nothing.
   - A failed write: an error info bar, the list stays switched, the failure logged by kind only.
2. The Profiles page's **Use** also selects the profile used (`IProfileSelection.Select`), so the picker
   shows the profile git commits as.
3. The remarks of `SelectedProfile`, `IProfileSelection` and `ProfileSelection` say what they do now.
4. Tests: picking a profile writes its identity once and says so; picking a profile without an identity,
   or the one already current, writes nothing; a failed write reports and keeps the list; putting the
   profile back on showing the page writes nothing; Use on the Profiles page selects the profile.

## Acceptance criteria

- Choosing a profile with a name and email in the start window's picker sets git's global identity to it
  and shows its repositories.
- A profile without an identity only switches the list; git's identity is untouched.
- The page's own reload never writes git's configuration.
- Build clean, whole suite green, the new tests among them.

## Out of scope

- The repository's own identity (`--local`); the repository window has no picker.
- Changing what "current profile" means on the Profiles page.

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| A profile without a name and email | Only the list switches | Writing it would unset git's identity; the Profiles page disables Use for it too | Unsetting the identity; refusing the pick |
| The profile already current | No write, no notification | A pick that changes nothing must not rewrite `~/.gitconfig` | Writing anyway |
| The Profiles page's Use | Also selects that profile's list | Otherwise the picker shows one profile while commits use another — the confusion this item removes | Leaving Use alone |
| Notification | The Profiles page's "Using {label}" info bar | One wording for one action | Silent switch |
| Two quick picks | Serialised writes; a superseded pick writes nothing | The last pick wins, never an older one finishing late | Unguarded concurrent writes |
| Failure | Error info bar; list still switched | The list is the user's choice; only the write failed | Rolling the pick back |
