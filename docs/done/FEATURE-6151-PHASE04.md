# FEATURE-6151-PHASE04 — A repository's own identity

**Item:** FEATURE-6151 — Git identity: global, profiles, local
**Branch:** `feature/feature-6151-phase04-local-identity`
**Run:** feature/2026-09-26-git-identity-profiles

## Summary

In a repository's window the identity page gains a third card, **This repository: <name>**, that
forces the repository's commits to a name and email of its own through its local git configuration.
The card only exists while a repository is open, so the start window's page never shows it.

- The two fields show what the repository's own configuration sets (not what it inherits); **Save**
  writes them with `git config --local` through the repository's write lock (`RunExclusiveAsync`, no
  reference refresh), enabled only when they changed and are valid, with the same validation
  sentence as the global card.
- **Remove** unsets both keys (`--unset-all`), so the repository goes back to the global identity; it
  is enabled only when the repository sets something of its own. The info bar says which identity
  its commits now use, or that there is none and git will refuse.
- **Copy from current profile** fills the two fields from the profile marked *Current* — filled, not
  written; Save writes — and is enabled only while a repository is open and a profile is current (its
  tooltip shows on the disabled button too).
- A line under the fields says who the repository's commits are made as: its own identity, part of
  one, or the global one (or none at all).
- Opening another repository re-reads the card and drops anything typed for the previous one; closing
  the repository empties it. A read that lands after the repository changed is ignored.

## Files / modules touched

**Modified — App**

- `ViewModels/Pages/IdentityPageViewModel.cs` — `RepositoryName`, `LocalIdentity`, `LocalName`,
  `LocalEmail`, `HasLocalIdentity`, `IsLocalChanged`, `LocalError`, `LocalSummary`; `SaveLocalCommand`,
  `RemoveLocalCommand`, `CopyFromCurrentProfileCommand`; the local read in `LoadAsync`;
  `OnRepositoryChanged`
- `Views/Pages/IdentityPageView.axaml` — the *This repository* card

**Docs**

- `README.md` — the feature line; the sentence on what is written outside the configuration directory
  now names a repository's configuration and removing (sweep)
- `RELEASENOTES.md` — the repository's identity under *Your git identity* (sweep)

**Tests**

- `App.UnitTests/IdentityPageTests.cs` — no section without a repository (and no copy); the
  repository's own identity is read; the summary without one, with and without a global identity;
  save writes the trimmed identity for that work tree and leaves the global one alone; remove puts
  the repository back on the global identity; copy fills without writing, then Save writes; copy
  follows which profile is current; another repository brings its own identity and drops the typing,
  closing empties the section; a failed write keeps the typing; a failed read is reported; the
  realised view hides the section without a repository and shows, fills and enables it with one

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| The card's header | "This repository: <name>" | The page is reachable from any repository's window; naming it removes any doubt about which configuration is written |
| Placeholders in empty fields | "Not set: the global name/email is used" | Says what an empty field means instead of leaving a blank box |
| `HasLocalIdentity` | True when either key is set | Remove must be able to clear a half identity set in a terminal |
| Typing when the repository changes | Dropped | Values typed for one repository must never be saved into another |
| A read finishing after a repository switch | Ignored | It describes the repository that is no longer open |
| `InvalidOperationException` on write | Caught and reported | `RunExclusiveAsync` throws it when the repository closed between the click and the write |
| The copy button's tooltip when disabled | Shown (`ToolTip.ShowOnDisabled`) | A greyed button should say what it needs (a profile marked Current) |

## Deviations & follow-ups

- None from the plan.
- One fix cycle: the view test looked for the repository card's text boxes before any repository was
  open, but a card that starts hidden has not built its content yet; the test now asserts that no
  visible box exists, and lays the window out after the repository opens.
- The page was rendered headlessly in both themes with a throwaway test (not committed) to check the
  new card.
- Recommendation only: line endings were not examined; nothing in this diff showed CRLF churn.

## Documentation sweep

- `README.md` — feature line; the sentence on what is written outside the configuration directory.
- `RELEASENOTES.md` — the repository's own identity.

## Build/test evidence

- `dotnet build Enigma.GitClient.slnx --no-incremental`: 0 warnings, 0 errors.
- `dotnet test --solution Enigma.GitClient.slnx`: 2103 passed, 0 failed (11 new), after one fix cycle.
