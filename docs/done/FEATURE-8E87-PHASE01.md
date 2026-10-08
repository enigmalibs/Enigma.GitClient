# FEATURE-8E87-PHASE01 — Shared rows, compact tags and remotes

**Item:** FEATURE-8E87 — Compact ref rows with multi-select
**Phase:** PHASE01 — Shared rows, compact tags and remotes
**Branch:** `feature/feature-8e87-phase01-compact-tags-remotes`
**Run:** feature/2026-10-08-title-watcher-refs-release

## Summary

The tags and the remotes pages now draw their lines like the changed files beside the history.

- **Shared styles** (`Themes/Styles.axaml`). The changed-files panel's values moved into classes that
  the panel and the two pages use; no value is copied.
  - `Grid.listrow`: 22 px high, 6 px between columns, transparent so the whole row is hit-testable.
  - `TextBlock.rowname`: centred, left, trimmed, full foreground.
  - `TextBlock.rowdetail`: 11 px, centred.
  - `ListBox.rows` / `TreeView.rows`: no frame, a transparent background, a sideways scrollbar when
    needed, and the tree's padding.
  - Both use the theme's own item hover and selection, as the panel always did.

  The panel draws exactly as before: its own snapshots, and every panel and toggle test, are
  unchanged.
- **The whole line answers a right-click** (`Views/LineMenus.cs`). The files panel's handler moved
  into a shared helper that opens a line's menu when a right-click lands on the container's padding,
  indentation or chevron rather than on the row. The panel and both pages attach it.
- **Tags:**
  - **The line:** a dim `Tag` icon where a file's status chip sits, then the name, and nothing else.
  - **The tooltip:** the kind, the message (when there is one), "Tagged by …" (when there is a
    tagger), and the short SHA with the commit's date.
  - **The menu:** unchanged — *Select in the history*, *Check out (detaches HEAD)*, *Push to the
    remote*, *Delete locally…*, *Delete from the remote…*.
- **Remotes:**
  - **The line:** a dim `HardDrives` icon and the name on the left, and the fetch URL on the right,
    faint, trimmed with an ellipsis.
  - **The tooltip:** the host (for a path on this machine, the path), and "Pushes to …" when pushes go
    elsewhere.
  - **The menu:** unchanged — *Fetch*, *Edit…*, *Remove…*.
- **Removed from both pages:** the per-row buttons, the pills, the second line, the separators, and
  each page's own item padding style. The headers are unchanged.

## Files / modules touched

**Created**

- `src/Enigma.GitClient.Desktop/Views/LineMenus.cs`
- `tests/Enigma.GitClient.Desktop.UnitTests/RefRowToolTipTests.cs`: 5 cases for the tag and the remote
  tooltips.
- `docs/done/FEATURE-8E87-PHASE01.md`

**Modified**

- `src/Enigma.GitClient.Desktop/Themes/Styles.axaml`: the shared classes.
- `src/Enigma.GitClient.Desktop/Views/Panels/ChangedFilesPanelView.axaml`, `.axaml.cs`: the shared
  classes, and the shared right-click helper instead of its own.
- `src/Enigma.GitClient.Desktop/Views/Pages/TagsPageView.axaml`, `.axaml.cs`
- `src/Enigma.GitClient.Desktop/Views/Pages/RemotesPageView.axaml`, `.axaml.cs`
- `src/Enigma.GitClient.Desktop/ViewModels/Pages/TagsPageViewModel.cs`: `TagRowViewModel.ToolTip`.
- `src/Enigma.GitClient.Desktop/ViewModels/Pages/RemotesPageViewModel.cs`: `RemoteRowViewModel.ToolTip`.
- Tests:
  - `TagsPageTests`: *the actions are visible on every row* became a test of the compact line. A tag
    row is as tall as a changed file's row, drawn beside it, with a 22 px `listrow`, no button, no
    pill, the name alone, and the tooltip.
  - `RemotesAndSyncTests`:
    - *the actions are visible* became a test of the compact line (the name, the faint right-aligned
      trimmed URL, no button, the tooltip);
    - the tag menu test finds its row's `Grid`;
    - the drawing test no longer looks for "1 branch".
  - `TagsAndCheckoutTests`: the drawing test checks the kind and the message are in the tooltip, not
    on the line.
  - `WholeLineMenuTests`: the tag and remote lines are right-clicked like a file line — the
    container's corner, both ends, between the columns and over the name.
- `docs/roadmap.md`, `docs/plan/FEATURE-8E87.md`: statuses.

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| The items' own padding | The theme's, as the files panel has it | "Exactly like the changed-files rows": the pages' `Padding=0, MinHeight=0` item style went, so the row pitch is the panel's (checked against a real file row in a test) |
| The remote's branch count | Off the line, and not in the tooltip | The draft's tooltip is the host and the push URL. The count is still in the remove confirmation |
| Where the URL is | A star column, right-aligned and trimmed, after an auto-width name | The name never yields to the URL |
| The right-click helper | Moved out of the files panel into `LineMenus`, attached by each view | Reuse, not a copy; the branch tree attaches it in PHASE03 |

## Deviations & follow-ups

- None from the plan. The branches page keeps its old rows until PHASE03.
- I looked at the rendered snapshots (`tags-page.png`, `remotes-page.png`, `changed-files-list.png`):
  same pitch, dim icons, names aligned.
- Line endings: the touched files are LF; no CRLF churn.

## Documentation sweep

Nothing to change: the README's tag line ("delete one here or on the remote, from its badge in the
history or its line in the tags dialog") still holds through the line's menu. The branches' "eye on
its row" goes with PHASE03.

## Build/test evidence

- `dotnet build Enigma.GitClient.slnx -c Debug --no-incremental`: 0 warnings, 0 errors, no `AVLN`
  warning.
- `dotnet test --solution Enigma.GitClient.slnx -c Debug`: 2984 total, 2983 passed, 1 skipped,
  0 failed.
- **Fix budget:** 1 cycle.
  - Before the first full run, targeted runs had already updated three remote tests that read the old
    rows.
  - The first full run then failed one more, `TagsAndCheckoutTests.Page_DrawsItsTags`, which looked
    for the kind and the message on the line.
  - It was updated to check the tooltip, and the second full run was green.
