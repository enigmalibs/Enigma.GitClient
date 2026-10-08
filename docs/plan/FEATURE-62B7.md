# FEATURE-62B7 — A simpler About dialog

**Status:** DONE — see `docs/done/FEATURE-62B7.md`
**Type:** FEATURE
**Branch:** `feature/feature-62b7-simple-about`
**Run:** vibe/2026-10-08-file-panel-about-release

## Objective

The About dialog becomes a plain box:

- the application's icon;
- its name;
- the version;
- the build;
- the copyright;
- a **Close** button at the bottom right.

The "About" title, the Info icon under it, and the *BUILT WITH* box are gone.

## Context & constraints

- `AboutDialogService` shows `AboutView` in the window's `ContentDialog`. It sets `Title = "About"`
  and `IconData` to Phosphor's Info glyph. The card draws those two as a column on the left of the
  content: that column is what the report wants gone.
- The tool dialogs (`ToolDialogService`) and the host repository browser already show title-less
  cards with `Title = null` and no icon.
- `AboutView` draws, in order:
  - the 96 px icon;
  - the product name;
  - "Version X";
  - the build line;
  - the copyright;
  - a *BUILT WITH* box listing `AboutViewModel.DefaultCredits`.
- The *Close* button is the dialog's only button, and the card puts it at the bottom right.
- `AboutDialogTests` pins today's shape:
  - the credits list;
  - "BUILT WITH";
  - `Title == "About"` and a non-null icon;
  - the four-argument view-model constructor.

## Steps

1. `AboutDialogService`:
   - `Title = null`;
   - no `IconData` (the static `Icon` field and its Phosphor usings go);
   - `CloseButtonText = "Close"` and `DefaultButton = DefaultButton.Close`.
2. `AboutView.axaml`: the *BUILT WITH* border and its three styles (`caption`, `credit`, `license`)
   are removed. Everything else stays as it is.
3. `AboutViewModel`:
   - `CreditEntry`, `DefaultCredits` and `Credits` are removed;
   - the testing constructor becomes `(version, buildSha, copyright)`;
   - the doc comments say what the dialog shows now.
4. `AboutDialogTests`:
   - the view model and the view show the name, version, build and copyright, and no credits;
   - the service shows the view with no title and no icon, and one *Close* button;
   - in a real window's dialog host, no "About" title and no icon are drawn, the content starts at
     the card's left padding (no column), nothing scrolls, and the *Close* button is right-aligned
     at the bottom;
   - the existing render test keeps drawing the view.

## Acceptance criteria

- The dialog shows the icon, the name, the version, the build line (when there is a revision), the
  copyright and a *Close* button, and nothing else.
- No title and no Info icon on the card; no *BUILT WITH* box.
- *Close* closes it, from every place it opens from: the repository toolbar, Settings, and the start
  window's home.
- Build clean with zero warnings; the affected suites are green.

## Out of scope

- Restyling the remaining lines.
- A third-party notices file. The in-app list goes, and the follow-up is noted in the completion doc.

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| Removing the title and icon | `Title = null`, no `IconData`: the tool dialogs' own pattern | That removes the left column, with no library change | Re-templating the card |
| The product name | Kept, above the version | Not among the explicit removals (title, icon, BUILT WITH); an About box that does not name its product reads as broken | Removing it to match the list word for word |
| The credits code | Deleted (`CreditEntry`, `DefaultCredits`, `Credits`) | Nothing would show it any more | Keeping it unused |
| Attribution | Recorded as a follow-up: a THIRD-PARTY-NOTICES file shipped with the app | The list carried names and licence ids only, never the notices MIT asks to be kept, so removing it changes little; a notices file is the proper home | Keeping the list somewhere else in the UI |
| Default button | *Close* | It is the only button; Enter closes the box | None |
