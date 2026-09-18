# FEATURE-295F-PHASE03 — No path beside the file name

**Item:** FEATURE-295F — Diff viewer: sync, paths, full file
**Branch:** `feature/feature-295f-phase03-no-path-column`
**Run:** feature/2026-09-18-history-and-diffs

## Summary

A row of the flat file list shows the file's name and no longer its directory beside it.

The dimmed directory column existed for one reason — telling two files of the same name apart — and
it cost every row the width its name needs to be read at all in a 320 px panel. The information is
not gone: the row's tooltip is the whole path, as it already was, and `Path` still carries it for
the selection, the row menu and "copy path".

`showDirectory` was the only thing `ShowDirectory`, `DirectoryLabel` and `HasDirectoryLabel` existed
for, so all four went with the column rather than being left as members no caller can turn on. The
tree is untouched: it says the directory in a row of its own, which is where repeating it is not
noise.

## Files / modules touched

**Modified — App**

- `ViewModels/Panels/ChangedFilesPanelViewModel.cs` — the file-row constructor loses its
  `showDirectory` parameter, `ShowDirectory` / `DirectoryLabel` / `HasDirectoryLabel` are gone, and
  the flat list builds its rows from the name alone
- `Views/Panels/ChangedFilesPanelView.axaml` — the dimmed directory column leaves the shared row
  template and the four columns after it close up

**Modified — tests**

- `tests/Enigma.GitClient.App.UnitTests/ChangedFilesPanelTests.cs` —
  `Panel_ShowsOneFlatRowPerFileInListMode` asserts the label is the bare name while `Path` is still
  the whole path; the rendering test asserts the directory is *not* drawn in the list, where it
  previously asserted it was

**Modified — docs**

- `docs/roadmap.md`, `docs/plan/FEATURE-295F.md`

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| The three now-unused members | Removed with the column | A property no caller can turn on is dead weight, and the next reader has to work out that it is |
| Telling same-named files apart | The row's existing path tooltip | The information stays one hover away, which is what "remove their path in the list view" leaves room for |
| The rendering test | Flipped to assert the absence | It asserted the directory was drawn; the honest opposite of that assertion is the one that now guards the change |

## Deviations & follow-ups

- **None from the plan.** All four acceptance criteria are covered.
- **Line endings (recommendation only):** no CRLF churn in the touched files. No action taken.

## Build/test evidence

```
dotnet build Enigma.GitClient.slnx
  0 Warning(s)
  0 Error(s)

dotnet test --solution Enigma.GitClient.slnx
  Test run summary: Passed!
  total: 1681  failed: 0  succeeded: 1681  skipped: 0
```

Two tests changed, none added — the behaviour removed is the behaviour they asserted. No fix cycle
was needed beyond the two compile errors the removed members caused, which are the same edit.

## Documentation sweep

Scanned `README.md` and `RELEASENOTES.md`. Both say the changed files are shown "as a list or a
tree", which is still true and says nothing about what a row contains. There is no `CLAUDE.md`,
`CHANGELOG.md` or `CONTRIBUTING.md` in the repository. Nothing edited.
