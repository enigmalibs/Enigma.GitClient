# FEATURE-478C — Branches, tags & checkout

**Status:** DONE
**Type:** FEATURE
**Branch:** `feature/feature-478c-<phase>-<slug>` (one per phase)
**Run:** feature/2026-09-15-enigma-git-client

## Objective

Full branch and tag lifecycle management, and one-click checkout of anything visible in the graph —
a commit, a branch, a remote branch or a tag.

## Context & constraints

- Every ref name is validated with `git check-ref-format --branch` / `--normalize` **before** the
  write command runs, so bad names fail with a readable message instead of git's terse one.
- Destructive actions (delete an unmerged branch, delete a tag, force-checkout over local changes)
  require an explicit `ContentDialog` confirmation naming exactly what will be lost.
- All writes take the repository write lock from `IRepositoryContext`.

## PHASE01 — Branch management

**Status:** DONE — see `docs/done/FEATURE-478C-PHASE01.md`

**Steps**

1. Core `IBranchService`: `CreateAsync(name, startPoint, checkout)`, `RenameAsync(old, new, force)`,
   `DeleteAsync(name, force)`, `DeleteRemoteAsync(remote, name)`, `SetUpstreamAsync`,
   `CheckoutAsync(branch)`, `CheckoutRemoteAsync(remoteBranch, localName)` (creates the tracking
   branch), and `IsMergedAsync(branch, into)` for the delete confirmation.
2. Guards: refuse to delete the current branch; `force` is required and explicitly confirmed for an
   unmerged branch; renaming onto an existing name reports the conflict.
3. Branches page: local and remote branches grouped by remote, ahead/behind badges, upstream, last
   commit subject and date, a search box, and per-row actions (checkout, rename, delete, set
   upstream, merge into current — the merge call itself lands in FEATURE-6DCC).
4. Graph context menu: "Create branch here…", "Checkout", "Delete branch" on a row's ref badges.
5. A `CreateBranchDialog` (ContentDialog) with name validation shown live, a start-point selector
   defaulting to the selected commit, and a "checkout after create" switch.

**Acceptance criteria**

- Integration tests: create from HEAD and from an arbitrary sha; create with checkout; rename;
  rename onto an existing name fails; delete a merged branch; delete an unmerged branch refuses
  without force and succeeds with it; deleting the current branch is refused; checkout a remote
  branch creates a tracking branch with the right upstream; `IsMergedAsync` is correct both ways.
- Unit tests: name validation accepts valid names and rejects `..`, a trailing `/`, a leading `-`,
  spaces, `~^:?*[`, `@{`, and an empty string, with a message naming the offending rule.
- Every destructive path is gated by a confirmation in the ViewModel (tested with a faked dialog
  service returning both answers).

## PHASE02 — Tags & checkout anything

**Status:** DONE — see `docs/done/FEATURE-478C-PHASE02.md`

**Steps**

1. Core `ITagService`: `CreateAsync(name, target, message?, force)` — annotated when a message is
   given, lightweight otherwise; `DeleteAsync(name)`, `DeleteRemoteAsync(remote, name)`,
   `PushTagAsync(remote, name)` (a thin call onto FEATURE-06FE's push).
2. Core `ICheckoutService`: `CheckoutAsync(revision, CheckoutOptions)` handling a branch, a tag, a
   remote branch and a raw sha, with an explicit detached-HEAD warning, a `--force` path that is only
   reachable after confirmation, and a post-checkout `HeadState` refresh.
3. Dirty-tree handling: checkout with local changes offers **Stash and checkout**, **Discard and
   checkout** or **Cancel**; "discard" states exactly which files are lost.
4. Tags page / panel: tag list with kind, target, tagger and message, create and delete actions.
5. Graph: double-click a row to checkout, plus "Checkout this commit (detached)" and "Create tag
   here…" in the context menu; the checked-out row is visually marked.

**Acceptance criteria**

- Integration tests: create a lightweight tag and an annotated tag with a multi-line message; both
  read back correctly through `RefReader`; delete a tag; `force` overwrites an existing tag; checkout
  a branch, a tag, a remote branch and a sha, asserting `HeadState` (including `IsDetached`) after
  each; checkout with a dirty tree refuses unless stashed or discarded, and the stash path restores.
- Unit tests: tag-name validation; the dirty-tree decision table maps each user answer to the right
  service call.

## Out of scope

- Rebase — forbidden product-wide.
- Merging (FEATURE-6DCC) and pushing (FEATURE-06FE), beyond thin calls into them.

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| Ref-name validation | `git check-ref-format` plus a local pre-check | git is the authority on what a valid ref is; the local pre-check gives an instant, specific message while typing | regex only (drifts from git's rules); no validation (cryptic failures) |
| Unmerged branch delete | Refuse, then offer force behind a confirmation naming the commits at risk | Losing commits silently is the one unforgivable behaviour in a git client | always force; never allow |
| Checkout with a dirty tree | Offer stash / discard / cancel | The three things a user actually wants, with "discard" spelling out what is lost | auto-stash silently; refuse outright |
| Detached checkout | Allowed, with a clear warning and a "create branch here" shortcut | The draft explicitly asks to check out anything in the graph | forbidding detached HEAD |
