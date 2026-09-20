# Roadmap

Registry of every tracked work item in Enigma.GitClient. Full details live in `docs/plan/<ID>.md`;
completion records live in `docs/done/<ID>.md`.

| ID           | Title                                   | Status      | Plan                      |
|--------------|-----------------------------------------|-------------|---------------------------|
| FEATURE-7CFD | Solution foundation & git engine        | DONE        | docs/plan/FEATURE-7CFD.md |
| - PHASE01    | Solution scaffolding & config           | DONE        | (in FEATURE-7CFD.md)      |
| - PHASE02    | Git process runner & discovery          | DONE        | (in FEATURE-7CFD.md)      |
| - PHASE03    | Commit log reading & model              | DONE        | (in FEATURE-7CFD.md)      |
| - PHASE04    | Refs, branches, tags & HEAD state       | DONE        | (in FEATURE-7CFD.md)      |
| FEATURE-6DB0 | Core graph, diff & tree algorithms      | DONE        | docs/plan/FEATURE-6DB0.md |
| - PHASE01    | Commit graph lane layout                | DONE        | (in FEATURE-6DB0.md)      |
| - PHASE02    | Unified diff & word-level diff          | DONE        | (in FEATURE-6DB0.md)      |
| - PHASE03    | File path tree builder                  | DONE        | (in FEATURE-6DB0.md)      |
| FEATURE-52FB | App shell & repository opening          | DONE        | docs/plan/FEATURE-52FB.md |
| - PHASE01    | Avalonia shell, theme & DI              | DONE        | (in FEATURE-52FB.md)      |
| - PHASE02    | Open, init & clone repositories         | DONE        | (in FEATURE-52FB.md)      |
| FEATURE-2326 | Commit graph UI                         | DONE        | docs/plan/FEATURE-2326.md |
| - PHASE01    | Graph row rendering control             | DONE        | (in FEATURE-2326.md)      |
| - PHASE02    | History page & virtualisation           | DONE        | (in FEATURE-2326.md)      |
| FEATURE-7D1B | Commit details & diff viewer            | DONE        | docs/plan/FEATURE-7D1B.md |
| - PHASE01    | Changed files list/tree panel           | DONE        | (in FEATURE-7D1B.md)      |
| - PHASE02    | Colour-coded diff viewer                | DONE        | (in FEATURE-7D1B.md)      |
| FEATURE-478C | Branches, tags & checkout               | DONE        | docs/plan/FEATURE-478C.md |
| - PHASE01    | Branch management                       | DONE        | (in FEATURE-478C.md)      |
| - PHASE02    | Tags & checkout anything                | DONE        | (in FEATURE-478C.md)      |
| FEATURE-13FE | Working directory & commits             | DONE        | docs/plan/FEATURE-13FE.md |
| - PHASE01    | Status, staging & commit engine         | DONE        | (in FEATURE-13FE.md)      |
| - PHASE02    | Changes page & commit UI                | DONE        | (in FEATURE-13FE.md)      |
| FEATURE-06FE | Remotes & synchronisation               | DONE        | docs/plan/FEATURE-06FE.md |
| - PHASE01    | Fetch, pull, push & remotes             | DONE        | (in FEATURE-06FE.md)      |
| - PHASE02    | Stash management                        | DONE        | (in FEATURE-06FE.md)      |
| FEATURE-6DCC | Merge & conflict resolution             | DONE        | docs/plan/FEATURE-6DCC.md |
| - PHASE01    | Merge engine & conflict model           | DONE        | (in FEATURE-6DCC.md)      |
| - PHASE02    | Conflict resolution engine              | DONE        | (in FEATURE-6DCC.md)      |
| - PHASE03    | Conflict resolution UI                  | DONE        | (in FEATURE-6DCC.md)      |
| FEATURE-22C0 | Repository hosting integrations         | DONE        | docs/plan/FEATURE-22C0.md |
| - PHASE01    | Provider abstraction & tokens           | DONE        | (in FEATURE-22C0.md)      |
| - PHASE02    | GitHub provider                         | DONE        | (in FEATURE-22C0.md)      |
| - PHASE03    | GitLab & Azure DevOps providers         | DONE        | (in FEATURE-22C0.md)      |
| FEATURE-5D77 | Settings, preferences & docs            | DONE        | docs/plan/FEATURE-5D77.md |
| BUG-6CE6     | Details panel ignores the splitter      | DONE        | docs/plan/BUG-6CE6.md     |
| FEATURE-0183 | Bigger graph nodes, taller rows         | DONE        | docs/plan/FEATURE-0183.md |
| - PHASE01    | Double the graph node size              | DONE        | (in FEATURE-0183.md)      |
| - PHASE02    | Default row height of 36                | DONE        | (in FEATURE-0183.md)      |
| FEATURE-7676 | Diff panel defaults and typography      | DONE        | docs/plan/FEATURE-7676.md |
| - PHASE01    | Side by side by default                 | DONE        | (in FEATURE-7676.md)      |
| - PHASE02    | Configurable diff font                  | DONE        | (in FEATURE-7676.md)      |
| BUG-1D34     | Diff lines overlap the other pane       | DONE        | docs/plan/BUG-1D34.md     |
| - PHASE01    | An offsettable diff line                | DONE        | (in BUG-1D34.md)          |
| - PHASE02    | Clip the panes and scroll them          | DONE        | (in BUG-1D34.md)          |
| FEATURE-2FDF | More room between graph lanes           | DONE        | docs/plan/FEATURE-2FDF.md |
| FEATURE-2288 | Diffs in a dialog, not a panel          | DONE        | docs/plan/FEATURE-2288.md |
| - PHASE01    | The commit diff dialog                  | DONE        | (in FEATURE-2288.md)      |
| - PHASE02    | Reopen it from the row menu             | DONE        | (in FEATURE-2288.md)      |
| BUG-0DC2     | Scrolled diff text over the gutter      | DONE        | docs/plan/BUG-0DC2.md     |
| FEATURE-14E8 | History: diffs, badges and dragging     | DONE        | docs/plan/FEATURE-14E8.md |
| - PHASE01    | Open the diffs on demand                | DONE        | (in FEATURE-14E8.md)      |
| - PHASE02    | Branch badges in their own column       | DONE        | (in FEATURE-14E8.md)      |
| - PHASE03    | Merging by dropping a branch            | DONE        | (in FEATURE-14E8.md)      |
| - PHASE04    | Dragging branches in the graph          | DONE        | (in FEATURE-14E8.md)      |
| BUG-1AEA     | The diff dialog scrolls everything      | DONE        | docs/plan/BUG-1AEA.md     |
| FEATURE-295F | Diff viewer: sync, paths, full file     | DONE        | docs/plan/FEATURE-295F.md |
| - PHASE01    | Synchronised side-by-side scroll        | DONE        | (in FEATURE-295F.md)      |
| - PHASE02    | The whole file, side by side            | DONE        | (in FEATURE-295F.md)      |
| - PHASE03    | No path beside the file name            | DONE        | (in FEATURE-295F.md)      |
| FEATURE-3030 | History list: columns, menus, search    | DONE        | docs/plan/FEATURE-3030.md |
| - PHASE01    | Right-click anywhere on the row         | DONE        | (in FEATURE-3030.md)      |
| - PHASE02    | Real columns with a resizable header    | DONE        | (in FEATURE-3030.md)      |
| - PHASE03    | No dragging from the history badges     | DONE        | (in FEATURE-3030.md)      |
| - PHASE04    | Merge nodes half a commit's size        | DONE        | (in FEATURE-3030.md)      |
| - PHASE05    | Search highlights instead of filters    | DONE        | (in FEATURE-3030.md)      |
| FEATURE-5EC4 | A minimap scrollbar for diffs           | DONE        | docs/plan/FEATURE-5EC4.md |
| - PHASE01    | The change map and its control          | DONE        | (in FEATURE-5EC4.md)      |
| - PHASE02    | The minimap replaces the scrollbar      | DONE        | (in FEATURE-5EC4.md)      |
| FEATURE-3B62 | Selectable rows and branch drops        | DONE        | docs/plan/FEATURE-3B62.md |
| - PHASE01    | Selectable branch and tag rows          | DONE        | (in FEATURE-3B62.md)      |
| - PHASE02    | Selectable remote rows                  | DONE        | (in FEATURE-3B62.md)      |
| - PHASE03    | Dropping one branch onto another        | DONE        | (in FEATURE-3B62.md)      |
| FEATURE-0DB4 | Bigger icons, readable selected rows    | DONE        | docs/plan/FEATURE-0DB4.md |
| - PHASE01    | One icon scale for the whole app        | DONE        | (in FEATURE-0DB4.md)      |
| - PHASE02    | Readable text on a selected row         | DONE        | (in FEATURE-0DB4.md)      |
| BUG-3BAE     | Search matches are not highlighted      | DONE        | docs/plan/BUG-3BAE.md     |
| FEATURE-F04E | Diffs take the whole page               | DONE        | docs/plan/FEATURE-F04E.md |
| - PHASE01    | The diff view replaces the history      | DONE        | (in FEATURE-F04E.md)      |
| - PHASE02    | Escape leaves the diff at once          | DONE        | (in FEATURE-F04E.md)      |
| FEATURE-B14C | A wider minimap that finds the change   | DONE        | docs/plan/FEATURE-B14C.md |
| - PHASE01    | A minimap wide enough to grab           | DONE        | (in FEATURE-B14C.md)      |
| - PHASE02    | Opening at the first change             | DONE        | (in FEATURE-B14C.md)      |
| BUG-876A     | Branch rows: selection and dragging     | DONE        | docs/plan/BUG-876A.md     |
| - PHASE01    | Deselected rows go back to normal       | DONE        | (in BUG-876A.md)          |
| - PHASE02    | The drag cursor says yes                | DONE        | (in BUG-876A.md)          |
| - PHASE03    | The list scrolls while dragging         | DONE        | (in BUG-876A.md)          |
| FEATURE-494B | History refs: full names, bigger badges | DONE        | docs/plan/FEATURE-494B.md |
| - PHASE01    | Branch names in full, never trimmed     | DONE        | (in FEATURE-494B.md)      |
| - PHASE02    | A roomier badge, a size bigger          | DONE        | (in FEATURE-494B.md)      |
| BUG-36A9     | A found row cannot be hovered           | DONE        | docs/plan/BUG-36A9.md     |
| FEATURE-0FBE | Tags get their own page                 | IN PROGRESS | docs/plan/FEATURE-0FBE.md |
| - PHASE01    | The tags page and its rail item         | DONE        | (in FEATURE-0FBE.md)      |
| - PHASE02    | The branches page is only branches      | TODO        | (in FEATURE-0FBE.md)      |
| BUG-1A42     | The drag cursor still says no           | TODO        | docs/plan/BUG-1A42.md     |
| - PHASE01    | A drag that offers something            | TODO        | (in BUG-1A42.md)          |
| - PHASE02    | Every drag event gets an answer         | TODO        | (in BUG-1A42.md)          |
