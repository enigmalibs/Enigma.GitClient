# Roadmap

Registry of every tracked work item in Enigma.GitClient. Full details live in `docs/plan/<ID>.md`;
completion records live in `docs/done/<ID>.md`.

| ID           | Title                                   | Status    | Plan                      |
|--------------|-----------------------------------------|-----------|---------------------------|
| FEATURE-7CFD | Solution foundation & git engine        | DONE      | docs/plan/FEATURE-7CFD.md |
| - PHASE01    | Solution scaffolding & config           | DONE      | (in FEATURE-7CFD.md)      |
| - PHASE02    | Git process runner & discovery          | DONE      | (in FEATURE-7CFD.md)      |
| - PHASE03    | Commit log reading & model              | DONE      | (in FEATURE-7CFD.md)      |
| - PHASE04    | Refs, branches, tags & HEAD state       | DONE      | (in FEATURE-7CFD.md)      |
| FEATURE-6DB0 | Core graph, diff & tree algorithms      | DONE      | docs/plan/FEATURE-6DB0.md |
| - PHASE01    | Commit graph lane layout                | DONE      | (in FEATURE-6DB0.md)      |
| - PHASE02    | Unified diff & word-level diff          | DONE      | (in FEATURE-6DB0.md)      |
| - PHASE03    | File path tree builder                  | DONE      | (in FEATURE-6DB0.md)      |
| FEATURE-52FB | App shell & repository opening          | DONE      | docs/plan/FEATURE-52FB.md |
| - PHASE01    | Avalonia shell, theme & DI              | DONE      | (in FEATURE-52FB.md)      |
| - PHASE02    | Open, init & clone repositories         | DONE      | (in FEATURE-52FB.md)      |
| FEATURE-2326 | Commit graph UI                         | DONE      | docs/plan/FEATURE-2326.md |
| - PHASE01    | Graph row rendering control             | DONE      | (in FEATURE-2326.md)      |
| - PHASE02    | History page & virtualisation           | DONE      | (in FEATURE-2326.md)      |
| FEATURE-7D1B | Commit details & diff viewer            | DONE      | docs/plan/FEATURE-7D1B.md |
| - PHASE01    | Changed files list/tree panel           | DONE      | (in FEATURE-7D1B.md)      |
| - PHASE02    | Colour-coded diff viewer                | DONE      | (in FEATURE-7D1B.md)      |
| FEATURE-478C | Branches, tags & checkout               | DONE      | docs/plan/FEATURE-478C.md |
| - PHASE01    | Branch management                       | DONE      | (in FEATURE-478C.md)      |
| - PHASE02    | Tags & checkout anything                | DONE      | (in FEATURE-478C.md)      |
| FEATURE-13FE | Working directory & commits             | DONE      | docs/plan/FEATURE-13FE.md |
| - PHASE01    | Status, staging & commit engine         | DONE      | (in FEATURE-13FE.md)      |
| - PHASE02    | Changes page & commit UI                | DONE      | (in FEATURE-13FE.md)      |
| FEATURE-06FE | Remotes & synchronisation               | DONE      | docs/plan/FEATURE-06FE.md |
| - PHASE01    | Fetch, pull, push & remotes             | DONE      | (in FEATURE-06FE.md)      |
| - PHASE02    | Stash management                        | DONE      | (in FEATURE-06FE.md)      |
| FEATURE-6DCC | Merge & conflict resolution             | DONE      | docs/plan/FEATURE-6DCC.md |
| - PHASE01    | Merge engine & conflict model           | DONE      | (in FEATURE-6DCC.md)      |
| - PHASE02    | Conflict resolution engine              | DONE      | (in FEATURE-6DCC.md)      |
| - PHASE03    | Conflict resolution UI                  | DONE      | (in FEATURE-6DCC.md)      |
| FEATURE-22C0 | Repository hosting integrations         | DONE      | docs/plan/FEATURE-22C0.md |
| - PHASE01    | Provider abstraction & tokens           | DONE      | (in FEATURE-22C0.md)      |
| - PHASE02    | GitHub provider                         | DONE      | (in FEATURE-22C0.md)      |
| - PHASE03    | GitLab & Azure DevOps providers         | DONE      | (in FEATURE-22C0.md)      |
| FEATURE-5D77 | Settings, preferences & docs            | DONE      | docs/plan/FEATURE-5D77.md |
| BUG-6CE6     | Details panel ignores the splitter      | DONE      | docs/plan/BUG-6CE6.md     |
| FEATURE-0183 | Bigger graph nodes, taller rows         | DONE      | docs/plan/FEATURE-0183.md |
| - PHASE01    | Double the graph node size              | DONE      | (in FEATURE-0183.md)      |
| - PHASE02    | Default row height of 36                | DONE      | (in FEATURE-0183.md)      |
| FEATURE-7676 | Diff panel defaults and typography      | DONE      | docs/plan/FEATURE-7676.md |
| - PHASE01    | Side by side by default                 | DONE      | (in FEATURE-7676.md)      |
| - PHASE02    | Configurable diff font                  | DONE      | (in FEATURE-7676.md)      |
| BUG-1D34     | Diff lines overlap the other pane       | DONE      | docs/plan/BUG-1D34.md     |
| - PHASE01    | An offsettable diff line                | DONE      | (in BUG-1D34.md)          |
| - PHASE02    | Clip the panes and scroll them          | DONE      | (in BUG-1D34.md)          |
| FEATURE-2FDF | More room between graph lanes           | DONE      | docs/plan/FEATURE-2FDF.md |
| FEATURE-2288 | Diffs in a dialog, not a panel          | DONE      | docs/plan/FEATURE-2288.md |
| - PHASE01    | The commit diff dialog                  | DONE      | (in FEATURE-2288.md)      |
| - PHASE02    | Reopen it from the row menu             | DONE      | (in FEATURE-2288.md)      |
| BUG-0DC2     | Scrolled diff text over the gutter      | DONE      | docs/plan/BUG-0DC2.md     |
| FEATURE-14E8 | History: diffs, badges and dragging     | DONE      | docs/plan/FEATURE-14E8.md |
| - PHASE01    | Open the diffs on demand                | DONE      | (in FEATURE-14E8.md)      |
| - PHASE02    | Branch badges in their own column       | DONE      | (in FEATURE-14E8.md)      |
| - PHASE03    | Merging by dropping a branch            | DONE      | (in FEATURE-14E8.md)      |
| - PHASE04    | Dragging branches in the graph          | DONE      | (in FEATURE-14E8.md)      |
| BUG-1AEA     | The diff dialog scrolls everything      | DONE      | docs/plan/BUG-1AEA.md     |
| FEATURE-295F | Diff viewer: sync, paths, full file     | DONE      | docs/plan/FEATURE-295F.md |
| - PHASE01    | Synchronised side-by-side scroll        | DONE      | (in FEATURE-295F.md)      |
| - PHASE02    | The whole file, side by side            | DONE      | (in FEATURE-295F.md)      |
| - PHASE03    | No path beside the file name            | DONE      | (in FEATURE-295F.md)      |
| FEATURE-3030 | History list: columns, menus, search    | DONE      | docs/plan/FEATURE-3030.md |
| - PHASE01    | Right-click anywhere on the row         | DONE      | (in FEATURE-3030.md)      |
| - PHASE02    | Real columns with a resizable header    | DONE      | (in FEATURE-3030.md)      |
| - PHASE03    | No dragging from the history badges     | DONE      | (in FEATURE-3030.md)      |
| - PHASE04    | Merge nodes half a commit's size        | DONE      | (in FEATURE-3030.md)      |
| - PHASE05    | Search highlights instead of filters    | DONE      | (in FEATURE-3030.md)      |
| FEATURE-5EC4 | A minimap scrollbar for diffs           | DONE      | docs/plan/FEATURE-5EC4.md |
| - PHASE01    | The change map and its control          | DONE      | (in FEATURE-5EC4.md)      |
| - PHASE02    | The minimap replaces the scrollbar      | DONE      | (in FEATURE-5EC4.md)      |
| FEATURE-3B62 | Selectable rows and branch drops        | DONE      | docs/plan/FEATURE-3B62.md |
| - PHASE01    | Selectable branch and tag rows          | DONE      | (in FEATURE-3B62.md)      |
| - PHASE02    | Selectable remote rows                  | DONE      | (in FEATURE-3B62.md)      |
| - PHASE03    | Dropping one branch onto another        | DONE      | (in FEATURE-3B62.md)      |
| FEATURE-0DB4 | Bigger icons, readable selected rows    | DONE      | docs/plan/FEATURE-0DB4.md |
| - PHASE01    | One icon scale for the whole app        | DONE      | (in FEATURE-0DB4.md)      |
| - PHASE02    | Readable text on a selected row         | DONE      | (in FEATURE-0DB4.md)      |
| BUG-3BAE     | Search matches are not highlighted      | DONE      | docs/plan/BUG-3BAE.md     |
| FEATURE-F04E | Diffs take the whole page               | DONE      | docs/plan/FEATURE-F04E.md |
| - PHASE01    | The diff view replaces the history      | DONE      | (in FEATURE-F04E.md)      |
| - PHASE02    | Escape leaves the diff at once          | DONE      | (in FEATURE-F04E.md)      |
| FEATURE-B14C | A wider minimap that finds the change   | DONE      | docs/plan/FEATURE-B14C.md |
| - PHASE01    | A minimap wide enough to grab           | DONE      | (in FEATURE-B14C.md)      |
| - PHASE02    | Opening at the first change             | DONE      | (in FEATURE-B14C.md)      |
| BUG-876A     | Branch rows: selection and dragging     | DONE      | docs/plan/BUG-876A.md     |
| - PHASE01    | Deselected rows go back to normal       | DONE      | (in BUG-876A.md)          |
| - PHASE02    | The drag cursor says yes                | DONE      | (in BUG-876A.md)          |
| - PHASE03    | The list scrolls while dragging         | DONE      | (in BUG-876A.md)          |
| FEATURE-494B | History refs: full names, bigger badges | DONE      | docs/plan/FEATURE-494B.md |
| - PHASE01    | Branch names in full, never trimmed     | DONE      | (in FEATURE-494B.md)      |
| - PHASE02    | A roomier badge, a size bigger          | DONE      | (in FEATURE-494B.md)      |
| BUG-36A9     | A found row cannot be hovered           | DONE      | docs/plan/BUG-36A9.md     |
| FEATURE-0FBE | Tags get their own page                 | DONE      | docs/plan/FEATURE-0FBE.md |
| - PHASE01    | The tags page and its rail item         | DONE      | (in FEATURE-0FBE.md)      |
| - PHASE02    | The branches page is only branches      | DONE      | (in FEATURE-0FBE.md)      |
| BUG-1A42     | The drag cursor still says no           | DONE      | docs/plan/BUG-1A42.md     |
| - PHASE01    | A drag that offers something            | DONE      | (in BUG-1A42.md)          |
| - PHASE02    | Every drag event gets an answer         | DONE      | (in BUG-1A42.md)          |
| FEATURE-2474 | Row action icons stay readable          | DONE      | docs/plan/FEATURE-2474.md |
| FEATURE-0667 | Branch rows say where they stand        | DONE      | docs/plan/FEATURE-0667.md |
| - PHASE01    | What a local branch knows of its remote | DONE      | (in FEATURE-0667.md)      |
| - PHASE02    | Arrows, counts and a remote state       | DONE      | (in FEATURE-0667.md)      |
| BUG-11A4     | A drag the compositor cannot refuse     | DONE      | docs/plan/BUG-11A4.md     |
| - PHASE01    | A drag the page runs itself             | DONE      | (in BUG-11A4.md)          |
| - PHASE02    | A pointer that says yes, and a way out  | DONE      | (in BUG-11A4.md)          |
| BUG-39D9     | Progress reports arrive late, unordered | DONE      | docs/plan/BUG-39D9.md     |
| FEATURE-5431 | A start window, one repo per window     | DONE      | docs/plan/FEATURE-5431.md |
| - PHASE01    | Start window and repository window      | DONE      | (in FEATURE-5431.md)      |
| - PHASE02    | Several instances side by side          | DONE      | (in FEATURE-5431.md)      |
| FEATURE-7514 | Branches, tags and remotes as dialogs   | DONE      | docs/plan/FEATURE-7514.md |
| - PHASE01    | Dialogs from the history toolbar        | DONE      | (in FEATURE-7514.md)      |
| - PHASE02    | A manual merge section                  | DONE      | (in FEATURE-7514.md)      |
| FEATURE-3507 | Branch actions in the history           | DONE      | docs/plan/FEATURE-3507.md |
| - PHASE01    | Merge source and merge into             | DONE      | (in FEATURE-3507.md)      |
| - PHASE02    | Pull, push and delete per branch        | DONE      | (in FEATURE-3507.md)      |
| FEATURE-1296 | Automatic fetch and refresh             | DONE      | docs/plan/FEATURE-1296.md |
| - PHASE01    | The interval setting                    | DONE      | (in FEATURE-1296.md)      |
| - PHASE02    | The periodic fetch and refresh          | DONE      | (in FEATURE-1296.md)      |
| BUG-45D9     | Uncommitted row shown twice             | DONE      | docs/plan/BUG-45D9.md     |
| FEATURE-1A7E | Reset the branch to a commit            | DONE      | docs/plan/FEATURE-1A7E.md |
| - PHASE01    | The reset engine                        | DONE      | (in FEATURE-1A7E.md)      |
| - PHASE02    | Soft and hard reset in the line menu    | DONE      | (in FEATURE-1A7E.md)      |
| FEATURE-7232 | Toolbars, one refresh, flat file lists  | DONE      | docs/plan/FEATURE-7232.md |
| - PHASE01    | Toolbar buttons that look enabled       | DONE      | (in FEATURE-7232.md)      |
| - PHASE02    | One refresh for everything              | DONE      | (in FEATURE-7232.md)      |
| - PHASE03    | Flat file lists by default              | DONE      | (in FEATURE-7232.md)      |
| BUG-449E     | Theme toggle is forgotten on restart    | DONE      | docs/plan/BUG-449E.md     |
| BUG-1840     | Long context menu items are clipped     | DONE      | docs/plan/BUG-1840.md     |
| FEATURE-6151 | Git identity: global, profiles, local   | DONE      | docs/plan/FEATURE-6151.md |
| - PHASE01    | Read and write the git identity         | DONE      | (in FEATURE-6151.md)      |
| - PHASE02    | The identity page, in both windows      | DONE      | (in FEATURE-6151.md)      |
| - PHASE03    | Identity profiles                       | DONE      | (in FEATURE-6151.md)      |
| - PHASE04    | A repository's own identity             | DONE      | (in FEATURE-6151.md)      |
| FEATURE-F873 | Tool dialogs on the window background   | DONE      | docs/plan/FEATURE-F873.md |
| FEATURE-75F4 | A splash screen and an About box        | DONE      | docs/plan/FEATURE-75F4.md |
| - PHASE01    | The splash screen                       | DONE      | (in FEATURE-75F4.md)      |
| - PHASE02    | The About dialog                        | DONE      | (in FEATURE-75F4.md)      |
| FEATURE-92A3 | History: no scope, no first parent      | DONE      | docs/plan/FEATURE-92A3.md |
| - PHASE01    | No branch-scope selector                | DONE      | (in FEATURE-92A3.md)      |
| - PHASE02    | No first-parent history                 | DONE      | (in FEATURE-92A3.md)      |
| FEATURE-70C1 | Hide branches from the history          | DONE      | docs/plan/FEATURE-70C1.md |
| - PHASE01    | Leaving refs out of the walk            | DONE      | (in FEATURE-70C1.md)      |
| - PHASE02    | Remembering hidden branches             | DONE      | (in FEATURE-70C1.md)      |
| - PHASE03    | Show and hide from the branches         | DONE      | (in FEATURE-70C1.md)      |
| FEATURE-B4C0 | A Linux installer                       | DONE      | docs/plan/FEATURE-B4C0.md |
| FEATURE-2B7B | Release 1.0.0                           | DONE      | docs/plan/FEATURE-2B7B.md |
| BUG-39CC     | Merge fast-forwards instead of merging  | DONE      | docs/plan/BUG-39CC.md     |
| FEATURE-A5D3 | Info bars that never block              | DONE      | docs/plan/FEATURE-A5D3.md |
| - PHASE01    | The helper, in the operations           | DONE      | (in FEATURE-A5D3.md)      |
| - PHASE02    | Every page on the helper                | DONE      | (in FEATURE-A5D3.md)      |
| FEATURE-5689 | Tool dialogs on the secondary surface   | DONE      | docs/plan/FEATURE-5689.md |
| FEATURE-8EBF | Pastel, more visible diff colours       | DONE      | docs/plan/FEATURE-8EBF.md |
| FEATURE-8F62 | Release 1.1.0                           | DONE      | docs/plan/FEATURE-8F62.md |
| FEATURE-8CC5 | The first file, every time              | DONE      | docs/plan/FEATURE-8CC5.md |
| FEATURE-1406 | Profiles that own their integrations    | DONE      | docs/plan/FEATURE-1406.md |
| - PHASE01    | Identity becomes Profiles               | DONE      | (in FEATURE-1406.md)      |
| - PHASE02    | Accounts belong to a profile            | DONE      | (in FEATURE-1406.md)      |
| - PHASE03    | Browse repositories in a dialog         | DONE      | (in FEATURE-1406.md)      |
| - PHASE04    | Integrations inside each profile        | DONE      | (in FEATURE-1406.md)      |
| - PHASE05    | A profile pushes only where it may      | DONE      | (in FEATURE-1406.md)      |
| FEATURE-4AC8 | Release 2.0.0                           | DONE      | docs/plan/FEATURE-4AC8.md |
| FEATURE-6C81 | Git signs in with the profile's token   | DONE      | docs/plan/FEATURE-6C81.md |
| - PHASE01    | Credential helper for git               | DONE      | (in FEATURE-6C81.md)      |
| - PHASE02    | Sync and remote pushes sign in          | DONE      | (in FEATURE-6C81.md)      |
| - PHASE03    | Clone signs in                          | DONE      | (in FEATURE-6C81.md)      |
| - PHASE04    | Token scopes and refused tokens         | DONE      | (in FEATURE-6C81.md)      |
| FEATURE-5CD8 | Release 3.0.0                           | DONE      | docs/plan/FEATURE-5CD8.md |
| BUG-6B9E     | Graph stays stale after a push          | DONE      | docs/plan/BUG-6B9E.md     |
| FEATURE-2408 | Release 3.0.0 with the refresh fix      | DONE      | docs/plan/FEATURE-2408.md |
| BUG-6CE0     | History badges arrive late              | DONE      | docs/plan/BUG-6CE0.md     |
| - PHASE01    | Badges as soon as the refs arrive       | DONE      | (in BUG-6CE0.md)          |
| - PHASE02    | A loader at the top of the history      | DONE      | (in BUG-6CE0.md)          |
| FEATURE-292D | A resizable graph column                | DONE      | docs/plan/FEATURE-292D.md |
| BUG-58A7     | Azure DevOps sign-in answers 400        | DONE      | docs/plan/BUG-58A7.md     |
| FEATURE-2074 | Stashes like GitKraken                  | DONE      | docs/plan/FEATURE-2074.md |
| - PHASE01    | One line per stash in the history       | DONE      | (in FEATURE-2074.md)      |
| - PHASE02    | Stash operations aware of conflicts     | DONE      | (in FEATURE-2074.md)      |
| - PHASE03    | Stashing from the history               | DONE      | (in FEATURE-2074.md)      |
| FEATURE-7762 | Drag a branch to merge it               | DONE      | docs/plan/FEATURE-7762.md |
| FEATURE-3E5D | Sort branches and tags                  | DONE      | docs/plan/FEATURE-3E5D.md |
| - PHASE01    | Sorting the branches                    | DONE      | (in FEATURE-3E5D.md)      |
| - PHASE02    | Sorting the tags                        | DONE      | (in FEATURE-3E5D.md)      |
| FEATURE-5860 | Context menus that do more              | DONE      | docs/plan/FEATURE-5860.md |
| - PHASE01    | A line's menu opens anywhere on it      | DONE      | (in FEATURE-5860.md)      |
| - PHASE02    | Copy names and hashes from the history  | DONE      | (in FEATURE-5860.md)      |
| - PHASE03    | Select the line in the history          | DONE      | (in FEATURE-5860.md)      |
| - PHASE04    | Icons on the important operations       | DONE      | (in FEATURE-5860.md)      |
| BUG-28E4     | Untracked file diff is empty            | DONE      | docs/plan/BUG-28E4.md     |
| FEATURE-6DDD | Release 3.1.0                           | DONE      | docs/plan/FEATURE-6DDD.md |
| BUG-EEC1     | Start page empty state not centred      | DONE      | docs/plan/BUG-EEC1.md     |
| FEATURE-49E4 | About in the start window               | DONE      | docs/plan/FEATURE-49E4.md |
| BUG-A303     | File line menus only open on the text   | DONE      | docs/plan/BUG-A303.md     |
| FEATURE-CC8E | Changes: way back, discards, commit box | DONE      | docs/plan/FEATURE-CC8E.md |
| - PHASE01    | Back to the history, in blue            | DONE      | (in FEATURE-CC8E.md)      |
| - PHASE02    | Red discards, one plain question        | DONE      | (in FEATURE-CC8E.md)      |
| - PHASE03    | No Amend, no Sign off                   | DONE      | (in FEATURE-CC8E.md)      |
| FEATURE-FC7E | History: commit details, discard all    | DONE      | docs/plan/FEATURE-FC7E.md |
| - PHASE01    | A commit details dialog                 | DONE      | (in FEATURE-FC7E.md)      |
| - PHASE02    | Details from the line's menu            | DONE      | (in FEATURE-FC7E.md)      |
| - PHASE03    | Discard from the uncommitted line       | DONE      | (in FEATURE-FC7E.md)      |
| FEATURE-10AA | Release 4.0.0                           | DONE      | docs/plan/FEATURE-10AA.md |
| BUG-29C8     | History column titles lack a margin     | DONE      | docs/plan/BUG-29C8.md     |
| BUG-5349     | Long dialog questions are cut off       | DONE      | docs/plan/BUG-5349.md     |
| FEATURE-A2A2 | Tags: bare placeholder, push from menus | DONE      | docs/plan/FEATURE-A2A2.md |
| - PHASE01    | The placeholder says 1.0.0              | DONE      | (in FEATURE-A2A2.md)      |
| - PHASE02    | Pushing one tag to the remote           | DONE      | (in FEATURE-A2A2.md)      |
| - PHASE03    | Push from the badge and the dialog      | DONE      | (in FEATURE-A2A2.md)      |
| FEATURE-A349 | Release 4.1.0                           | DONE      | docs/plan/FEATURE-A349.md |
| BUG-6EAA     | Test suite fails on Windows             | ABANDONED | docs/plan/BUG-6EAA.md     |
| BUG-7E5C     | Dialogs with a view show its type name  | DONE      | docs/plan/BUG-7E5C.md     |
| FEATURE-28C8 | Release 4.1.1                           | DONE      | docs/plan/FEATURE-28C8.md |
| FEATURE-3071 | A narrower navigation rail              | DONE      | docs/plan/FEATURE-3071.md |
| FEATURE-2087 | Clone remembers its directory           | DONE      | docs/plan/FEATURE-2087.md |
| FEATURE-0842 | History: checked-out line, tag focus    | DONE      | docs/plan/FEATURE-0842.md |
| - PHASE01    | The checked-out line is washed          | DONE      | (in FEATURE-0842.md)      |
| - PHASE02    | The tag name has the focus              | DONE      | (in FEATURE-0842.md)      |
| FEATURE-5261 | History details panel                   | DONE      | docs/plan/FEATURE-5261.md |
| - PHASE01    | A details panel for commits             | DONE      | (in FEATURE-5261.md)      |
| - PHASE02    | The working tree in the panel           | DONE      | (in FEATURE-5261.md)      |
| FEATURE-711F | Repository lists per profile            | DONE      | docs/plan/FEATURE-711F.md |
| - PHASE01    | Profiles without a name and email       | DONE      | (in FEATURE-711F.md)      |
| - PHASE02    | A repository list per profile           | DONE      | (in FEATURE-711F.md)      |
| - PHASE03    | The profile picker on the home page     | DONE      | (in FEATURE-711F.md)      |
| - PHASE04    | Reorder repositories by dragging        | DONE      | (in FEATURE-711F.md)      |
| - PHASE05    | A theme button beside About             | DONE      | (in FEATURE-711F.md)      |
| FEATURE-1669 | New repositories start with a README    | DONE      | docs/plan/FEATURE-1669.md |
| BUG-6787     | Diff text cannot be selected            | DONE      | docs/plan/BUG-6787.md     |
| - PHASE01    | The text selection and its drawing      | DONE      | (in BUG-6787.md)          |
| - PHASE02    | Selecting and copying with the pointer  | DONE      | (in BUG-6787.md)          |
| FEATURE-3988 | The name Enigma Git Client              | DONE      | docs/plan/FEATURE-3988.md |
| FEATURE-5DFF | Release 5.0.0                           | DONE      | docs/plan/FEATURE-5DFF.md |
| FEATURE-85E2 | Enigma.Avalonia.Desktop 1.2.0           | DONE      | docs/plan/FEATURE-85E2.md |
| FEATURE-45D3 | Info bars close after 2.5 seconds       | DONE      | docs/plan/FEATURE-45D3.md |
| BUG-15B8     | The repository browser is cut off       | DONE      | docs/plan/BUG-15B8.md     |
| FEATURE-A35A | A selected file lets go on a click      | DONE      | docs/plan/FEATURE-A35A.md |
| - PHASE01    | The commit's files are toggles          | DONE      | (in FEATURE-A35A.md)      |
| - PHASE02    | The working tree's files too            | DONE      | (in FEATURE-A35A.md)      |
| BUG-546B     | Tool dialog headers stay on top         | DONE      | docs/plan/BUG-546B.md     |
| FEATURE-0C53 | Release 5.1.0                           | TODO      | docs/plan/FEATURE-0C53.md |

> `BUG-6EAA` abandoned at the user's request: out of the run's scope. Its unverified work (a Windows retry in
> `AtomicFile`, Windows-safe App test cleanup) is on the unmerged branch `bugfix/bug-6eaa-windows-test-suite`.
