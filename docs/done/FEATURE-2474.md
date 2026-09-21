# FEATURE-2474 — Row action icons stay readable

**Item:** FEATURE-2474 — Row action icons stay readable
**Branch:** `feature/feature-2474-row-action-icons`
**Run:** feature/2026-09-21-icons-tracking-dragging

## Summary

The icon-only actions at the end of a list row — check out, delete, fetch, edit, remove, pin — are
drawn in the full foreground, so they are legible on an ordinary row and on the selected one.

One rule, because one thing was wrong. A row action is a `Button.toolbar` holding an `ei:Icon.row`;
the button's style sets the secondary grey as its foreground and the icon, stating none of its own,
**inherits** it. `Styles.axaml` already raises quiet things on a selected row, but only those carrying
`dim` or `faint` — which a row action's icon does not — so no rule had ever applied to it: on the
selection plate it was grey on grey. An inherited value loses to a style setter on the element itself,
so a selector on the pair settles every state at once.

The pair is also what tells a row action from a header button without inventing a class: a header's
button holds an `ei:Icon.toolbar`, a row's holds an `ei:Icon.row`, everywhere in the application.

## Files / modules touched

**Modified — App**

- `Themes/Styles.axaml` — a `Button.toolbar ei|Icon.row` rule setting the full foreground, with the
  reasoning recorded on it

**Modified — tests**

- `tests/Enigma.GitClient.App.UnitTests/ShellRenderTests.cs` — the pair in a real `ListBox`: an action
  icon is at full strength on the selected row and on the others; and a row's leading `dim` glyph is
  still quiet beside an action that is not
- `tests/Enigma.GitClient.App.UnitTests/BranchesPageTests.cs` — the real branch rows' two actions, on
  the selected row and on an unselected one
- `tests/Enigma.GitClient.App.UnitTests/TagsPageTests.cs` — the same for the tags page
- `tests/Enigma.GitClient.App.UnitTests/RemotesAndSyncTests.cs` — the same for the remotes page's three

**Modified — docs**

- `docs/roadmap.md`, `docs/plan/FEATURE-2474.md`

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| Where the page-level tests assert | On the realised `ListBoxItem`s of the real pages, through the real templates | A synthetic pair proves the rule fires; only the real rows prove the selector matches the markup those pages actually use |
| How an action is found in a row | The `Icon` descendants of the row's `Button`s carrying `toolbar` | It is the selector the rule uses, expressed as a query — if one of them stops being a toolbar button the test says so |
| Whether to also assert the leading glyph | Yes, in `ShellRenderTests` | It is the guard against the rule flattening the row: the fix is only right if the quiet glyph stayed quiet |

## Deviations & follow-ups

- **None from the plan.** All four acceptance criteria are met.
- The repositories page gets the same improvement for free — its pin and remove buttons are the same
  pair. It was not in the request and is not covered by a test of its own; noted rather than claimed.
- **Line endings (recommendation only):** no CRLF churn in the touched files. No action taken.

## Build/test evidence

```
dotnet build Enigma.GitClient.slnx
  0 Warning(s)
  0 Error(s)

dotnet test --solution Enigma.GitClient.slnx
  Test run summary: Passed!
  total: 1808  failed: 0  succeeded: 1808  skipped: 0
```

Five tests added (1803 → 1808). No fix cycle: green on the first run. All five were also run against
the tree with the new rule commented out, where all five failed and the other 1803 passed — so they
test the fix and nothing else.

## Documentation sweep

`README.md` and `RELEASENOTES.md` describe what the lists do, never how they are painted; nothing in
either became wrong. There is no `CLAUDE.md`, `CHANGELOG.md` or `CONTRIBUTING.md` in the repository. No
edits.
