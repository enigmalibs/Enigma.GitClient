# BUG-1A42-PHASE01 — A drag that offers something

**Item:** BUG-1A42 — The drag cursor still says no
**Branch:** `bugfix/bug-1a42-phase01-drag-payload`
**Run:** feature/2026-09-21-refs-tags-and-dragging

## Summary

A dragged branch row now carries the branch's full name as text, beside the in-process row it always
carried.

This is the fix for the cursor, and the reasoning matters because the previous attempt
(`BUG-876A-PHASE02`) changed the wrong thing. That phase made the page report
`DragDropEffects.Move` over the whole list, and the "no" pointer survived — because the page's
answer was never what the pointer was being drawn from. `BranchDrop.DragFormat` is an
**in-process** format, and `Avalonia.X11.Selections.DataFormatHelper.ToAtoms` skips every in-process
format when it publishes a drag's types: the drag took ownership of the drag selection while
advertising *no type at all*. A desktop that bridges an X drag onward then has nothing to offer
anyone, nothing can accept it, and the pointer says so for the entire gesture — while Avalonia
delivered the drag **in process**, never sending the protocol messages at all, which is exactly why
the ring, the auto-scroll and the drop all worked the whole time it looked impossible.

`BranchDrop.TransferFor` now builds both items. The in-process row is still what the drop reads —
a name alone would not say whether the branch is remote or checked out — and the name is what the
platform is offered. It is also the honest thing to advertise: drop a branch on a terminal or an
editor and its name is typed.

## Files / modules touched

**Modified — App**

- `ViewModels/Pages/BranchesPageViewModel.cs` — `BranchDrop.TransferFor`, with the reason the second
  item exists recorded on it
- `Views/Pages/BranchesPageView.axaml.cs` — the drag session starts from that factory instead of
  building the transfer inline

**Modified — tests**

- `tests/Enigma.GitClient.App.UnitTests/BranchesPageTests.cs` — the transfer carries the row under
  the in-process format and the full name as text, and its formats include one that is not
  in-process; a remote row carries `origin/…` and not the short name the row displays; and the
  in-process item still resolves the same pair, so the drop is untouched

**Modified — docs**

- `docs/roadmap.md`, `docs/plan/BUG-1A42.md`

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| Where the transfer is built | A static factory on `BranchDrop`, beside the format it pairs with | The two items only make sense together, and the view had no business knowing about either |
| What the test asserts about the platform | `Assert.Contains(transfer.Formats, format => format.Kind != DataFormatKind.InProcess)` | It is the property that actually matters — that *something* is publishable — rather than the name of a particular atom |
| The remote case | Its own test | `row.Name` is "published" and `row.FullName` is "origin/published"; carrying the wrong one would be a silent, plausible mistake |

## Deviations & follow-ups

- **None from the plan.** All four acceptance criteria are covered, with one honest limit stated
  below.
- **What the tests can and cannot say.** Headless Avalonia has no drag backend, so the platform drag
  session cannot be driven in a test and the cursor itself is not covered by one. What is covered is
  the condition the diagnosis turns on: the drag now advertises a format the platform can publish.
  The diagnosis itself came from reading `Avalonia.X11` 12.1.1 and from a headless probe of the real
  `DragDropDevice` against the real page, which showed the page answering `Move` at every point over
  the list — recorded in the plan file's *Context & constraints*.
- **Line endings (recommendation only):** no CRLF churn in the touched files. No action taken.

## Build/test evidence

```
dotnet build Enigma.GitClient.slnx
  0 Warning(s)
  0 Error(s)

dotnet test --solution Enigma.GitClient.slnx
  Test run summary: Passed!
  total: 1795  failed: 0  succeeded: 1795  skipped: 0
```

Three tests added (1792 → 1795). No fix cycle: green on the first run.

## Documentation sweep

`README.md` and `RELEASENOTES.md` describe the drop — the menu it opens, the merges it offers, the
list scrolling while a branch is held near an edge — and none of that changed. That a branch dropped
outside the window now types its name is new behaviour rather than a correction, so the sweep made
no edit. There is no `CLAUDE.md`, `CHANGELOG.md` or `CONTRIBUTING.md` in the repository.
