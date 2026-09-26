# FEATURE-6151 — Git identity: global, profiles, local

**Status:** TODO
**Type:** FEATURE
**Branch:** one per phase, see below
**Run:** feature/2026-09-26-git-identity-profiles

## Objective

A page of its own — not a section of the settings page — for the name and email git records on every
commit, reachable from the start window as well as from a repository's window:

1. **The global identity.** Read and edit the global `user.name` and `user.email`.
2. **Profiles.** Named name-and-email pairs ("Work", "Personal", …) kept by the client, so switching
   the global identity is one click instead of retyping two fields.
3. **A repository's own identity.** Force the commits of the open repository to a specific name and
   email through its local git configuration, remove that local identity again, and fill it from the
   current profile with one button.

## Context & constraints

- Both windows have a rail: `StartNavigation` (Repositories; footer Integrations, Settings) and
  `ShellNavigation` (History, Changes, the conditional Conflicts; footer Integrations, Settings). Pages
  are singleton `PageViewModelBase`s with transient views, resolved by `ContainerPageFactory`; both
  rails are asserted by `AppWindowsTests` and `MainWindowShellTests`, and every page view by the
  theories in `ShellRenderTests` / `CompositionRootTests`.
- Every git invocation goes through `IGitCommandFactory` (global `-c` options, forbidden-operation
  check) and `IGitProcessRunner`. `git config` is not forbidden. The minimum git is 2.20: `--global`,
  `--local`, `--unset-all`, `-z` and `--get-regexp` all predate it.
- `git config --get*` exits 1 when nothing matches; `--unset-all` exits 5 when the key is absent.
- Core keeps the user's own state under `IAppPaths` as versioned JSON (`SettingsService`,
  `HostAccountService`) and writes it with `AtomicFile`, because several instances share the files.
  The configuration directory is created `0700` on Linux.
- A repository's writes are serialised through `IRepositoryContext.RunExclusiveAsync`.
- Core integration tests run in a `GitWorkspace` whose environment points `GIT_CONFIG_GLOBAL`, `HOME`
  and `XDG_CONFIG_HOME` at a throwaway directory, so a real `git config --global` write is safe there.
  **App tests are not isolated that way**: `TestServices` uses the real engine, so a global write from
  an App test would change the developer's own identity. The App tests must use a fake identity
  service.
