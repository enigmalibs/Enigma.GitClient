# FEATURE-0842-PHASE02 — The tag name has the focus

**Item:** FEATURE-0842 — History: checked-out line, tag focus
**Branch:** `feature/feature-0842-phase02-tag-name-focus`
**Run:** feature/2026-09-29-rail-clone-history

## Summary

When *Create a tag* opens, the caret is in its name box, so the name can be typed straight away.
The dialog is opened from the history's toolbar, a line's or a badge's menu, or the Tags dialog. All
of these go through `TagOperations.CreateAsync`, which builds a new `CreateTagDialogView` every time.
The view takes the focus itself.

## Files / modules touched

**Created**

- `docs/done/FEATURE-0842-PHASE02.md`

**Modified**

- `src/Enigma.GitClient.App/Views/Dialogs/CreateTagDialogView.axaml` — `x:Name="NameBox"` on the name
  box.
- `src/Enigma.GitClient.App/Views/Dialogs/CreateTagDialogView.axaml.cs` — on `Loaded`, posts
  `NameBox.Focus()` while the box is effectively visible (the changes page's `TakeTheFocus`
  pattern).
- `tests/Enigma.GitClient.App.UnitTests/TagsAndCheckoutTests.cs` —
  `CreateTagDialog_OpensWithTheCaretInTheName`: a real host `ContentDialog`, opened while a button
  has the focus, ends with the focus in the name box. Checked to fail with the focus call disabled.
- `docs/roadmap.md`, `docs/plan/FEATURE-0842.md` — statuses. The item is `DONE`.

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| Where the focus is taken | In the view, on `Loaded` | Every way of opening the dialog builds a new view, so it works whatever opened it, and `TagOperations` doesn't need to know the view's controls |
| Select the text too | No | The box always opens empty; nothing to select |

## Deviations & follow-ups

- **"Enter confirms it" is not true, and was not made true.** The plan's acceptance criterion
  assumed the dialog's `DefaultButton = Primary` makes Enter press *Create*. In
  Enigma.Avalonia.Desktop 1.1.0, `DefaultButton` is declared but nothing uses it (no `IsDefault` in the
  dialog's template, no Enter handling). The draft asked for the focus only, so this dev does exactly
  that.
  - Follow-up: wire `DefaultButton` to `Button.IsDefault` in the library's `ContentDialog`. That would
    give every dialog of the application Enter-to-confirm at once, which a per-view key handler here
    would not.
- Follow-up, out of scope: the other naming dialogs (create/rename branch, stash, remote) could take
  the focus the same way.
- Line endings: no CRLF churn.

## Documentation sweep

The README's "Tag management — create (lightweight or annotated) and delete" stays true. No edit.

## Build/test evidence

- `dotnet build Enigma.GitClient.slnx --no-incremental`: 0 warnings, 0 errors.
- `dotnet test --solution Enigma.GitClient.slnx`: **2528 passed**, 0 failed, 0 skipped (1 new).
- Fix budget: no fix cycle.
