# FEATURE-B14C-PHASE02 — Opening at the first change

**Item:** FEATURE-B14C — A wider minimap that finds the change
**Branch:** `feature/feature-b14c-phase02-open-at-first-change`
**Run:** feature/2026-09-20-ui-polish-diff-page

## Summary

A file now opens where its first change is, with two rows of context above it, instead of wherever
the last file happened to be scrolled to.

That "wherever" was the whole bug: `Apply` reset the sideways panes for every new patch and left the
vertical one alone, so a `ListBox` kept the pixel offset it had. A third of the way down a long
patch is a plausible-looking place to land in a short one, which is why the position looked random
rather than wrong.

The ViewModel says where the change is — `UnifiedFirstChangeRow` and `SideBySideFirstChangeRow`,
one per rendering because the side-by-side projection pairs and pads rows — and raises
`PatchChanged` from `Apply`, the one place every path ends in. The view answers it by scrolling both
renderings, so switching rendering after the file opens does not land somewhere else.

Two things the build taught, both recorded in the code:

- **The first change is the first added or removed run, not the first run.** The map's first run is
  the hunk band, and the side-by-side rendering reads the whole file as one hunk — so its band sits
  at row 0 whatever the change is, and aiming at it would have opened every file at the top. That
  is the plan's definition corrected against what the maps actually hold.
- **A virtualising panel's extent is an estimate**, revised as rows are realised, so one move
  computed from it overshot the change by about six rows. The alignment now runs up to three passes
  and stops as soon as a pass moves the view by less than a row: the first move realises the rows
  around the change, and the next is measured against them.

## Files / modules touched

**Modified — App**

- `ViewModels/Panels/DiffViewerViewModel.cs` — `UnifiedFirstChangeRow`, `SideBySideFirstChangeRow`,
  the `PatchChanged` event raised at the end of `Apply`, and the `FirstChangeRow` reduction over a
  map
- `Views/Panels/DiffViewerView.axaml.cs` — the subscription, `ShowFirstChange` and `Align`, with
  `ContextRows` naming the two rows kept above the change

**Modified — tests**

- `tests/Enigma.GitClient.App.UnitTests/DiffViewerTests.cs` — a "where a file opens" section: each
  rendering's first change for a change in the middle of a file, the top for a patch that changes
  nothing and for one whose change is its first line, `PatchChanged` raised once per patch
  (including a clear), a file opening with its change on screen and near the top of it, and a second
  file opening at its own change rather than at the offset the first left behind

**Modified — docs**

- `docs/roadmap.md`, `docs/plan/FEATURE-B14C.md`

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| What "the first change" is | The first added or removed run | The hunk band is row 0 of a whole-file rendering, so it is not where the change is |
| Which renderings are moved | Both | The reader can switch rendering after the file opens, and the other one must not be where the last file left it |
| How the target is reached | Repeated alignment, up to three passes | The extent is an estimate until the rows around the target are realised; one pass overshot by six rows |
| Where the loop stops | A pass that moves less than a row | Converged by any measure the reader could see, and it never spins |
| What the view test asserts | The change is on screen and in the top half of it | An exact offset is an assertion about a virtualising panel's estimate, which is not the behaviour anybody wants |

## Deviations & follow-ups

- **The plan's definition of "the first change" was wrong and was corrected** (first *changed* run
  rather than first run). Recorded above; the plan file keeps its original wording, as the record of
  what was planned.
- The alignment runs on every patch, including a re-read caused by expanding context. That is
  deliberate: expanding context around a change should keep the reader on the change.
- **Line endings (recommendation only):** no CRLF churn in the touched files. No action taken.

## Build/test evidence

```
dotnet build Enigma.GitClient.slnx
  0 Warning(s)
  0 Error(s)

dotnet test --solution Enigma.GitClient.slnx
  Test run summary: Passed!
  total: 1755  failed: 0  succeeded: 1755  skipped: 0
```

Six tests added (1749 → 1755). Three fix cycles: the first-change definition, the view test's
assertion, and the convergence pass — the last of which was a real defect, not a test artefact.

## Documentation sweep

`README.md` and `RELEASENOTES.md` describe the diff viewer and the minimap without saying where a
file opens, so nothing the diff touched made them wrong. No edits. There is no `CLAUDE.md`,
`CHANGELOG.md` or `CONTRIBUTING.md` in the repository.
