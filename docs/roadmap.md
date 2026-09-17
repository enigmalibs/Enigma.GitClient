# Roadmap

Registry of every tracked work item in Enigma.GitClient. Full details live in `docs/plan/<ID>.md`;
completion records live in `docs/done/<ID>.md`.

| ID           | Title                              | Status      | Plan                      |
|--------------|------------------------------------|-------------|---------------------------|
| FEATURE-7CFD | Solution foundation & git engine   | DONE        | docs/plan/FEATURE-7CFD.md |
| - PHASE01    | Solution scaffolding & config      | DONE        | (in FEATURE-7CFD.md)      |
| - PHASE02    | Git process runner & discovery     | DONE        | (in FEATURE-7CFD.md)      |
| - PHASE03    | Commit log reading & model         | DONE        | (in FEATURE-7CFD.md)      |
| - PHASE04    | Refs, branches, tags & HEAD state  | DONE        | (in FEATURE-7CFD.md)      |
| FEATURE-6DB0 | Core graph, diff & tree algorithms | DONE        | docs/plan/FEATURE-6DB0.md |
| - PHASE01    | Commit graph lane layout           | DONE        | (in FEATURE-6DB0.md)      |
| - PHASE02    | Unified diff & word-level diff     | DONE        | (in FEATURE-6DB0.md)      |
| - PHASE03    | File path tree builder             | DONE        | (in FEATURE-6DB0.md)      |
| FEATURE-52FB | App shell & repository opening     | DONE        | docs/plan/FEATURE-52FB.md |
| - PHASE01    | Avalonia shell, theme & DI         | DONE        | (in FEATURE-52FB.md)      |
| - PHASE02    | Open, init & clone repositories    | DONE        | (in FEATURE-52FB.md)      |
| FEATURE-2326 | Commit graph UI                    | DONE        | docs/plan/FEATURE-2326.md |
| - PHASE01    | Graph row rendering control        | DONE        | (in FEATURE-2326.md)      |
| - PHASE02    | History page & virtualisation      | DONE        | (in FEATURE-2326.md)      |
| FEATURE-7D1B | Commit details & diff viewer       | DONE        | docs/plan/FEATURE-7D1B.md |
| - PHASE01    | Changed files list/tree panel      | DONE        | (in FEATURE-7D1B.md)      |
| - PHASE02    | Colour-coded diff viewer           | DONE        | (in FEATURE-7D1B.md)      |
| FEATURE-478C | Branches, tags & checkout          | DONE        | docs/plan/FEATURE-478C.md |
| - PHASE01    | Branch management                  | DONE        | (in FEATURE-478C.md)      |
| - PHASE02    | Tags & checkout anything           | DONE        | (in FEATURE-478C.md)      |
| FEATURE-13FE | Working directory & commits        | DONE        | docs/plan/FEATURE-13FE.md |
| - PHASE01    | Status, staging & commit engine    | DONE        | (in FEATURE-13FE.md)      |
| - PHASE02    | Changes page & commit UI           | DONE        | (in FEATURE-13FE.md)      |
| FEATURE-06FE | Remotes & synchronisation          | DONE        | docs/plan/FEATURE-06FE.md |
| - PHASE01    | Fetch, pull, push & remotes        | DONE        | (in FEATURE-06FE.md)      |
| - PHASE02    | Stash management                   | DONE        | (in FEATURE-06FE.md)      |
| FEATURE-6DCC | Merge & conflict resolution        | DONE        | docs/plan/FEATURE-6DCC.md |
| - PHASE01    | Merge engine & conflict model      | DONE        | (in FEATURE-6DCC.md)      |
| - PHASE02    | Conflict resolution engine         | DONE        | (in FEATURE-6DCC.md)      |
| - PHASE03    | Conflict resolution UI             | DONE        | (in FEATURE-6DCC.md)      |
| FEATURE-22C0 | Repository hosting integrations    | DONE        | docs/plan/FEATURE-22C0.md |
| - PHASE01    | Provider abstraction & tokens      | DONE        | (in FEATURE-22C0.md)      |
| - PHASE02    | GitHub provider                    | DONE        | (in FEATURE-22C0.md)      |
| - PHASE03    | GitLab & Azure DevOps providers    | DONE        | (in FEATURE-22C0.md)      |
| FEATURE-5D77 | Settings, preferences & docs       | DONE        | docs/plan/FEATURE-5D77.md |
| BUG-6CE6     | Details panel ignores the splitter | DONE        | docs/plan/BUG-6CE6.md     |
| FEATURE-0183 | Bigger graph nodes, taller rows    | DONE        | docs/plan/FEATURE-0183.md |
| - PHASE01    | Double the graph node size         | DONE        | (in FEATURE-0183.md)      |
| - PHASE02    | Default row height of 36           | DONE        | (in FEATURE-0183.md)      |
| FEATURE-7676 | Diff panel defaults and typography | DONE        | docs/plan/FEATURE-7676.md |
| - PHASE01    | Side by side by default            | DONE        | (in FEATURE-7676.md)      |
| - PHASE02    | Configurable diff font             | DONE        | (in FEATURE-7676.md)      |
| BUG-1D34     | Diff lines overlap the other pane  | DONE        | docs/plan/BUG-1D34.md     |
| - PHASE01    | An offsettable diff line           | DONE        | (in BUG-1D34.md)          |
| - PHASE02    | Clip the panes and scroll them     | DONE        | (in BUG-1D34.md)          |
| FEATURE-2FDF | More room between graph lanes      | TODO        | docs/plan/FEATURE-2FDF.md |
| FEATURE-2288 | Diffs in a dialog, not a panel     | TODO        | docs/plan/FEATURE-2288.md |
| - PHASE01    | The commit diff dialog             | TODO        | (in FEATURE-2288.md)      |
| - PHASE02    | Reopen it from the row menu        | TODO        | (in FEATURE-2288.md)      |
