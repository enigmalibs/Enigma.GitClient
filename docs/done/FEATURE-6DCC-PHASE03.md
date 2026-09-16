# FEATURE-6DCC-PHASE03 — Conflict resolution UI

**Item:** FEATURE-6DCC — Merge & conflict resolution
**Branch:** `feature/feature-6dcc-phase03-conflict-ui`
**Run:** feature/2026-09-15-enigma-git-client

## Summary

A merge that stops on conflicts now has a page to finish it on. The rail grows a **Conflicts** entry
the moment a merge conflicts and loses it again when the merge ends, so the page exists exactly when
it is useful; the shell's banner says how far the merge has got ("2 of 5 files resolved"), offers the
way to the page, and holds a **Commit the merge** button that stays disabled until nothing is left.

The page lists the conflicted files with the kind of conflict each one has, and opens on the first
one still needing work. The selected file is shown as its regions in order: text both sides agree on
is context, and each disagreement is a card with **ours**, the **original** and **theirs** side by
side, tinted by which side they came from, with the six ways of settling it beside them — take ours,
take theirs, take both in either order, take the original, or write the region yourself in a box
that opens on what is currently chosen.

Below it, the preview shows the exact file that will be written, rendered by the same method that
writes it, with anything still undecided marked in place by git's own markers. Saving writes it and
stages it; the file stays in the list with a **Resolved** pill so the count above still means
something. A file with no line-by-line answer — a binary file, or one side that deleted it — is
offered as the whole-file choice it actually is, and the button says so: "Keep theirs (delete the
file)".

`Alt+←` and `Alt+→` take ours and theirs for the region the reader is on, `Alt+↓` moves to the next
one still undecided and comes back round at the end.

## Files / modules touched

**Created — App**

- `ViewModels/Pages/ConflictResolutionPageViewModel.cs` — `ConflictLineViewModel`,
  `ConflictRegionViewModel`, `ConflictFileRowViewModel` and the page itself
- `Views/Pages/ConflictResolutionPageView.axaml` (+ `.axaml.cs`) — the file list, the region cards,
  the whole-file card, the preview pane and the key bindings

**Modified — App**

- `Navigation/ShellNavigation.cs` — `ShellPage.Conflicts` and `SetConflictsVisible`
- `ViewModels/MainWindowViewModel.cs` — the page as a property, `ResolveConflictsCommand`, and
  showing or hiding the rail entry as the merge starts and ends
- `Views/MainWindow.axaml` — the banner's progress, Resolve, Abandon and Commit buttons
- `Themes/Graph.axaml` — eleven conflict colours in both variants, and their brushes
- `Themes/Styles.axaml` — the card, the three side panes, the side headings, the state pills, the
  choice buttons and the preview box
- `DependencyInjection/ServiceCollectionExtensions.cs`

**Modified — Core**

- `Merging/ConflictService.cs` — nothing new; PHASE02's `ResolveAllWithAsync` is what the page's
  "Keep ours everywhere" runs

**Created — tests**

- `tests/Enigma.GitClient.App.UnitTests/ConflictResolutionPageTests.cs` — 25 cases against real
  conflicting merges: the file list, the regions git produced, each of the six choices and what they
  do to the preview, the editor, the bulk and per-file commands, saving, committing, abandoning, the
  rail entry appearing and going away, the banner's counters, and three rendered frames (dark, light
  and the editor open) whose pixels are counted

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| The three panes | One list of region cards, each with three columns | The plan asked for three panes scrolled in sync by region. Three columns of one grid are aligned by construction — there is no scroll handler to drift — and a region's controls sit beside the lines they decide rather than in a gutter between panes |
| Where the page lives | A rail entry added when a merge conflicts and removed when it ends | A page that is only meaningful during a merge has no place in a permanent rail, and a greyed-out entry teaches nothing. Appearing is also how the user finds out the page exists |
| Leaving the page as the merge ends | Navigate to the history first, then remove the entry | Removing the selected item leaves the rail with a selection it no longer has and the page still on screen |
| The banner's counters | Bound straight through to the page's ViewModel | The banner has to be right before the page has ever been opened; a second copy of the same counters is a second thing to keep in step |
| When the file is written | Only on **Save and mark resolved** | Every choice changes the preview and nothing else, so a half-made decision never reaches the work tree. The button is disabled until every region is decided |
| A resolved file | Stays in the list with a pill | Removing it would make "3 of 5 files resolved" impossible to say, and the list would shrink under the reader as they work |
| Committing before the conflicts have been read | Refused | An empty list means "nothing has looked yet" as often as "nothing is left", and the two must not offer the same button. The page has to have read the conflicts at least once |
| Line numbers in the panes | Each side numbered as its own file | That is what each pane is showing: the numbers a reader would find if they opened that version. Numbering every block from one was noise |
| The base column's width | A fixed range, not `Auto` | An `Auto` column is a different width in every card, and three panes that do not line up between regions are three panes the eye has to re-find each time |
| The pane tints | The graph's first and fourth lane hues | Already established in this app as "different thing", told apart under the common forms of colour blindness, and neither is the red/green the diff viewer has already spent |
| Where a whole-file conflict goes | The same page, as a card with the two choices | It is still a conflict in the same merge; sending it somewhere else would split one task across two places |

## Deviations & follow-ups

- **Deviation:** the plan's three panes are three columns of one region card rather than three
  independently scrolling panes, for the reason in the table above. The panes, their headings, their
  tints and the per-region controls between them are all there.
- **Deviation:** the plan's acceptance criteria asked for "unit tests with a faked service". The
  tests drive real conflicting merges instead, as every other App test in this repository does — a
  fake would only pin that the page can render a document the test wrote for it, not that it shows
  what git actually left behind.
- **Follow-up:** selecting a file that was resolved earlier shows a card saying so rather than the
  resulting file. Showing it would mean reading the work-tree file, and undoing a resolution would
  mean `git checkout --merge`, which is a Core operation this phase did not need.
- **Follow-up:** the merge dialog PHASE01 left behind — `--no-ff`, `--squash` and `--no-commit` are
  modelled and tested but nothing offers them — is still a follow-up. The conflicts page is about
  finishing a merge, not starting one.
- **Fixed while building:** a region's editor was a `DockPanel` child with no dock, which docks left
  and keeps its own width; it now shares a cell with the three panes and spans the card. A rendered
  frame is what caught it, and a test now measures the box.
- **Line endings (recommendation only):** no CRLF churn observed. No action taken.

## Build/test evidence

```
dotnet build Enigma.GitClient.slnx --no-incremental
  0 Warning(s)
  0 Error(s)

dotnet test --solution Enigma.GitClient.slnx
  Test run summary: Passed!
  total: 1286  failed: 0  succeeded: 1286  skipped: 0
```

25 tests are new in this dev. Three of them render the page off-screen and save the frame
(`snapshots/conflict-page-dark.png`, `-light.png`, `-editing.png`); those frames were opened and
looked at, which is how the base column's wandering width and the editor's docking were found.
