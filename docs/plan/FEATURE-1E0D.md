# FEATURE-1E0D — A base directory per profile

**Status:** DONE
**Type:** FEATURE
**Branch:** one per phase, see below
**Run:** feature/2026-10-05-revert-tree-basedir-release

## Objective

A profile can name a **base directory** — where its repositories live. On the home page, with that
profile selected, **Open** starts its folder picker there, and **Clone** (and **Create**) start their
*Where* field — and so their *Browse* picker — there.

## Context & constraints

- `IdentityProfile` (Core) is a positional record kept by `IdentityProfileStore` in
  `identity-profiles.json` (version 1). The store reads before every write and writes atomically;
  `SaveAsync` validates through `IdentityProfileRules.Validate`. `ReadAsync` patches nulls a hand-edited
  file may hold.
- The profile dialog (`IdentityProfileDialogViewModel` / `IdentityProfileDialogView`) is shown by
  `ProfilesPageViewModel.OnAddProfileAsync` / `OnEditProfileAsync`; it builds the profile with
  `ToProfile(existing)`. The profiles page has no folder dialog yet.
- `RepositoriesPageViewModel` (the home page) has a profile picker (`SelectedProfile`) and the three
  buttons: `OnOpenAsync` raises the folder picker with no start location; `OnCloneAsync` opens the clone
  dialog on `CloneParentDirectory()` (the remembered last clone directory while it exists, else home —
  FEATURE-2087); `OnCreateAsync` opens the create dialog on the home folder. The dialogs' *Browse*
  pickers start on their *Where* field.
- `ProfilesPageViewModel.OnBrowseRepositoriesAsync` clones a host repository into
  `_repositories.CloneParentDirectory()`, from an account that belongs to a profile.
- `IFolderDialogService.ShowOpenFolderDialogAsync(title, allowMultiple, suggestedStartLocation)` takes a
  start location.

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| Where it is kept | `IdentityProfile.BaseDirectory` (string, empty = none), in `identity-profiles.json`, no schema bump | It belongs to the profile, and goes when the profile goes; a missing key reads empty; older builds ignore an unknown key | A map in `AppSettings` keyed by profile id |
| Where it is edited | The Add/Edit profile dialog: an optional *Base directory* field with *Browse* | Where everything else about a profile is set | A Settings card; a field on the home page |
| Validation | Empty, or an absolute path on one line; it need not exist when saved | A drive may be unplugged when the profile is edited; a relative path has no meaning for a picker | Requiring it to exist |
| Which buttons | Open, Clone and Create | The draft names Open and Clone; Create is the third button of the same row, and a profile's base directory is where its repositories go | Open and Clone only |
| Precedence with the remembered clone directory | The selected profile's base directory, while it exists; else the remembered last clone directory; else the home folder | A directory the user set on purpose for this profile is more specific than a memory shared by every profile | The remembered directory first |
| A base directory that no longer exists | Treated as unset | Never start a picker on a path that is not there | Offering it anyway |
| The host-browser clone | The base directory of the profile that owns the account | That clone belongs to that profile | The home page's selected profile |
| Shown on the profile's row | No | Not asked; the dialog shows it | A third line on the row |
| Breakdown | Two phases: the profile keeps it; the buttons use it | Each is one reviewable commit | One dev |

## PHASE01 — The profile keeps a base directory

**Branch:** `feature/feature-1e0d-phase01-profile-base-directory`
**Status:** DONE — see `docs/done/FEATURE-1E0D-PHASE01.md`

### Steps

1. Core, `IdentityProfile`: `BaseDirectory` (init, default empty), trimmed by `With(...)`'s sibling
   (`WithBaseDirectory`, or an extra optional argument), read as empty when a file has none or `null`.
2. Core, `IdentityProfileRules.ValidateBaseDirectory`: empty is fine; otherwise one line and
   `Path.IsPathFullyQualified`; `Validate` includes it.
3. `IdentityProfileStore.ReadAsync` patches a `null` base directory to empty.
4. Desktop, `IdentityProfileDialogViewModel`: takes `IFolderDialogService` and the starting base
   directory; a `BaseDirectory` property with validation; `BrowseCommand` raises the folder picker (on
   the field's directory, else the home folder); `ToProfile` carries it.
5. Desktop, `IdentityProfileDialogView`: the *Base directory* field with a *Browse* button (as the create
   dialog draws its *Create it in*), a placeholder and a one-line hint.
6. `ProfilesPageViewModel` takes `IFolderDialogService` and passes it and the profile's base directory
   to the dialog.
7. Tests:
   - Core: the base directory round-trips through the store; a file without the key reads empty;
     validation accepts empty and an absolute path, refuses a relative path and a two-line one.
   - Desktop: the dialog starts with the profile's base directory; *Browse* fills it; a relative path
     disables the primary button; adding and editing a profile stores it.

### Acceptance criteria

- A profile's base directory can be set, changed and cleared from the profile dialog, and survives a
  restart.
- A relative or multi-line path cannot be saved.
- Build clean with zero warnings; the whole suite green.

## PHASE02 — Open, clone and create start there

**Branch:** `feature/feature-1e0d-phase02-buttons-use-base-directory`
**Status:** DONE — see `docs/done/FEATURE-1E0D-PHASE02.md`

### Steps

1. `RepositoriesPageViewModel`:
   - `BaseDirectoryOf(IdentityProfile?)`: the profile's base directory when set and existing, else
     `null`;
   - `OnOpenAsync` passes the selected profile's base directory as the picker's start location;
   - `CloneParentDirectory()` → `CloneParentDirectory(IdentityProfile? profile)`: the profile's base
     directory, else the remembered directory, else home; `OnCloneAsync` uses the selected profile;
   - `OnCreateAsync` opens on the selected profile's base directory, else home.
2. `ProfilesPageViewModel.OnBrowseRepositoriesAsync` clones into the parent directory of the profile
   that owns the account.
3. Tests (`Desktop.UnitTests`): with a base directory on the selected profile, Open's picker starts there,
   the clone and create dialogs open on it, and it wins over the remembered clone directory; without one
   (or with one that is gone) the previous behaviour stands; switching the picker to another profile
   switches the directory; the host-browser clone uses the account's profile's base directory.
4. Documentation sweep: README's profile/clone lines.

### Acceptance criteria

- With the selected profile's base directory set, Open's folder picker starts there, and the clone and
  create dialogs suggest it.
- Without one, Open starts where it did, Clone suggests the remembered directory or home, Create home.
- Build clean with zero warnings; the whole suite green.

## Out of scope

- Remembering the last clone directory per profile.
- Showing the base directory on the profile's row or the home page.
- Scanning the base directory for repositories.
