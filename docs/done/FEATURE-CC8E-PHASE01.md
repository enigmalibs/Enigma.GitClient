# FEATURE-CC8E-PHASE01 — Back to the history, in blue

**Item:** FEATURE-CC8E — Changes: way back, discards, commit box
**Branch:** `feature/feature-cc8e-phase01-back-to-history`
**Run:** bugfix/2026-09-28-changes-commit-details-release

## Summary

- **A way back from the Changes page.**
  - A back button (ArrowLeft, "Back to the history") opens the page's toolbar, before its title, where
    the History diff view has its own.
  - `ChangesPageViewModel.BackToHistoryCommand` raises `HistoryRequested`.
    `MainWindowViewModel`, which now takes the Changes page, answers it with
    `shell.GoTo(ShellPage.History)`. This mirrors how the history's `WorkingDirectoryRequested` reaches
    the Changes page.
- **Escape.**
  - `ChangesPageView` handles Escape in a tunnelling `KeyDown` handler, so it goes back from anywhere
    on the page, the commit message box included. The message is kept.
  - The page is focusable and takes the focus when it is shown, unless something in it already has
    it, so the first press works even when the page was reached from the rail.
  - Escape is left alone while a content dialog is open over the window (the new
    `Visual.IsBehindOpenDialog`). The library's `ContentDialog` does not take the focus, so without
    this the Escape meant for a discard confirmation would have sent the reader to the history.
- **Blue.**
  - A `back` class for a toolbar button: the app's accent blue (`ActionAccentBrush`) behind a white
    glyph, with no frame.
  - Its own tints under the pointer and pressed: the new `ActionAccentHoverColor` and
    `ActionAccentPressedColor`, in both themes. They are defined after the toolbar's states, because
    the later style wins.
  - Both back buttons wear it: the Changes page's and the History diff view's `LeaveDiffView`.

## Files / modules touched

**Created**

- `src/Enigma.GitClient.App/Views/VisualExtensions.cs` — `IsBehindOpenDialog` (a C# 14 extension
  block).
- `docs/done/FEATURE-CC8E-PHASE01.md`

**Modified**

- `src/Enigma.GitClient.App/ViewModels/Pages/ChangesPageViewModel.cs` — `HistoryRequested`,
  `BackToHistoryCommand`.
- `src/Enigma.GitClient.App/ViewModels/MainWindowViewModel.cs` — takes the Changes page and goes to
  History on its request.
- `src/Enigma.GitClient.App/Views/Pages/ChangesPageView.axaml` — focusable, and the back button at the
  head of the toolbar.
- `src/Enigma.GitClient.App/Views/Pages/ChangesPageView.axaml.cs` — Escape, the focus on showing.
- `src/Enigma.GitClient.App/Views/Pages/HistoryPageView.axaml` — `LeaveDiffView` gets `back`.
- `src/Enigma.GitClient.App/Themes/Styles.axaml` — the `Button.toolbar.back` states.
- `src/Enigma.GitClient.App/Themes/Graph.axaml` — `ActionAccentHover`/`Pressed` colours in both themes,
  and their brushes.
- Tests:
  - `ChangesPageTests`:
    - the shell goes to History;
    - the blue button is first in the header and a click raises the request;
    - Escape with no click first;
    - Escape from the commit message, which is kept;
    - Escape while a dialog is open does nothing.
  - `ToolbarButtonLookTests` — the back look normally, under the pointer and pressed; the three colours
    in both themes.
  - `HistoryPageTests` — the diff view's back button is blue.
- `docs/roadmap.md`, `docs/plan/FEATURE-CC8E.md` — statuses.

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| Escape while a dialog is open | Ignored by the page | `ContentDialog` never takes the focus, so the key still routes through the page. Answering it would leave the page under an open question. |
| Where the page puts the focus | On itself, when shown, if nothing inside has it | That is what lets the first Escape work after arriving from the rail. A keyboard user arriving from the rail lands on the page, as the History diff view does when it opens. |
| Hover and pressed | Tints of the same blue, per theme | The toolbar's own hover would have put a grey plate and a frame back over the blue |
| Tooltip | "Back to the history", the History diff view's own | The same button in both places |

## Deviations & follow-ups

- The plan's Escape step did not foresee the open-dialog case; the guard was added (see the decision
  above). FEATURE-FC7E PHASE01 brings the History diff view a dialog of its own and uses the same guard
  there.
- Follow-up, not done: Escape does not close a content dialog while the focus stays on the page
  beneath it (the library's own Escape handling needs the focus inside the dialog). This is unchanged
  from before.
- Line endings: no CRLF churn.

## Documentation sweep

The README's "Working directory" bullet names what the page does, not how to leave it. No edit. The
release notes are FEATURE-10AA's.

## Build/test evidence

- `dotnet build Enigma.GitClient.slnx`: 0 warnings, 0 errors.
- `dotnet test --solution Enigma.GitClient.slnx`: **2467 passed**, 0 failed, 0 skipped (9 new).
- Fix budget: no fix cycle.