- The README's *Where your things are kept* table lists every file the client writes, and says nothing
  is written anywhere else.

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| Where the page lives | A rail entry of its own, **Identity** (page title "Git identity", icon `IdentificationCard`), in the footer of both rails, before Integrations | "A new page, not in the settings page", "accessible from the home window as well"; the footer is where the rails keep what is about the user rather than the repository, and Settings stays last | A dialog from a toolbar (not a page); a section of the settings page (excluded by the prompt); a main-rail item (the main rail is the repository's work) |
| How the configuration is read and written | The real `git config --global` / `--local`, behind a Core `IGitIdentityService` | git honours `GIT_CONFIG_GLOBAL`, XDG locations, includes and its own lock file; parsing and rewriting `.gitconfig` by hand would get every one of those wrong eventually | Editing `~/.gitconfig` directly; a git library |
| What a profile is | A label, a name and an email, kept by the client in `identity-profiles.json` in its configuration directory (versioned, written atomically) | A profile is the client's convenience, not git's; the file sits beside the other per-user files and is listed in the README | git `includeIf` files (changes how git behaves, out of scope); `settings.json` (preferences, not a list the user curates) |
| What "use a profile" does | Writes the profile's name and email to the global git configuration at once | "Make easy the change of name and email" — one click, and the global fields show the result | Fill the global fields and wait for Save (two clicks for the one thing profiles exist for) |
| What the "current profile" is | The profile whose name and email equal the global identity, worked out on every load and marked *Current* | Always truthful, even after the configuration was changed in a terminal; nothing to keep in sync | A stored "active profile" id (drifts as soon as git is configured outside the client) |
| Where the repository's identity is edited | A **This repository** section on the same page, shown only while a repository is open — so in the repository window, not in the start window | One page for everything about who commits; the start window has no repository to configure | A separate page; a dialog from the repository toolbar |
| What "copy the values from the current profile" does | Fills the repository's name and email fields with the current profile's values; **Save** writes them; disabled when no profile is current | Copy means copy: the reader sees what will be written, and can adjust it before saving | Copy and save in one step; a picker of every profile (not asked) |
| Removing the repository's identity | `git config --local --unset-all user.name` and `user.email`, no confirmation, an info bar saying the repository now uses the global identity | Easily undone (copy from a profile, save), and the prompt asks for "the possibility to remove it" | A confirmation dialog (friction for a reversible change) |
| Saving | An explicit Save per section, enabled only when the values differ from what git has and are valid; both name and email are required | Every save rewrites a git configuration file; a half identity (name without email) mixes scopes in ways nobody means | Saving on every keystroke; allowing one field alone |
| Validation | Values are trimmed; a name is not blank and has no `<`, `>` or line break; an email is not blank, has no whitespace, `<`, `>` or line break, and has text on both sides of an `@`; each at most 256 characters; a profile label is required, at most 100 characters | git itself refuses angle brackets and line breaks in an ident; the `@` rule catches the typo that matters without policing real addresses | Full RFC 5322 checking; no validation (git would store what later breaks a commit) |
| Reading a scope | One `git config -z --<scope> --get-regexp ^user\.(name\|email)$` per scope, the last value of each key winning | One process instead of two; `-z` makes a value with spaces unambiguous | Two `--get` calls; `--list` |
| Concurrency | Local writes go through `RunExclusiveAsync` without a reference refresh; global writes rely on git's lock file and report its failure; the profile store has a lock and atomic writes | The house rule for repository writes; nothing in the references changes | Unserialised local writes |
| A corrupt profiles file | Moved aside as `identity-profiles.corrupt-<stamp>.json` and read as empty | Same as the settings file: the next save must not silently overwrite what the user had | Read as empty and overwrite on the next save (`HostAccountService`'s behaviour) |
| Deleting a profile | Asks first, like deleting a branch or disconnecting an account; never touches git's configuration | A profile is something the user typed and may not remember | Deleting without asking |
| Logging | Failures are logged; names and emails never are | They identify a person | Logging the values |
| App tests | `TestServices` replaces `IGitIdentityService` with an in-memory fake | A test must never rewrite the developer's own git identity | Pointing the real service at a temporary `GIT_CONFIG_GLOBAL` in the App tests (the App tests have no environment plumbing, and a missed spot is exactly the accident to prevent) |
| Documentation | The README's feature list and *Where your things are kept* table (the new file, and that the client writes git's own configuration only when asked), and a release-notes entry | The house sweep: the table would otherwise be wrong | No documentation |

## PHASE01 — Read and write the git identity

**Branch:** `feature/feature-6151-phase01-identity-engine`
**Status:** TODO

### Steps

1. `Core/Identity/GitIdentity.cs`: `sealed record GitIdentity(string Name, string Email)` with
   `Empty`, `IsEmpty`, `IsComplete`, `Normalised()` (trimmed); `GitIdentityRules` (static) with
   `MaximumLength`, `ValidateName`, `ValidateEmail` returning a sentence or `null`, and `Validate`.
2. `Core/Identity/GitIdentityService.cs`: `IGitIdentityService` with `GetGlobalAsync`,
   `SetGlobalAsync`, `GetLocalAsync(RepositoryHandle)`, `SetLocalAsync(RepositoryHandle, GitIdentity)`,
   `RemoveLocalAsync(RepositoryHandle)`, each taking a `CancellationToken`. Default `GitIdentityService`
   drives `git config`:
   - read: `config -z --global|--local --get-regexp ^user\.(name|email)$`, exit 1 = nothing set;
     global reads run in `AppContext.BaseDirectory` (as the environment probe does), local reads in the
     work tree;
   - write: validate (refuse an incomplete or invalid identity with `ArgumentException`), then
     `config --<scope> user.name <name>` and `config --<scope> user.email <email>`;
   - remove: `config --local --unset-all user.name` / `user.email`, exit 5 = already absent.
   Static `BuildReadArguments`, `BuildWriteArguments`, `BuildRemoveArguments` and `Parse` for unit
   tests.
3. Register `IGitIdentityService` in `AddGitClientCore`.
4. Tests — `Core.UnitTests/Identity`: the validation rules (blank, angle brackets, line breaks, `@`,
   length, trimming); the argument vectors; parsing `-z` output (spaces in a name, repeated keys →
   last wins, empty output). `Core.IntegrationTests/Identity`: against real git in a workspace, an
   unset global identity reads empty; set then read round-trips; a local identity overrides for
   `git config --get` in that repository and is read back; removing it leaves the global one; removing
   when nothing is set succeeds; an invalid identity is refused before git runs.

### Acceptance criteria

- The global and local identities can be read, written and (locally) removed through
  `IGitIdentityService`, with git doing the file work.
- An incomplete or invalid identity is refused, and nothing is written.
- Build clean with zero warnings; the whole suite green.

## PHASE02 — The identity page, in both windows

**Branch:** `feature/feature-6151-phase02-identity-page`
**Status:** TODO

### Steps

1. `StartPage.Identity` and `ShellPage.Identity`; both rails add **Identity** (`IdentificationCard`) to
   their footer before Integrations.
2. `IdentityPageViewModel : PageViewModelBase` (singleton): `GlobalName`, `GlobalEmail`, the loaded
   identity, `GlobalError` (the validation sentence), `IsGlobalUnset`, `SaveGlobalCommand` (valid and
   changed), `LoadAsync` on appearing; failures reported through the info bar.
3. `IdentityPageView.axaml`: the house page header (header icon + "Git identity"), a centred column
   (`MaxWidth` 900) with a **Global identity** card: two labelled text boxes, the validation line, a
   hint when git has no identity ("git refuses to commit until a name and an email are set"), and
   Save.
4. DI: the view (transient) and the ViewModel (singleton).
5. `TestServices` replaces `IGitIdentityService` with `FakeGitIdentityService` (in memory, local
   identities per work tree, a switch to make it fail).
6. Tests: both rails list Identity before Integrations and resolve its page; the page loads the global
   identity; Save writes a changed valid identity and is disabled when unchanged or invalid; the
   validation line and the unset hint; a failing write is reported and the fields keep what was typed;
   the view builds, lays out and sizes its header icon from the scale (added to the existing
   theories); composition-root theories include the new ViewModel.
7. README feature list + release notes.

### Acceptance criteria

- **Identity** is on both rails and opens the page; the settings page is unchanged.
- The page shows the global name and email and saves changes to the global git configuration.
- Build clean with zero warnings; the whole suite green.

## PHASE03 — Identity profiles

**Branch:** `feature/feature-6151-phase03-identity-profiles`
**Status:** TODO

### Steps

1. `Core/Identity/IdentityProfile.cs`: `sealed record IdentityProfile(string Id, string Label,
   string Name, string Email)` with `Identity` and `Matches(GitIdentity)` (name ordinal, email
   case-insensitive, both trimmed); `IdentityProfileRules.ValidateLabel`.
2. `Core/Identity/IdentityProfileStore.cs`: `IIdentityProfileStore` (`GetAllAsync`, `SaveAsync` —
   add or replace by id, `RemoveAsync`), default `IdentityProfileStore` over `identity-profiles.json`
   (`{ version: 1, profiles: [...] }`), a lock, `AtomicFile`, a corrupt file moved aside; registered in
   `AddGitClientCore`.
3. `IdentityProfileDialogViewModel` + `IdentityProfileDialogView` (label, name, email, validation
   sentence, `IsValid`, `ValidationChanged`), shown through `IContentDialogService` like the connect
   dialog.
4. The page: a **Profiles** section — rows with the label, `Name <email>`, a *Current* pill, and row
   actions *Use*, *Edit*, *Delete*; an *Add a profile* button whose dialog starts from the global
   identity; an empty state. Use writes the global identity and reloads; Delete asks first.
   `CurrentProfile` is derived from the loaded global identity.
5. Tests — Core: store round trip, replace by id, remove, missing file, corrupt file moved aside,
   insertion order; `Matches`. App: add (prefilled from the global identity), edit, delete (confirmed
   and cancelled), use (global identity written, *Current* moves), the current profile follows a
   global identity typed and saved by hand, a store failure is reported; the dialog's validation.
6. README *Where your things are kept* (`identity-profiles.json`, and that the client writes git's own
   configuration only when asked) + release notes.

### Acceptance criteria

- Profiles can be added, edited, deleted and used; using one sets the global identity in one click.
- The profile matching the global identity is marked as current.
- Profiles survive a restart and are shared by every instance.
- Build clean with zero warnings; the whole suite green.

## PHASE04 — A repository's own identity

**Branch:** `feature/feature-6151-phase04-local-identity`
**Status:** TODO

### Steps

1. The page: a **This repository** section, visible while a repository is open, naming it; `LocalName`,
   `LocalEmail`, `HasLocalIdentity`, `LocalError`; `SaveLocalCommand` (valid and changed, through
   `RunExclusiveAsync` without a refresh), `RemoveLocalCommand` (enabled when a local identity exists),
   `CopyFromCurrentProfileCommand` (enabled when a profile is current; fills the fields). A line says
   whether commits here use the repository's own identity or the global one.
2. The local identity is re-read on appearing and whenever the repository changes; closing the
   repository clears it.
3. Tests: hidden without a repository; loads a repository's local identity; save writes it through the
   fake for that work tree; remove clears it and reports it; copy fills the fields from the current
   profile and is disabled without one; switching repositories reloads; failures reported.
4. README feature line + release notes updated for the local identity.

### Acceptance criteria

- In a repository's window the page sets and removes that repository's own name and email.
- "Copy from current profile" fills the repository's fields with the current profile's values.
- The start window's page shows no repository section.
- Build clean with zero warnings; the whole suite green.

## Out of scope

- Signing keys (`user.signingkey`, `commit.gpgsign`) and any other configuration key.
- The system scope, conditional includes (`includeIf`) and showing the effective identity git will
  resolve through them.
- Choosing an identity per commit on the changes page.
- Importing profiles from, or exporting them to, git configuration files.
