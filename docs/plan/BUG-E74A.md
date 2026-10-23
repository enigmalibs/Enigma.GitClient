# BUG-E74A — Stale Desktop tests red on develop

**Status:** TODO
**Type:** BUG
**Branch:** `bugfix/bug-e74a-stale-desktop-tests`
**Run:** bugfix/2026-10-23-crash-net-token-reset

## Objective

Two Desktop unit tests fail on `develop` (41dc222) on Linux, in isolation as in the whole run. The
run that follows (BUG-9DE3, BUG-3024) must end with the whole suite green, so the baseline is made
green first, in a commit of its own, before anything else changes.

## Context & constraints

Both tests were last touched by features built on a Windows machine, where the Desktop suite was
compiled but not run (BUG-6EAA standing instruction). They are stale expectations, not regressions
of the product — to be confirmed while fixing, not assumed:

- `RepositoryListStoreTests.TwoStoresOverTheSameDirectory_SeeEachOthersWrites` expects
  `["one", "two"]` and gets `["two", "one"]`. FEATURE-1DC2 ("new repositories join at the top")
  changed the order on purpose.
- `BranchesPageTests.CollapseAll_IsRememberedAcrossARefresh_AndKeepsTheSelectedBranches` sets
  `SelectedItem` and expects `SelectedBranches` to hold the row. `SelectedBranches` is fed by the
  tree's own selection (`SelectedNodes`), which only the view or `SelectBranches` fills.

## Steps

1. Read each test against the feature that changed the behaviour (FEATURE-1DC2, FEATURE-5595) and
   the code it drives.
2. If the product behaviour is the intended one, correct the test's setup or expectation so it
   asserts the intended behaviour — the test keeps its name and its intent. If the product is wrong,
   fix the product instead and say so in the completion doc.
3. No other test is touched.

## Acceptance criteria

- Both tests pass, alone and in the whole run.
- No assertion is weakened beyond what the intended behaviour requires; each test still checks what
  its name says.
- The full build has zero warnings; the whole suite (Core unit, Core integration, Desktop unit) is
  green.

## Out of scope

- Anything else in those test classes or in the code they drive.

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| Two red tests on the starting commit vs "the whole suite is green" | Fix them first, as their own item | Every later dev's Definition of Done needs an honest green suite; a separate commit keeps the unrelated fix reviewable on its own | Tolerating a "known red baseline" (the spec asks for green); folding the fixes into BUG-9DE3 (unrelated change in a safety-net commit) |
| Test or product | Decide per test from the feature that changed it | FEATURE-1DC2 changed the order on purpose; the selection model is the view's | Changing product code to satisfy stale tests |
