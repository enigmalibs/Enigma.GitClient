# FEATURE-1085 — Legible ref row details

**Item:** FEATURE-1085 — Legible ref row details
**Branch:** `feature/feature-1085-ref-row-details`
**Run:** feature/2026-10-21-ui-polish-release-650

## Summary

The text on the right of branch and remote lines is now readable in the dark theme:

- a branch's badges (*checked out*, *worktree*, *hidden*, ahead/behind counts, *local only*);
- the branch count on *Local*, each remote and each folder;
- a remote's URL.

**Bigger:** these texts move from `rowdetail` (11 px) to a new `refdetail` class (12 px). The icons on
the badges stay at the `pill` size (12 px), which now matches the text.

**Lighter:** text and badge icons change from `faint` to `dim`, which is `EnigmaForegroundSecondaryBrush`
instead of `EnigmaForegroundTertiaryBrush`. You asked for the same key in both themes, and this is it:

| Theme | Before | Now | Contrast before → now |
|---|---|---|---|
| Dark (on `#1E1F22`) | `#4E5157` | `#6F737A` | about 2 : 1 → 3.5 : 1 |
| Light (on `#F7F8FA`) | `#A0A3AA` | `#6F737A` | about 2.4 : 1 → 4.5 : 1 |

Light mode gets a little darker, which reads better too.

A selected row still raises both roles to the full foreground: the `:selected` rule covers `dim` as
it did `faint`.

The worktrees dialog and the changed-files panel keep `rowdetail faint`, as planned.

## Files / modules touched

**Modified**

- `src/Enigma.GitClient.Desktop/Themes/Styles.axaml`:
  - the `TextBlock.refdetail` style, with a comment saying why;
  - the list-row comment names it.
- `src/Enigma.GitClient.Desktop/Views/Pages/BranchesPageView.axaml`:
  - eight `rowdetail faint` → `refdetail dim`;
  - four badge icons `pill faint` → `pill dim`;
  - the badge comment.
- `src/Enigma.GitClient.Desktop/Views/Pages/RemotesPageView.axaml`: the URL → `refdetail dim`, and the
  page comment.
- `tests/Enigma.GitClient.Desktop.UnitTests/BranchesPageTests.cs`: the branch-line test also checks
  that the badges are `refdetail`, 12 px, in the secondary brush.
- `tests/Enigma.GitClient.Desktop.UnitTests/RemotesAndSyncTests.cs`: the remote-line test expects
  `refdetail dim` at 12 px instead of `faint`.
- `tests/Enigma.GitClient.Desktop.UnitTests/ShellRenderTests.cs`: a new test,
  `ARefsDetail_IsAStepLargerThanAFilesDetail` (11 px `rowdetail` against 12 px `refdetail`, centred,
  secondary brush).
- `docs/roadmap.md`, `docs/plan/FEATURE-1085.md`: statuses.

**Created**

- `docs/done/FEATURE-1085.md`

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| A new class or a modifier on `rowdetail` | A separate `refdetail` class | Reads as a role, like `rowname` / `rowdetail`; a `rowdetail.ref` compound would hide the size in a selector |
| Badge icons | `pill dim` | An icon fainter than its own text would look disabled |
| Group and folder counts | Included | "the number of a branch": they sit in the same right-hand column of the same tree |

## Deviations & follow-ups

- **The Desktop suite is not run** on this machine (standing instruction). The new and updated tests
  are written and compiled.
- **Follow-up, if wanted:** the worktrees dialog's badges (*main*, *this window*, *locked*,
  *folder missing*) use the same `rowdetail faint` pair and likely have the same dark-mode problem.
  Switching them is a one-line change per badge. Left out because you named branches and remotes.
- Line endings: the touched files are LF; no CRLF churn.

## Build / test evidence

- `dotnet build Enigma.GitClient.slnx`: succeeded, **0 warnings, 0 errors** (AVLN included).
- `Enigma.GitClient.Core.UnitTests`: 1307 total, 1306 passed, 0 failed, 1 skipped (pre-existing).
- `Enigma.GitClient.Core.IntegrationTests`: 388 total, 386 passed, 0 failed, 2 skipped (pre-existing).
- `Enigma.GitClient.Desktop.UnitTests`: **compiled, not run** (standing instruction). One test added,
  two extended.

## Documentation sweep

Nothing stale: the README does not describe the badges' size or colour. The 6.5.0 notes
(FEATURE-6E21) will mention the change.
