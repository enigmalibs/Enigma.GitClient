# FEATURE-711F — Repository lists per profile

**Status:** DONE
**Type:** FEATURE
**Branch:** one per phase, see below
**Run:** feature/2026-09-30-profile-lists-readme-diffs

## Objective

Change how the start window's repositories page works:

- the repository list belongs to a profile, so every profile has its own list;
- a combobox on the page chooses the profile whose list is shown;
- when no profile exists (at the first start), a profile named "Default" is created;
- the user orders the list by dragging the rows;
- the "Pin to the top" button, and the code behind it, go away;
- a button that switches the theme sits left of the About button.

The lists saved before this change are not carried over. Every profile starts with an empty list,
which the user accepted.

## Context & constraints

- **The list today.** `App/Services/RecentRepositoryStore.cs`: one list, most recently opened first,
  with pinned entries above the rest. It is capped at 20 unpinned entries and stored as a versioned
  JSON file (`recent-repositories.json`), read again before every write and written atomically
  (several instances share it). `RecentRepository(Path, Name, LastOpenedUtc, IsPinned)` flags a path
  that has gone (`Exists`).
- **Its users.** `RepositoriesPageViewModel` (`Recent`, `HasRecent`, `ReloadRecentAsync`, the
  open/forget/pin/new-window commands, create and clone touching the list) and `RepositoryOpener`
  (the page's Open and the command line `Enigma.GitClient.App <path>`). `StartWindowViewModel`
  re-reads the list when the window comes back from a repository.
- **Profiles.** `Core/Identity/IdentityProfile.cs` and `IdentityProfileStore.cs`: `Id`, `Label`,
  `Name`, `Email`, in `identity-profiles.json`. `IdentityProfileRules.Validate` requires a label, a
  name and an email. The current profile is the first one whose name and email match git's identity.
  **`ProfilePushRule`: a matching profile with no integration refuses every push**, and
  `GitCredentialResolver` signs git in with the same matching profile. `ProfilesPageViewModel` shows
  the profiles (Use, Edit, Delete, Connect), and deleting one also removes its accounts.
- **Settings.** `Core/Configuration/AppSettings.cs` holds remembered values such as
  `CloneParentDirectory`. A file written before a setting existed reads it as its default.
- **Dragging.** Drags in this app are pointer gestures the page runs itself (press, threshold,
  capture, move, release), never a platform drag session. On XWayland that session shows the refusal
  cursor (BUG-11A4). `BranchesPageView.axaml.cs` is the pattern (`BranchDragGesture.IsDrag`,
  `ScrollFor`, the auto-scroll timer, Escape, capture lost), and its tests drive the pointer headlessly.
- **The theme toggle.** `MainWindowViewModel.OnToggleTheme` switches `RequestedThemeVariant` to the
  variant not on screen and records it as `AppSettings.Theme` (BUG-449E). The repository toolbar
  shows it as a `Palette` icon left of About.
- **Tests.** xUnit v3 and headless Avalonia (`TestServices`, which uses a throwaway configuration
  directory): `RecentRepositoryStoreTests`, `RepositoriesPageTests`, `ProfilesPageTests`,
  `ProfileIntegrationsTests`, `MainWindowShellTests`, `CompositionRootTests`, and the Core identity
  tests.

## PHASE01 — Profiles without a name and email

**Branch:** `feature/feature-711f-phase01-identityless-profiles`
**Status:** DONE — see `docs/done/FEATURE-711F-PHASE01.md`

The "Default" profile PHASE02 creates must not carry an identity. With git's global identity it would
match, and a matching profile without integrations refuses every push, so every existing user would
lose push. This phase makes such a profile legal.

### Steps

1. Core:
   - `IdentityProfile.HasIdentity` (the name or the email is set);
   - `IdentityProfileRules.Validate`: a label is still required. An empty identity (no name and no
     email) is accepted. A partial or invalid one is refused, as today.
2. App, Profiles page:
   - a profile without an identity is summarised as "No name or email";
   - its Use button is hidden, and `OnUseProfileAsync` refuses it;
   - it is never the current profile (`Matches` already requires a complete identity).
3. The profile dialog accepts empty name and email fields, with a line saying that such a profile
   changes nothing in git.
4. Tests:
   - the rule: empty accepted; name only refused; email only refused; label still required;
   - the store saves and reads a profile without an identity;
   - the Profiles page row: its summary, no Use, never current;
   - the dialog is valid with both fields empty and invalid with one.

### Acceptance criteria

- A profile can be saved with a label and no name and email, and edited without having to add them.
- Such a profile never becomes the current one, offers no Use, and changes nothing about commits,
  pushes or sign-ins.
- Build clean with zero warnings; the whole suite green.

## PHASE02 — A repository list per profile

**Branch:** `feature/feature-711f-phase02-profile-lists`
**Status:** DONE — see `docs/done/FEATURE-711F-PHASE02.md`

### Steps

1. Core:
   - `IIdentityProfileStore.EnsureAnyAsync`: when there is no profile, save
     `IdentityProfile.CreateDefault()` (Id `default`, label `Default`, no identity) and return the
     list. The fixed id means two instances creating it at the same time write the same profile;
   - `AppSettings.SelectedProfileId` (empty by default, trimmed by the normaliser).
2. App, `Services/RepositoryListStore.cs` (it replaces `RecentRepositoryStore`):
   - `ListedRepository(Path, Name)` with `Exists`;
   - one file, `repository-lists.json` (version 1): `{ version, profiles: { "<id>": [ … ] } }`.
     `recent-repositories.json` is no longer read and is left where it is;
   - `GetAsync(profileId)`, `AddAsync(profileId, path, name)` (appends a path that is not listed yet;
     a listed path keeps its place and takes the new name), `RemoveAsync(profileId, path)`,
     `RemoveProfileAsync(profileId)`;
   - no pin, no ordering by date, no cap. The same gate, re-read before writing, atomic write, and
     corrupt-file backup as today.
3. App, `Services/ProfileSelection.cs` (`IProfileSelection`):
   - `LoadAsync()` returns every profile (via `EnsureAnyAsync`) and the selected one: the profile
     `SelectedProfileId` names, else the first;
   - `Select(profileId)` records the choice in the settings.
4. `RepositoryOpener` adds the repository to the selected profile's list. The repositories page reads
   and changes the selected profile's list: open, forget, create and clone.
5. Remove "Pin to the top": the button, `TogglePinCommand`, `SetPinnedAsync`, `IsPinned`, and their
   tests.
6. Deleting a profile on the Profiles page also removes its repository list.
7. DI: register the two services in place of the old store. Update the doc comments that say
   "recent" or "most recently opened first".
8. Tests:
   - the store: add appends; re-adding keeps the place; remove; per-profile isolation; remove
     profile; persistence across instances; a corrupt file moved aside; the old file ignored;
     concurrent adds;
   - the selection: Default is created when there is none, and only once; a remembered choice is
     used; a stale id falls back to the first profile;
   - the page and the opener fill the selected profile's list;
   - deleting a profile drops its list.

### Acceptance criteria

- At the first start, with no profiles, a "Default" profile exists and the page shows its list,
  initially empty.
- Opening, creating or cloning adds the repository to the selected profile's list, at the end. Opening
  a listed repository does not move it.
- Two profiles never see each other's repositories. Deleting a profile removes its list.
- There is no pin anywhere.
- Build clean with zero warnings; the whole suite green.

## PHASE03 — The profile picker on the home page

**Branch:** `feature/feature-711f-phase03-profile-picker`
**Status:** DONE — see `docs/done/FEATURE-711F-PHASE03.md`

### Steps

1. `RepositoriesPageViewModel`:
   - `Profiles` (every profile) and `SelectedProfile` (two-way);
   - changing the selection records it (`IProfileSelection.Select`) and shows that profile's list;
   - the page reloads the profiles every time it appears, so a profile added, renamed or deleted on
     the Profiles page shows up here.
2. `RepositoriesPageView.axaml`: a `ComboBox` of the profiles' labels in the header, after the
   "Repositories" title, with an automation name.
3. Tests:
   - the combobox lists every profile, with the remembered one selected;
   - choosing another profile shows its list and is remembered after the page is rebuilt;
   - a profile deleted elsewhere falls back to the first;
   - an item opened while a profile is selected lands in that profile's list.

### Acceptance criteria

- The page has a combobox of the profiles. Picking one shows its repositories, and the choice is
  still there after a restart.
- A profile added or renamed on the Profiles page is in the combobox when the page is shown again.
- Build clean with zero warnings; the whole suite green.

## PHASE04 — Reorder repositories by dragging

**Branch:** `feature/feature-711f-phase04-drag-reorder`
**Status:** DONE — see `docs/done/FEATURE-711F-PHASE04.md`

### Steps

1. `RepositoryListStore.MoveAsync(profileId, path, index)`: moves an entry to a position, clamped to
   the list.
2. `RepositoriesPageViewModel.MoveAsync(entry, index)`: calls the store and shows the result.
3. `RepositoriesPageView.axaml(.cs)`, following `BranchesPageView`'s pattern:
   - a press on a row's body (not on its buttons) becomes a drag past the threshold, and the pointer
     is captured by the list;
   - a line on the row edge shows where the row would land;
   - the list scrolls while the pointer is near its top or bottom edge;
   - release moves the row, Escape or a lost capture cancels, and a click is still a click;
   - a grip icon at the left of each row, with a "Drag to reorder" tooltip, and the drag-move cursor
     while dragging.
   The insertion-index arithmetic goes in a small testable static helper.
4. Tests:
   - the store: move up, down, to the ends, in place, an unknown path, and a clamped index;
   - the gesture: a headless press, move and release reorders the list and persists it; a press on a
     button starts nothing; Escape cancels; the insertion index helper as a theory.

### Acceptance criteria

- Dragging a repository row onto another place in the list moves it there, and the order is kept
  across restarts and per profile.
- The buttons on a row still work as clicks. Escape cancels a drag.
- Build clean with zero warnings; the whole suite green.

## PHASE05 — A theme button beside About

**Branch:** `feature/feature-711f-phase05-start-theme-button`
**Status:** DONE — see `docs/done/FEATURE-711F-PHASE05.md`

### Steps

1. Move the toggle out of `MainWindowViewModel` into `Services/ThemeSwitcher.cs` (`IThemeSwitcher.Toggle`,
   singleton). `MainWindowViewModel.ToggleThemeCommand` uses it.
2. `RepositoriesPageViewModel.ToggleThemeCommand`. On `RepositoriesPageView`, a toolbar button with
   the `Palette` icon and the tooltip "Switch between the dark and light themes", between the
   separator and About (so it sits left of About), as the repository window has it.
3. Tests: the start page's command switches the variant and records the preference, and the existing
   `MainWindowShellTests` still pass through the shared service.

### Acceptance criteria

- The start window has a theme button left of About. It switches the theme for the whole app, and
  the choice survives a restart.
- Build clean with zero warnings; the whole suite green.

## Out of scope

- Carrying the old recent list over to a profile. The user accepted the reset.
- Deleting `recent-repositories.json`.
- Tying the combobox to git's identity ("Use" stays on the Profiles page).
- Reordering from the keyboard.
- Moving a repository from one profile's list to another's.

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| What picking a profile in the combobox does | Chooses the list shown; git's identity is untouched | The draft asks for lists per profile. A combobox that rewrites `~/.gitconfig` on selection is a hidden side effect | Also applying the profile's identity (the Profiles page's Use) |
| The Default profile's identity | None: profiles may exist without a name and email (PHASE01) | With git's identity it would match, and a matching profile without integrations refuses every push. Every existing user would silently lose push | Copying git's global identity; leaving push rules to special-case "Default" |
| Where the lists live | A new versioned `repository-lists.json` keyed by profile id; the old file is left untouched | The reset is accepted, and an older build still reading its own file can never overwrite the new format | Version 2 of `recent-repositories.json` (an older build would clobber it); lists inside `identity-profiles.json` (Core would carry an App concern) |
| Ordering | The user's order: opening never reorders, a new repository is appended at the end, no cap | Most-recent-first would undo every drag, and a cap would silently drop an entry the user placed | Most recently opened first with drag on top; inserting new entries at the top |
| The profile shown at start | The last one chosen (`SelectedProfileId`), else the first | Stable, cheap, and independent of git's identity | The profile matching git's identity (a git read on start, and contradicts the first decision) |
| Which list the command line adds to | The selected profile's | The only profile the user has pointed at | A list of its own; none |
| When Default is created | Whenever the profiles are read for the start page or the opener and none exists; fixed id `default` | Covers the first start and a deleted last profile. The fixed id makes two instances starting together converge on one profile | Refusing to delete the last profile; a random id (duplicate Defaults under a race) |
| A deleted profile's list | Removed with the profile | Otherwise it lingers in the file forever, unreachable | Leaving orphans |
| The type names | `RepositoryListStore` / `ListedRepository` in place of `RecentRepository*` | The list is no longer "recent", and the store is being rewritten anyway | Keeping the misleading names |
| How the drag works | A pointer gesture the page runs, as the branches list does, with an insertion line, auto-scroll and Escape | The platform drag shows a refusal cursor on XWayland (BUG-11A4). The house pattern is proven and testable headlessly | `DragDrop.DoDragDropAsync` |
| Where a drag starts | Anywhere on the row but its buttons, with a grip icon as the cue | Big target. The grip tells the user the list can be reordered | Only from the grip; a separate "reorder mode" |
| The theme button | The repository toolbar's behaviour through a shared `IThemeSwitcher` | One implementation of "switch and remember" for both windows | Copying `OnToggleTheme` into the page |
| Split | Five phases: identity-less profiles, lists per profile, the picker, dragging, the theme button | One reviewable concern each. The Default profile needs PHASE01 first to be safe | Fewer, larger devs |
