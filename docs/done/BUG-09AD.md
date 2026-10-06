# BUG-09AD — Scrollbar hides the diff's last line

**Item:** BUG-09AD — Scrollbar hides the diff's last line
**Branch:** `bugfix/bug-09ad-diff-bottom-padding`
**Run:** vibe/2026-10-06-diff-search-refs-release

## Summary

The diff's last line no longer disappears under the horizontal scrollbar when the bar grows under
the pointer.

The Fluent scrollbars lie over the content. A scroll viewer with no padding scrolls its text to the
very bottom edge, which is exactly where the bar grows. `DiffTextEditor.OnApplyTemplate` now gives
the editor's scroll viewer (`PART_ScrollViewer`) a bottom padding equal to the theme's `ScrollBarSize`
(the bar's full, expanded thickness). The fallback is 12, Fluent's own value, when the editor can't
reach the theme's resources. The scrolled text therefore ends that much higher, and the bar covers
empty padding instead of the last line.

It applies to every diff editor, because they are all the same control:
- the unified editor and both side-by-side panes;
- commit diffs, working-tree diffs and stash diffs.

## Files / modules touched

**Modified**

- `src/Enigma.GitClient.Desktop/Controls/Diff/DiffTextEditor.cs`: `ScrollBarSizeKey`,
  `DefaultScrollBarSize`, and the padding set in `OnApplyTemplate`.
- `docs/roadmap.md`, `docs/plan/BUG-09AD.md`: statuses.

**Created**

- `tests/Enigma.GitClient.Desktop.UnitTests/DiffBottomPaddingTests.cs`:
  - scrolled to the end, the last line ends above the horizontal bar, in the unified editor and in
    both side-by-side panes;
  - the room left is the theme's `ScrollBarSize`, at least the bar's height, and at the bottom only.
- `docs/done/BUG-09AD.md`.

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| Where the padding is set | On the scroll viewer, in `OnApplyTemplate` | AvaloniaEdit's template is not ours. Setting the viewer directly works whether or not the template passes the editor's `Padding` through, which could not be checked by running the Desktop tests this session |
| How much | The theme's `ScrollBarSize` resource, looked up at run time; 12 as a fallback | Exactly what the bar can cover. The key was confirmed in `Avalonia.Themes.Fluent` 12.1.1's strings |
| Only while the bar is shown | No, always | The bar is `Auto`, and padding that comes and goes would make the text jump when a long line scrolls in. The cost is 12 px of empty space under a diff that doesn't scroll sideways |

## Deviations & follow-ups

- The plan named a style setter on the editor's `Padding` as the first choice. Setting it on the
  scroll viewer in code was chosen instead: it doesn't depend on AvaloniaEdit's template, and the
  Desktop tests that would have confirmed the template could not be run.
- **Not run:** the new tests are compiled but were not run, because you asked to skip the Desktop
  tests this session. They are what to run first on Linux, or on Windows once BUG-6EAA is fixed.
- Line endings: the touched files are LF; no CRLF churn.

## Documentation sweep

Nothing to change. No document describes the scrollbar covering the last line.

## Build/test evidence

- `dotnet build Enigma.GitClient.slnx -c Debug --no-incremental`: 0 warnings, 0 errors. This
  compiles the new tests.
- `Enigma.GitClient.Core.UnitTests`: 1159 total, 1158 passed, 1 skipped, 0 failed. The racing
  `AtomicFileTests` test is excluded (BUG-6EAA, it hangs on Windows).
- `Enigma.GitClient.Core.IntegrationTests`: 351 total, 349 passed, 2 skipped, 0 failed.
- `Enigma.GitClient.Desktop.UnitTests`: **not run**, at your request.
- Fix budget: 0 cycles used.
