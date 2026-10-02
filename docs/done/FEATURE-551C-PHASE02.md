# FEATURE-551C-PHASE02 — Reset local to the remote's commit

**Item:** FEATURE-551C — Branches: grouping, reset, double-click
**Phase:** PHASE02 — Reset local to the remote's commit
**Branch:** `feature/feature-551c-phase02-reset-local-to-here`
**Run:** feature/2026-10-02-tags-branches-release

## Summary

Checking out a remote branch whose local branch already exists no longer fails with *A branch called
"x" already exists*. It checks out the local branch, and asks first when the two are apart, as
GitKraken does.

- **Level** (same commit): the local branch is checked out without a question. If it is already
  checked out, an info bar says so and nothing moves.
- **Apart:** a content dialog titled *Reset "main" to "origin/main"?*. Its text:
  - where each branch is (both short hashes), and what the reset does;
  - what it leaves behind: *Nothing is left behind…* when the remote already has every commit of the
    local branch, or else the commits only the local branch has (up to 10, then *…and more*);
  - *Uncommitted changes come along, as for any checkout: git stops instead of overwriting one.*

  Its buttons:
  - **Reset local to here** moves the branch to the remote's commit, checks it out, and makes it track
    that remote branch (`git checkout --track -B`). It is the default button only when nothing is left
    behind.
  - **Check out "main"** checks the branch out where it is. It is not offered when the branch is
    already checked out.
  - **Cancel**, the default button whenever commits would be left behind.
- **No local branch:** unchanged. The tracking branch is created.

Every remote checkout goes through this: the history's remote badge, the Branches dialog's remote
line, and (in PHASE03) the double-click.

## Files / modules touched

**Modified**

- `src/Enigma.GitClient.Core/Branches/BranchService.cs`: `IBranchService.ResetAndCheckoutAsync`
  (validated name, a start point beginning with a dash refused, `checkout --track -B`).
- `src/Enigma.GitClient.Desktop/Services/BranchOperations.cs`:
  - the remote path of `CheckoutAsync` now goes through `CheckoutExistingAsync`;
  - `DescribeReset` builds the question's text;
  - `ResetLocalToHere` and `MaximumListedCommits` are constants.
- `tests/Enigma.GitClient.Core.IntegrationTests/Branches/BranchServiceTests.cs`: 6 tests.
- `docs/roadmap.md`, `docs/plan/FEATURE-551C.md`: statuses.

**Created**

- `tests/Enigma.GitClient.Desktop.UnitTests/ResetLocalToHereTests.cs` (12 tests)
- `docs/done/FEATURE-551C-PHASE02.md`

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| How "nothing left behind" is decided | `IsMergedAsync(local, into: remote)`, git's exit code | `GetUnmergedCommitsAsync` returns an empty list when git fails, which would pass for "nothing to lose" and make the reset the default |
| The order of `--track` and `-B` | `checkout --track -B <name> <remote>` | `-B` takes the next argument as its name, so the option goes before it |
| The level, checked-out case | An info bar, *Already checked out*, and `false` | Nothing moved, so the history does not reload, and the click is still answered |
| The Branches dialog's local lines | Unchanged (they go through `CheckoutOperations`) | Only the remote path had the refusal this phase replaces |

## Deviations & follow-ups

- None from the plan.
- Follow-up suggestion: a local branch whose name differs from the remote's but tracks it, such as
  `their-work` tracking `origin/published`, is not considered. The pairing is by name, as
  `CheckoutRemoteAsync` always was. This is out of scope per the plan.
- Line endings: no CRLF churn; the touched files are LF.

## Documentation sweep

Nothing became wrong. README's *Checkout of anything in the graph: a branch from its badge's menu* is
still true. The question is described in FEATURE-4D5A's release notes. No `CLAUDE.md` or `AGENTS.md`
exists.

## Build/test evidence

- `dotnet build Enigma.GitClient.slnx`: 0 warnings, 0 errors.
- `dotnet test --solution Enigma.GitClient.slnx`: **2682 passed**, 0 failed, 0 skipped (18 new).
- Core (real git):
  - the reset moves and checks out, and sets the upstream;
  - it moves the branch that is checked out;
  - it carries an uncommitted change;
  - it refuses to overwrite an untracked file and moves nothing;
  - it refuses an option-like start point and an invalid name.
- Desktop (real git, a bare origin and a second clone):
  - the question and its default button in both cases;
  - each of the three answers;
  - the listed commits, capped at 10;
  - level branches, checked out or not;
  - the no-local case;
  - an overwriting file stops the reset;
  - the history's remote badge and the Branches dialog's remote line ask the same question.
- Fix budget: 0 cycles used.
