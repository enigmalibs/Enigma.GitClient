# FEATURE-6151-PHASE02 — The identity page, in both windows

**Item:** FEATURE-6151 — Git identity: global, profiles, local
**Branch:** `feature/feature-6151-phase02-identity-page`
**Run:** feature/2026-09-26-git-identity-profiles

## Summary

A new **Identity** page (title "Git identity", icon `IdentificationCard`) sits in the footer of both
rails, before Integrations — on the start window and in every repository window. The settings page
is unchanged.

The page's **Global identity** card shows the global `user.name` and `user.email` as git reports them
and saves an edit back through `IGitIdentityService`:

- Save is enabled only when the typed values differ from git's (trimmed) and pass
  `GitIdentityRules`; the sentence under the fields says what is wrong, and says nothing until
  something has been typed.
- A line under the card says who new commits are made as, or — with a warning icon — that git has no
  identity and refuses to commit until one is set.
- A failed read or save goes to the info bar in git's own words (its standard error), never the
  exception's message, which repeats the command line and the values; the typed values stay in the
  fields so a retry is one click. Logs name the failure's kind, never the values.
- Coming back to the page re-reads git; a field the reader edited and did not save is kept.

App tests can no longer reach the developer's global git configuration: `TestServices` installs an
in-memory `FakeGitIdentityService` in place of the real service.

## Files / modules touched

**Created — App**

- `ViewModels/Pages/IdentityPageViewModel.cs` — the page's ViewModel (global section, `Describe`)
- `Views/Pages/IdentityPageView.axaml` / `.axaml.cs` — the page

**Modified — App**

- `Navigation/StartNavigation.cs` — `StartPage.Identity`, the rail item
- `Navigation/ShellNavigation.cs` — `ShellPage.Identity`, the rail item
- `DependencyInjection/ServiceCollectionExtensions.cs` — the view (transient) and ViewModel (singleton)
- `Views/StartWindow.axaml`, `Views/StartWindow.axaml.cs`, `ViewModels/StartWindowViewModel.cs` —
  comments naming what the start window offers now include the identity

**Docs**

- `README.md` — a feature line for the identity page; the *Where your things are kept* paragraph now
  says git's own configuration is written when an identity is saved (sweep)
- `RELEASENOTES.md` — a *Your git identity* section (sweep)

**Tests**

- `App.UnitTests/Infrastructure/FakeGitIdentityService.cs` — the in-memory identity (and a lock
  failure to throw)
- `App.UnitTests/Infrastructure/TestServices.cs` — installs it; `Identity` accessor
- `App.UnitTests/IdentityPageTests.cs` — both windows open the page with the one ViewModel; the page
  shows git's identity; the unset hint without an error; a failed read is reported; unsaved edits
  survive a revisit while untouched fields follow git; save writes the trimmed identity and reports
  it; spaces alone are no change; an unusable value is described and cannot be saved; a first
  identity on a machine without one; a failed save is reported in git's words and keeps the typing;
  `Describe`; the realised view binds the fields and the Save button
- `App.UnitTests/AppWindowsTests.cs`, `MainWindowShellTests.cs` — the footers list Identity first
- `App.UnitTests/CompositionRootTests.cs`, `ShellRenderTests.cs` — the new ViewModel and view join the
  existing theories (resolves, observes the one context, builds and lays out, header icon from the
  scale, no refresh button of its own)

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| The card | A `SettingsCardExpander` opened by default, in the settings page's centred 900-pixel column | Looks like the rest of the application's forms; the plan asked for the settings page's column |
| The Save button's look | `Button.accent` | It is the section's one primary action, as "Connect an account" is on the integrations page |
| The cancellation token | `IRepositoryContext.RepositoryLifetime`, as the integrations page uses for its own non-repository reads | Cancelled when the application closes; a cancelled read is dropped quietly |
| Reloading over an edit | A field edited and not saved is kept; untouched fields follow git | Navigating away and back must not throw typing away |
| Comparing typed values with git's | Exact (record equality) after trimming — a change of case in the email is a change | The reader may want to fix the case; "same person" comparison is for the profiles |
| The settings-page negative test | Not written | The settings page never had identity fields, and its cards are collapsed in a test; the rail tests prove the page is separate |

## Deviations & follow-ups

- The README's "nothing is written anywhere else" sentence was corrected in this phase rather than in
  PHASE03: it became wrong as soon as the page could write git's configuration.
- One fix cycle: the first build failed on the `Enigma.Avalonia` ↔ `Avalonia` namespace collision in
  the new test file (`Avalonia.Automation` written inline); a file-scope `using` fixed it.
- The page was also rendered headlessly in both themes with a throwaway test (not committed) to check
  the layout.
- Recommendation only: line endings were not examined; nothing in this diff showed CRLF churn.

## Documentation sweep

- `README.md` — feature list; *Where your things are kept* (git's configuration is written on save).
- `RELEASENOTES.md` — *Your git identity*.
- No `CLAUDE.md`, `CHANGELOG.md` or `CONTRIBUTING.md` in the repository.

## Build/test evidence

- `dotnet build Enigma.GitClient.slnx --no-incremental`: 0 warnings, 0 errors.
- `dotnet test --solution Enigma.GitClient.slnx`: 2059 passed, 0 failed (17 new), after one fix cycle.
