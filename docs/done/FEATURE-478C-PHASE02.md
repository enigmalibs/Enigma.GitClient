# FEATURE-478C-PHASE02 — Tags & checkout anything

**Item:** FEATURE-478C — Branches, tags & checkout
**Branch:** `feature/feature-478c-phase02-tags-checkout`
**Run:** feature/2026-09-15-enigma-git-client

## Summary

Tags are now managed beside the branches — the page carries a Branches/Tags switch, and the tag list
shows each tag's kind, its target, its tagger and its message, with create and delete actions. A tag
is annotated when a message is written for it and lightweight when one is not, which is the only
difference anyone actually means.

And anything in the graph can be checked out: a branch, a tag, a remote branch or a bare commit,
from the branches page, from the graph's context menu, or by double-clicking a row. Two things stand
between the user and a lost afternoon, and both are asked before anything happens: checking out
something that is not a local branch warns that HEAD will detach and offers to create a branch there
instead, and uncommitted work is met with the three real answers — stash it, discard it, or stop —
with the files at risk named.

With this phase FEATURE-478C is complete.

## Files / modules touched

**Created — Core**

- `Tags/TagService.cs` — `ITagService`: create (annotated or lightweight), delete, delete on a
  remote, push one tag, and read an annotated tag's whole message
- `Checkout/CheckoutService.cs` — `DirtyTreeResolution`, `CheckoutOptions`, `CheckoutResult` and
  `ICheckoutService`: check out anything, answer whether it would detach, and stash

**Created — App**

- `Services/CheckoutOperations.cs` — `ICheckoutOperations`: the detach warning, the question about
  local changes with the files listed, and the report afterwards
- `Services/TagOperations.cs` — `ITagOperations`: the create form and the delete confirmation
- `Views/Dialogs/CreateTagDialogView.axaml` (+ code-behind)

**Modified**

- `src/Enigma.GitClient.Core/DependencyInjection/ServiceCollectionExtensions.cs`
- `src/Enigma.GitClient.App/DependencyInjection/ServiceCollectionExtensions.cs`
- `src/Enigma.GitClient.App/ViewModels/Dialogs/BranchDialogViewModels.cs` — `CreateTagDialogViewModel`
- `src/Enigma.GitClient.App/ViewModels/Pages/BranchesPageViewModel.cs` — `TagRowViewModel`, the
  Branches/Tags switch, and checkout routed through the checkout flow
- `src/Enigma.GitClient.App/Views/Pages/BranchesPageView.axaml` — the switch and the tag rows
- `src/Enigma.GitClient.App/ViewModels/Pages/CommitRowViewModel.cs` — three more row commands
- `src/Enigma.GitClient.App/ViewModels/Pages/HistoryPageViewModel.cs` — checkout a commit, tag a
  commit, and what a double-click means
- `src/Enigma.GitClient.App/Views/Pages/HistoryPageView.axaml(.cs)` — the new menu items and the
  double-click handler
- `docs/roadmap.md`, `docs/plan/FEATURE-478C.md`

**Created — tests**

- `tests/Enigma.GitClient.Core.IntegrationTests/Tags/TagAndCheckoutServiceTests.cs` — 23 cases
  against a real repository: both kinds of tag, force, push and delete on a remote, detaching on a
  tag, a sha and a remote branch, and each answer to a dirty work tree
- `tests/Enigma.GitClient.App.UnitTests/TagsAndCheckoutTests.cs` — 22 cases on the tag list, the
  dialogs, the detach warning, the three-way dirty-tree decision, the graph's menu and a snapshot

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| Annotated or lightweight | Decided by whether a message was written | It is the only difference a user cares about, and a separate switch that changes what the message box means is one more thing to explain |
| A tag's full message | A second, cheap read through the service | The reference reader's records are newline separated, so a multi-line field cannot survive them. Rather than re-shape the reader for every ref, the annotation is fetched where a detail view shows it |
| Reading that message | `%(objecttype)` first, then `%(contents)` | `%(contents)` on a lightweight tag returns the *commit's* message, which is not the tag's annotation and must never be shown as one. Found by a test asserting a lightweight tag has none |
| Deleting a tag on a remote | The full `refs/tags/<name>`, not the short name | `git push --delete origin v1` deletes a *branch* called v1 if one exists. The short form is a loaded gun |
| Discarding local changes | `checkout --force`, never `reset --hard` | `--force` overwrites exactly what the checkout needs and nothing else; `reset --hard` would also throw away staged work the checkout never touched |
| Stashing | `stash push --include-untracked` | Without it an untracked file stays in the tree and "your changes are stashed" is a half-truth |
| Who decides about a dirty tree | The UI asks; the service takes the answer | Only the UI can ask, and only the service should run git. The answer travels as a value, so the decision table is testable without a pointer |
| The detach warning's default button | "Create a branch here…" | It is the answer that loses nothing. Detaching stays one click away for the people who mean it |
| What a double-click means | The row's branch when it has one, the commit itself otherwise | Checking out the commit under the pointer is what the specification asks for; going to the branch first is what a reader means by it when there is one |
| Where branch checkout runs | Through the checkout flow, not the branch service | Otherwise the dirty-tree question would exist for tags and not for branches, which is the same trap in a different place |

## Deviations & follow-ups

- **Deviation:** the stash created before a checkout is never popped automatically. Restoring it is
  the working-directory item's job (FEATURE-13FE), and popping it onto a different branch without
  being asked is exactly the kind of silent write this client avoids. The info bar says where the
  work went.
- **Deviation:** `ITagService.PushAsync` exists and is tested against a local bare repository, but no
  UI offers it yet — publishing belongs with the rest of the remote work in FEATURE-06FE, where
  credentials and progress live.
- **Deviation:** the tag list has no per-remote view and no "delete on the remote" action, for the
  same reason.
- **Follow-up:** a tag's full annotation is read by the service but only its subject is shown. A tag
  detail pane belongs with whatever shows a commit's own detail, not in the list.
- **Line endings (recommendation only):** no CRLF churn observed. No action taken.

## Build/test evidence

```
dotnet build Enigma.GitClient.slnx --no-incremental
  0 Warning(s)
  0 Error(s)

dotnet test --solution Enigma.GitClient.slnx
  Test run summary: Passed!
  total: 934  failed: 0  succeeded: 934  skipped: 0
```

45 tests are new in this dev. Two real defects were caught: reading a lightweight tag's "message"
returned the commit's message instead of nothing, and a `ConfigureAwait(false)` in the checkout flow
built its dialog off the UI thread — which the headless test surfaced as a cross-thread access, and
which would have thrown in the running application just the same. The tag list is written to
`snapshots/tags-page.png`.
