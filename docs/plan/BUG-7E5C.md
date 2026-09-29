# BUG-7E5C — Dialogs with a view show its type name

**Status:** DONE — see `docs/done/BUG-7E5C.md`
**Type:** BUG
**Branch:** `bugfix/bug-7e5c-dialog-shows-its-view`
**Run:** vibe/2026-09-29-dialog-view-type-name

## Objective

Every dialog whose content is a view shows that view again. A regression shipped in 4.1.0: the
Branches, Tags and Remotes tool dialogs show one line of text — the view's type name, e.g.
`Enigma.GitClient.App.Views.Pages.BranchesPageView` — and the Close button, and the page is missing.
Every other dialog whose `Content` is a `Control` is broken the same way:

- About (`AboutDialogService.cs:70`);
- commit details (`CommitDetailsDialogService.cs:70`);
- the branch forms (`BranchOperations.cs:278`, and the form helper at `:438`);
- create a tag (`TagOperations.cs:110`);
- stash (`StashOperations.cs:121`);
- add/edit a remote (`RemotesPageViewModel.cs:392`);
- the repository browser (`HostRepositoryBrowser.cs:128`);
- the profile dialogs (`ProfilesPageViewModel.cs:940`, `:1015`);
- the Repositories page form (`RepositoriesPageViewModel.cs:450`).

Plain-string questions still render, and must keep BUG-5349's wrapping.

## Context & constraints

- **The cause** (diagnosed in the draft, verified here with ilspycmd): BUG-5349 (commit `1a4c3c3`)
  added a `contentDialog|ContentDialog` style to `Themes/Styles.axaml` that sets `ContentTemplate` to
  a `DataTemplate DataType="x:String"`. In Avalonia 12.1.1, `ContentPresenter.CreateChild` sends a
  `Control` content through `FindDataTemplate(content, template)` whenever a template is set. The
  `x:String` template does not match a view, the app declares no other data templates, and the
  presenter falls back to `FuncDataTemplate.Default` — a `TextBlock` bound to `DataContext`, which
  `UpdateChild` sets to the content when a template is set. The view's `ToString()` is shown, and the
  view is never attached.
- **The dialog's template** (Enigma.Avalonia.Desktop 1.1.0, decompiled): one `ContentPresenter`, in
  the card's scrolled row, **with no name**. It is not `PART_ContentPresenter`, so
  `ContentControl.RegisterContentPresenter` declines it, its `Host` is null, and a child it generates
  is a logical child of the presenter, not of the dialog. A view given as the content stays the
  dialog's logical child (`ContentControl` adopts it first).
- `FuncDataTemplate.Default` sets nothing on its `TextBlock` but the text, so a style setter reaches
  it.
- **Why the suite stayed green:** `ToolDialogTests` checks the `Content` property, not the visual
  tree; `DialogQuestionWrapTests.AViewOfItsOwn_KeepsItsOwnTextLayout` checks a label's `NoWrap`,
  which holds trivially for a label that is never attached.
- No change to services or view models, no new dependency; Avalonia and Enigma.Avalonia.Desktop stay
  where they are.
- **Built after BUG-6EAA.** Without it the suite does not finish on Windows. The first attempt at this
  dev was set aside unmerged when that was found; its branch `bugfix/bug-7e5c-dialog-view-type-name`
  holds no commit, and the dev is rebuilt from the run branch once BUG-6EAA is in.
- **The fix makes the tool pages attach for real.** A test that opens them over a real repository
  then shares the UI thread with them, so its own teardown must not race a git the history still has
  running.

## Steps

1. `Themes/Styles.axaml`: remove the `ContentTemplate` setter. Style only the text block the
   presenter generates for a string: `TextWrapping="Wrap"` (not `WrapWithOverflow`, so an unbreakable
   word breaks too) and `TextTrimming="None"`.
2. The selector, in the draft's order:
   1. `contentDialog|ContentDialog > TextBlock` — expected not to match (the generated block's logical
      parent is the unnamed presenter), proven by the four existing `DialogQuestionWrapTests`;
   2. `contentDialog|ContentDialog /template/ ContentPresenter > TextBlock` — the `#<part name>` form
      without a name, since the part has none. The template holds one `ContentPresenter`, so this
      reaches the generated block and none of a view's own (their logical parent is their panel).
   3. Only if no selector works: the `x:String` template in the `DataTemplates` of each dialog host
      (MainWindow and StartWindow), with the reason in the completion doc.
3. Rewrite the style's comment: a `ContentTemplate` applies to `Control` content too, which is why
   the style targets the generated text block instead.
4. Tests (headless fixture, the real App and `Styles.axaml`):
   - `ToolDialogTests.ShowAsync_ShowsThePageWithItsViewModelUntilItIsClosed`: once layout settles,
     the page is a visual descendant of `window.ToolDialog` — Branches, Tags, Remotes;
   - `DialogQuestionWrapTests.AViewOfItsOwn_KeepsItsOwnTextLayout`: before the `NoWrap` check, the
     `StackPanel` and the label are in the dialog's visual tree, and no `TextBlock` shows the
     content's type name;
   - a regression test on a real form dialog on the operations host: About, shown through
     `IAboutDialogService`, in both windows — the `AboutView` is attached;
   - the BUG-5349 wrap tests pass unchanged.

## Acceptance criteria

- Every dialog whose content is a view shows that view (the tool dialogs and the forms).
- A plain-string question still wraps, untrimmed, long unbreakable words included.
- A view's own text blocks keep their own layout.
- The `Styles.axaml` comment describes Avalonia's real behaviour.
- The four BUG-5349 wrap tests pass unchanged; build clean with zero warnings; the whole suite green.

## Out of scope

- Services, view models, the dialogs' widths, rewording any question.
- Bumping Avalonia or Enigma.Avalonia.Desktop.
- A custom template that hands the `Control` back (the presenter still overwrites `DataContext` with
  the content); `TextWrapping` on the presenter (inherited into every view — rejected by BUG-5349);
  reverting BUG-5349.

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| Which selector | Probe `ContentDialog > TextBlock` first, then `/template/ ContentPresenter > TextBlock` | The draft's order. The decompiled template predicts the second: the presenter is unnamed, so the generated block is its logical child, not the dialog's. The wrap tests decide | Naming a part that does not exist (`#PART_ContentPresenter`); going straight to the `DataTemplates` fallback |
| The operations-host regression test | About, through `IAboutDialogService`, in MainWindow and StartWindow | A real form dialog on the real host, no repository to set up, and both windows' hosts are covered | Create a tag through `TagOperations` (needs an open repository for the same proof) |
| Keep `TextTrimming="None"` explicit | Yes | "Never cut" is the contract; the default happens to be `None`, the setter states it | Relying on the default |
