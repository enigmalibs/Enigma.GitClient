# FEATURE-CC8E — Changes: way back, discards, commit box

**Status:** TODO
**Type:** FEATURE
**Branch:** one per phase, see below
**Run:** bugfix/2026-09-28-changes-commit-details-release

## Objective

- A back button on the Changes page, like the History diff view's, that goes to the History page; Esc
  does the same. Both back buttons are blue, so they are easy to see.
- The Changes page's *Discard everything* button is red, and it asks a plain confirmation — no typed
  repository name — whose confirm button is red, as every discard's is.
- The commit box loses its *Amend* and *Sign off* check boxes, and the code behind them goes too.

## Context & constraints

- **Navigation.** The shell's rail is `IShellNavigation` (`GoTo(ShellPage.History)`). A page cannot
  hold it — the navigation builds the page ViewModels — so the History page raises
  `WorkingDirectoryRequested` and `MainWindowViewModel` answers with `shell.GoTo(ShellPage.Changes)`.
  The Changes page does the same the other way. `ChangesPageViewModel` is a singleton.
- **Escape.** `HistoryPageView` handles Escape in a tunnelling `KeyDown` handler on the page and moves
  the focus into the diffs when they open, so the first press works (FEATURE-F04E PHASE02). A page
  shown from the rail leaves the focus on the rail, outside the page's route.
- **Buttons.** `Button.toolbar` (`Themes/Styles.axaml`) is transparent with a border.
  `ActionAccentBrush` is the app's blue (#3574F0 dark / #2E63D6 light, `Themes/Graph.axaml`). The
  library's `EnigmaErrorBrush` is a text red, too light to carry white text.
- **Dialogs.** `ContentDialog`'s primary button is `PART_PrimaryButton` with `Classes="accent"`.
  `ContentDialogService.ShowAsync` resets the host's content and buttons before each dialog, but not
  its `Classes`.
- **Discards today.**
  - `ChangesPageViewModel.OnDiscardAllAsync` builds a `ConfirmTextDialogViewModel`/`View` asking for
    the repository's name — their only user.
  - `OnDiscardAsync` (one row) asks through `ConfirmAsync`.
- **Amend and Sign off.**
  - View: two check boxes in `ChangesPageView.axaml`.
  - VM: `ChangesPageViewModel.Amend`, `SignOff`, `CommitButtonText`, `OnAmendChangedAsync`, and the
    amend clause of `CanCommit`.
  - Core: `CommitRequest.Amend`/`SignOff` with `--amend`/`--signoff`, and
    `ICommitService.GetLastCommitMessageAsync` (used only to prefill an amend).
  - Tests: those in `ChangesPageTests` and `WorkingDirectoryServiceTests`.

## PHASE01 — Back to the history, in blue

**Branch:** `feature/feature-cc8e-phase01-back-to-history`
**Status:** TODO

### Steps

1. `ChangesPageViewModel`: `BackToHistoryCommand` raising a `HistoryRequested` event;
   `MainWindowViewModel` takes the Changes page and answers with `shell.GoTo(ShellPage.History)`.
2. `ChangesPageView.axaml`: the back button (ArrowLeft, "Back to the history") at the head of the
   page's toolbar, before its title.
3. `ChangesPageView.axaml.cs`: Escape anywhere on the page goes back, in a tunnelling `KeyDown`
   handler (the commit message keeps what was typed). When the page is shown it takes the focus if
   nothing inside it has it, so the first Escape works.
4. A `back` look for a toolbar button, in `Styles.axaml`: accent-blue fill, white glyph, no border,
   with hover and pressed tints (`ActionAccentHoverColor`/`ActionAccentPressedColor` in both theme
   dictionaries). It applies to the Changes page's back button and to the History diff view's
   `LeaveDiffView`.
5. Tests:
   - the command raises the event, and the shell moves to History;
   - Escape on the Changes page returns to History, from the commit message box too, with no click
     first;
   - both back buttons paint the accent brush.

### Acceptance criteria

- The Changes page has a back button at the left of its header; clicking it, or pressing Esc on the
  page, shows the History page.
- The History diff view's back button and the Changes page's are blue, with a white arrow, in both
  themes.
- Build clean with zero warnings; the whole suite green.

## PHASE02 — Red discards, one plain question

**Branch:** `feature/feature-cc8e-phase02-red-discards`
**Status:** TODO

### Steps

