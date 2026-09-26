# FEATURE-F04E-PHASE02 — Escape leaves the diff at once

**Item:** FEATURE-F04E — Diffs take the whole page
**Branch:** `feature/feature-f04e-phase02-escape-closes-diff`
**Run:** feature/2026-09-20-ui-polish-diff-page

## Summary

Escape closes the diffs on the first press, with nothing clicked first — which is what the request
asked for and what no arrangement of key handlers alone could have delivered.

The reason the old dialog's Escape needed a click is **focus, not handlers**: a key event is routed
to whatever has the focus and then up through its ancestors. With the focus still on the commit
list — or nowhere at all, which is where a freshly shown window leaves it — the route never passed
through the diffs, so the key reached nothing until the reader happened to click inside them.

So the page does two things:

- **It moves the focus when the diffs open.** `DiffPage` is focusable, and the page focuses it when
  its state turns on, posted to the dispatcher because a panel that is still collapsed cannot take
  the focus. Closing gives the focus back to the commit list, so the arrow keys keep working where
  the reader came from.
- **It handles the key first.** `KeyDown` is handled at the page root with `RoutingStrategies.Tunnel`,
  so Escape leaves the diffs whatever inside them has the key — including the file filter box, which
  is exactly the kind of control that swallows a key it is offered. When the diffs are closed the
  key is left alone: Escape on the graph belongs to whatever the reader is using.

## Files / modules touched

**Modified — App**

- `Views/Pages/HistoryPageView.axaml` — `DiffPage` is `Focusable`
- `Views/Pages/HistoryPageView.axaml.cs` — the tunnelling `KeyDown` handler, the page-state watcher
  it needs, and `MoveFocus`, with the routing explained where the next reader will look for it

**Modified — tests**

- `tests/Enigma.GitClient.App.UnitTests/HistoryPageTests.cs` — four tests driving the real key
  through the headless window: the view takes the focus when it opens, Escape leaves it with no
  click first, Escape leaves it from inside the file filter box, and Escape on the graph itself does
  nothing and keeps the selection

**Modified — docs**

- `RELEASENOTES.md`, `docs/roadmap.md`, `docs/plan/FEATURE-F04E.md`

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| Why Escape needed a click | The focus, not the handler | A key goes to the focused element and routes up from there; nothing focused inside the diffs meant no route through them |
| Where the handler lives | The page root, tunnelling | It sees the key before any child can claim it, and it does not care which control inside the diffs has the focus |
| How the focus is moved | Posted to the dispatcher | The panel is collapsed until the layout pass that follows the state change, and an invisible control cannot take focus |
| Where the focus goes on the way out | Back to the commit list | The reader came from the graph and their next key is probably an arrow; leaving the focus on a hidden panel would strand it |
| Escape when the diffs are closed | Left alone | It belongs to whatever else is open — a context menu, a tooltip, the shell |
| How it is tested | `window.KeyPress` through the headless window | It is the only assertion that actually proves "no click first": a synthesised routed event would start wherever the test chose |

## Deviations & follow-ups

- **None from the plan.** All four acceptance criteria are covered.
- Restoring the focus to the commit list on the way out is one line beyond the plan's three steps.
  It costs nothing and it is the other half of the same idea: the focus should always be where the
  reader's next key belongs.
- **Line endings (recommendation only):** no CRLF churn in the touched files. No action taken.

## Build/test evidence

```
dotnet build Enigma.GitClient.slnx
  0 Warning(s)
  0 Error(s)

dotnet test --solution Enigma.GitClient.slnx
  Test run summary: Passed!
  total: 1747  failed: 0  succeeded: 1747  skipped: 0
```

Four tests added (1743 → 1747). One fix cycle, in the tests only: Avalonia 12's
`HeadlessWindowExtensions.KeyPress` takes the physical key and the text as required parameters.

## Documentation sweep

- `RELEASENOTES.md` — the graph bullet this item rewrote in PHASE01 now names Escape beside the back
  button, and says it works the moment the page opens, which was the complaint.
- `README.md` — says nothing about how the diffs are dismissed; unchanged.

There is no `CLAUDE.md`, `CHANGELOG.md` or `CONTRIBUTING.md` in the repository.
