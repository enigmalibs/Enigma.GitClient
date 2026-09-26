# FEATURE-6DB0-PHASE03 — File path tree builder

**Item:** FEATURE-6DB0 — Core graph, diff & tree algorithms
**Branch:** `feature/feature-6db0-phase03-file-tree`
**Run:** feature/2026-09-15-enigma-git-client

## Summary

Added the model and builder behind the list/tree toggle the specification asks for: a `ChangedFile`
summary shared by the commit view and the working-directory view, and a tree builder that nests
files under their directories, collapses single-child directory chains the way GitHub and GitKraken
do, sorts directories first, and aggregates per-directory change and line counts for the folder
badges.

This completes FEATURE-6DB0: all three headless algorithms the two headline features rest on are
implemented and tested.

## Files / modules touched

**Created — `src/Enigma.GitClient.Core/Files/`**

- `ChangedFile.cs` — path, old path, change kind, staging state, line counts, binary/conflicted/
  submodule flags, `Name` / `DirectoryPath` / `IsRenamed`, path normalisation, and `FromPatch`
- `FileTreeNode.cs` — `FileTreeCounts` (with aggregation) and the immutable tree node, plus
  `DescendantsAndSelf` and `Find`
- `FileTreeBuilder.cs` — `FileTreeOptions`, `Build`, `Flatten` and `Total`

**Created — tests**

- `tests/Enigma.GitClient.Core.UnitTests/Files/FileTreeBuilderTests.cs` — 34 cases

**Modified**

- `docs/roadmap.md`, `docs/plan/FEATURE-6DB0.md` — status updates

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| Where `ChangedFile` lives | A new `Core.Files` namespace, not `Core.Diff` | It is produced by **both** the diff layer (a commit's files) and the status layer (the working directory). Putting it in either one would make the other depend on a namespace it has no other business with |
| Collapsing rule | Fold only while a directory has exactly one child **and** that child is a directory | Folding a directory into its single *file* child would hide the directory entirely — `docs/README.md` must still show a `docs` folder. A test pins this |
| A collapsed node's `FullPath` | The path of the **deepest** folded directory | The display name spans the chain, but every other consumer — selection, expansion state, the "reveal in explorer" action — needs a real path that exists |
| Sort order | Directories first, then case-insensitive, with an ordinal tie-break | Directories-first is what every file browser does and what makes a tree scannable. The ordinal tie-break means `README.md` and `readme.md` have a stable order instead of one that depends on enumeration |
| A path appearing twice | The last entry wins, one row | The working-directory view can legitimately produce a staged and an unstaged entry for the same path; duplicating the row would be worse than showing the later state, and the panel models the pair explicitly through `FileStagingState.PartiallyStaged` |
| `Flatten` | Added alongside `Build` | The list view and the tree view must agree on order, and deriving the list from the tree is the only way to guarantee that without duplicating the sort |
| Windows separators | Normalised to `/` on entry | git always speaks forward slashes; accepting a backslash path and normalising it means no caller has to remember |

## Deviations & follow-ups

- **Deviation (additive):** `FileTreeBuilder.Flatten` and `FileTreeBuilder.Total`, and
  `FileTreeNode.Find` / `DescendantsAndSelf`, were not named in the plan. They are what the panel
  needs to keep the two views in step and to restore a selection after the commit changes.
- **Follow-up:** the builder returns a fully materialised tree. A commit touching ten thousand files
  builds in well under the panel's frame budget (a test covers exactly that shape), so lazy
  expansion is not needed; revisit only if a real repository proves otherwise.
- **Follow-up:** `ChangedFile.IsConflicted` is modelled here but only populated from
  FEATURE-13FE's status reader onwards; the diff layer never sets it.
- **Line endings (recommendation only):** no CRLF churn observed. No action taken.

## Build/test evidence

```
dotnet build Enigma.GitClient.slnx
  0 Warning(s)
  0 Error(s)

dotnet test --solution Enigma.GitClient.slnx
  Test run summary: Passed!
  total: 409  failed: 0  succeeded: 409  skipped: 0
```

36 tests are new in this dev. Both gates passed on the first run; no fix cycle was needed. The
warning count was read explicitly (0).