1. A `danger` look, in `Styles.axaml` and the theme dictionaries (`ActionDangerColor` with its hover
   and pressed tints, and a white foreground):
   - `Button.toolbar.danger` — a red fill and a white glyph; when disabled, the ordinary disabled
     toolbar look, so a red button always means "this will throw work away";
   - `ContentDialog.danger /template/ Button#PART_PrimaryButton` — the dialog's confirm button, red.
2. `Services/ContentDialogServiceExtensions.cs` (C# 14 extension block on `IContentDialogService`):
   `ConfirmDestructiveAsync(title, message, confirmText)`.
   - It asks a plain question: the message, the red confirm button, and Cancel as the default.
   - The host carries `danger` for that dialog's lifetime only, removed in a `finally`.
   - It returns `true` only for the primary button.
3. `ChangesPageViewModel`:
   - *Discard everything* asks through it: "Discard everything" — "Throw away every change in *N
     files*? This cannot be undone." Confirm button: "Discard everything".
   - The one-row *Discard…* uses it too: the same paths listed, confirm button "Discard".
   - `ConfirmTextDialogViewModel`/`View` and their DI registration are deleted.
4. `ChangesPageView.axaml`: the *Discard everything* button gets `danger`.
5. Tests:
   - *Discard everything* asks one yes/no question with no text field; confirming discards, and
     cancelling does not;
   - the dialog carries `danger` while it is shown and not afterwards, and a later question is not
     red;
   - the confirm button and the toolbar button paint the danger brush in a rendered window, and the
     disabled toolbar button does not.

### Acceptance criteria

- The *Discard everything* button is red while it can discard, and looks like any disabled button when
  it cannot.
- Clicking it asks one confirmation with a red *Discard everything* button and a default *Cancel*; no
  repository name has to be typed.
- A one-file discard's confirm button is red too; every other question keeps its usual look.
- Build clean with zero warnings; the whole suite green.

## PHASE03 — No Amend, no Sign off

**Branch:** `feature/feature-cc8e-phase03-no-amend-signoff`
**Status:** TODO

### Steps

1. `ChangesPageView.axaml`: remove the two check boxes; the commit row is the counters and the
   *Commit* button.
2. `ChangesPageViewModel`: remove `Amend`, `SignOff`, `CommitButtonText` (the button says *Commit*),
   `OnAmendChangedAsync`, the amend clause of `CanCommit` and the amend wording of the success
   message.
3. Core: remove `CommitRequest.Amend`/`SignOff`, their `--amend`/`--signoff` arguments and the amend
   exemption of the nothing-staged check, and `ICommitService.GetLastCommitMessageAsync`.
4. Tests: delete the amend/sign-off tests (App and Core), keep the rest; a commit with nothing staged
   is still refused.

### Acceptance criteria

- The Changes page's commit box has no *Amend* and no *Sign off*.
- No code path of the application amends or signs off a commit.
- Build clean with zero warnings; the whole suite green.

## Out of scope

- Red confirmations for the other irreversible questions (delete a branch, a tag or a stash, hard
  reset) — a follow-up.
- Changing what *Discard everything* discards (every change that is not staged, as today).

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| How the Changes page reaches History | A `HistoryRequested` event the window's ViewModel answers with `shell.GoTo` | The pattern `WorkingDirectoryRequested` set; a page cannot hold the navigation that builds it | Injecting `IShellNavigation` into the page (circular construction) |
| Where the back button goes | The head of the Changes toolbar, before the title | Where the History diff view has it: the same gesture in the same place | The right end of the toolbar |
| Esc from inside the commit message | Goes back too; the message is kept | "Listen to the Esc key" on the page; the History diffs do the same from their filter box, and nothing is lost | Ignoring Esc in text boxes |
| "Blue" | Accent fill with a white glyph, on a `back` class | Readable and unmistakable, in the app's own blue | A blue glyph on a transparent button (easy to miss, which is the complaint); `Button.accent` (reserved for the page's one primary action) |
| "Red" button | A red fill with a white glyph; plain when disabled | Mirrors the blue request; a red button that cannot be pressed would say "danger" for nothing | A red glyph only |
| Red confirm | A `danger` class on the host for one dialog, through `ConfirmDestructiveAsync` | The library has no per-button style and does not reset `Classes`; one helper keeps it scoped | Styling every dialog's primary button; a custom dialog view with its own buttons |
| Which confirmations are red | Every discard: everything, one file, and FEATURE-FC7E's uncommitted discard | The same irreversible act must look the same wherever it is asked | Only *Discard everything* |
| The typed-name dialog | Deleted with its only use | Dead code otherwise | Keeping it for later |
| "Remove their usage in code too" | View, ViewModel and Core, with their tests | Nothing left that could amend or sign off | Keeping the Core options unused |
