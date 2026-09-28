# FEATURE-A2A2-PHASE01 — The placeholder says 1.0.0

**Item:** FEATURE-A2A2 — Tags: bare placeholder, push from menus
**Branch:** `feature/feature-a2a2-phase01-bare-tag-placeholder`
**Run:** bugfix/2026-09-28-history-tag-push-release

## Summary

The *Create a tag* dialog's name box suggests `1.0.0` instead of `v1.0.0`, matching the bare `X.Y.Z`
tags the project's own releases use. It is only a suggestion: a name with a `v` is accepted as before.

## Files / modules touched

**Created**

- `docs/done/FEATURE-A2A2-PHASE01.md`

**Modified**

- `src/Enigma.GitClient.App/Views/Dialogs/CreateTagDialogView.axaml` — `PlaceholderText="1.0.0"`.
- `tests/Enigma.GitClient.App.UnitTests/TagsAndCheckoutTests.cs` —
  `CreateTagDialog_SuggestsABareVersion_AndStillAcceptsOneWithAV`: the dialog's name box, found by its
  automation name, suggests `1.0.0`, and `v1.0.0` still validates.
- `docs/roadmap.md`, `docs/plan/FEATURE-A2A2.md` — statuses.

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| How the test finds the name box | By its automation name, "Tag name" | The view names no control. The automation name is part of what the view promises, so the test does not depend on the box's position in the form |

## Deviations & follow-ups

- None.
- Line endings: no CRLF churn.

## Documentation sweep

The README's *Tag management — create (lightweight or annotated) and delete* stays true. No edit.

## Build/test evidence

- `dotnet build Enigma.GitClient.slnx`: 0 warnings, 0 errors.
- `dotnet test --solution Enigma.GitClient.slnx`: **2496 passed**, 0 failed, 0 skipped (1 new).
- Fix budget: no fix cycle.
