# BUG-7E5C — Dialogs with a view show its type name

**Item:** BUG-7E5C — Dialogs with a view show its type name
**Branch:** `bugfix/bug-7e5c-dialog-shows-its-view`
**Run:** vibe/2026-09-29-dialog-view-type-name

## Summary

Every dialog whose content is a view shows that view again: the Branches, Tags and Remotes tools,
About, commit details, and every form. Plain-text questions keep BUG-5349's wrapping.

- **The cause, verified with ilspycmd.** BUG-5349's style set a `ContentTemplate` (an `x:String`
  template) on every `ContentDialog`. In Avalonia 12.1.1, `ContentPresenter.CreateChild` sends a
  `Control` content through `FindDataTemplate` whenever a template is set. The `x:String` template
  does not match a view, so the presenter falls back to `FuncDataTemplate.Default`, a `TextBlock` of
  `DataContext.ToString()`. `UpdateChild` sets that `DataContext` to the content, so the result is the
  view's type name, and the view is never attached.
- **The fix.** The `ContentTemplate` setter is gone. One style reaches only the text block the
  presenter generates for a string: `contentDialog|ContentDialog /template/ ContentPresenter >
  TextBlock`, with `TextWrapping="Wrap"` and `TextTrimming="None"`.
  - Why this selector: Enigma.Avalonia.Desktop 1.1.0's dialog template has one `ContentPresenter`,
    with **no name**. It is not `PART_ContentPresenter`, so `ContentControl.RegisterContentPresenter`
    declines it. A child it generates stays the presenter's own logical child. A view given as the
    content is the dialog's logical child, and so are the text blocks inside it: none of them
    matches.
  - The draft's first selector, `contentDialog|ContentDialog > TextBlock`, was tried first. The wrap
    tests showed `NoWrap`, as the decompiled template predicted.
- The `Styles.axaml` comment now describes Avalonia's real behaviour.

## Files / modules touched

**Created:** `docs/done/BUG-7E5C.md`

**Modified**

- `src/Enigma.GitClient.App/Themes/Styles.axaml` — the style and its comment.
- `tests/Enigma.GitClient.App.UnitTests/ToolDialogTests.cs`:
  - `ShowAsync_ShowsThePageWithItsViewModelUntilItIsClosed` checks, once layout settles, that the page
    is a visual descendant of the tool dialog (Branches, Tags, Remotes);
  - `TheHistoryToolbar_OpensEachToolOnceARepositoryIsOpen` waits for the history's own git reads
    before its teardown (see *Deviations*).
- `tests/Enigma.GitClient.App.UnitTests/DialogQuestionWrapTests.cs` —
  `AViewOfItsOwn_KeepsItsOwnTextLayout` first checks that the panel and its label are in the dialog's
  visual tree, and that no text block shows the content's type name.
- `tests/Enigma.GitClient.App.UnitTests/AboutDialogTests.cs` — new theory
  `InEitherWindow_TheDialogShowsTheAboutViewItself`. It opens the About view on the real operations
  host, in MainWindow and in StartWindow, and checks that it is attached and draws its own text.
- `docs/roadmap.md`, `docs/plan/BUG-7E5C.md`, `docs/plan/BUG-6EAA.md` — statuses (BUG-6EAA
  abandoned).

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| Selector | `/template/ ContentPresenter > TextBlock`, no part name | The part has no name; the probe of `ContentDialog > TextBlock` failed the wrap tests |
| The toolbar test's teardown | Wait until the history is not busy | Now that the pages attach for real, the history's own load (a `stash list`, traced with `GIT_TRACE2_EVENT`) was still running when the test deleted its repository |

## Deviations & follow-ups

- **Verification is scoped to the dialog tests, not the whole suite, at the user's request.** On this
  Windows machine the whole suite is red before and after this change, for reasons unrelated to it
  (earlier runs were on Linux: their suites skip no test, while Windows skips three):
  - `TestServices.Dispose` cannot delete the read-only git objects of the test repositories (397 App
    tests fail in teardown; the same 14 `RefListSortTests` failures with the old style as with the new
    one);
  - `AtomicFileTests.AReaderRacingTheWriter_AlwaysReadsAWholeDocument` hangs: on Windows the replace
    is refused while a reader holds the file, and the test's writer dies without releasing its
    reader;
  - Git for Windows' system `core.autocrlf=true` gives CRLF working files where the tests expect LF,
    and a few font and long-ref-name tests are Windows-specific.

  An attempt to fix these (BUG-6EAA) was stopped and abandoned. Its unverified work is on the unmerged
  branch `bugfix/bug-6eaa-windows-test-suite`.
- The first attempt at this dev was set aside unmerged when the Windows failures were found. Its
  branch `bugfix/bug-7e5c-dialog-view-type-name` holds no commit.
- Line endings: no CRLF churn; the touched files are LF.

## Documentation sweep

Nothing in the README or other docs describes how a dialog lays out its content. No edit. The fix goes
into the 4.1.1 release notes with FEATURE-28C8.

## Build/test evidence

- `dotnet build Enigma.GitClient.slnx`: 0 warnings, 0 errors.
- The dialog tests: `DialogQuestionWrapTests`, `AboutDialogTests`, `ToolDialogTests` and
  `DestructiveConfirmationTests` — **40 passed**, 0 failed. The four BUG-5349 wrap tests are unchanged
  and green. `ToolDialogTests`, `DialogQuestionWrapTests` and `AboutDialogTests` were green on three
  consecutive runs.
- **The new checks catch the regression.** With 4.1.0's `ContentTemplate` style put back
  temporarily, six of them fail: the three tool dialogs, the view test, and About in both windows.
- Fix budget: 1 cycle of 3 (the selector switch and the toolbar test's teardown wait).
