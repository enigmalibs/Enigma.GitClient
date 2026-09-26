# FEATURE-70C1-PHASE02 — Remembering hidden branches

**Item:** FEATURE-70C1 — Hide branches from the history
**Branch:** `feature/feature-70c1-phase02-hidden-branch-store`
**Run:** feature/2026-09-26-release-1-0-0

## Summary

Which branches are hidden from the history is now kept per repository, across restarts and across
instances, in `hidden-branches.json` in the configuration directory:

```json
{
  "version": 1,
  "repositories": {
    "/home/you/src/project": [ "refs/heads/spike", "refs/remotes/origin/spike" ]
  }
}
```

- **`IHiddenBranchStore` / `HiddenBranchStore`** — `Get`, `SetHidden` and `ShowAll` per work tree. Every
  change reads the file again and writes it atomically under a lock, so two instances keep each other's
  work; the same directory with or without a trailing separator (and in any case on Windows) is the
  same repository; a repository whose last hidden branch is shown again leaves the file; an entry that
  is not one full ref name (a hand edit) hides nothing; a corrupt file is moved aside as
  `hidden-branches.corrupt-<stamp>.json` and read as empty; a read or write failure is logged and never
  thrown at the user. Given the repository's refs, a write drops the entries of branches deleted since
  they were hidden — never without them, so a set is not wiped by a write made before the refs were
  read.
- **`IHiddenBranches` / `HiddenBranches`** — the open repository's set, for the branches dialog and the
  history (PHASE03): `Hidden`, `IsHidden`, `SetHidden`, `ShowAll` and a `Changed` event raised once per
  real change. It reads the set the first time it is asked after a repository opens — so the history's
  first query of a repository already gets that repository's set — is empty with no repository, and
  keeps hiding for the session when the store fails.
- The README's *Where your things are kept* table lists the new file.

## Files / modules touched

**Created — App**

- `Services/HiddenBranchStore.cs`
- `Services/HiddenBranches.cs`

**Modified**

- `App/DependencyInjection/ServiceCollectionExtensions.cs` — both registered as singletons
- `README.md` — the file in *Where your things are kept* (sweep)

**Tests**

- `App.UnitTests/HiddenBranchStoreTests.cs` (new, 16) — empty at first; remembered across stores; per
  repository; a trailing separator is the same repository; showing again forgets the entry and then
  the repository; show all is per repository; pruning with the refs, none without; malformed names
  refused; two stores writing in turn; the file names its version; hand-edited entries ignored; a
  corrupt file moved aside and never overwritten
- `App.UnitTests/HiddenBranchesTests.cs` (new, 7) — nothing without a repository; read for the open
  repository; another repository brings its own set and closing empties it; hide and show are stored
  and announced once per real change; show all; a deleted branch forgotten at the next change; a
  failing store reads as nothing hidden and the session still hides

## Deviations & follow-ups

- **Synchronous, not `GetAsync` / `SetHiddenAsync` as the plan named them.** The history reads the set
  as it builds each query, on the UI thread and before git runs; an asynchronous read would either race
  the first page of a repository just opened — showing the hidden branches for a moment — or hold that
  page back. The file is a few hundred bytes, read under a lock and written atomically, as
  `RecentRepositoryStore` already does from inside its async methods.
- **No `IRepositoryContext` subscription.** The service keys its cached set by work tree and re-reads it
  when the open repository differs from the one it read, which makes it independent of the order in
  which pages and services hear about a repository change.

## Build/test evidence

- `dotnet build Enigma.GitClient.slnx` — 0 warnings, 0 errors.
- `dotnet test --solution Enigma.GitClient.slnx` — 2195 passed, 0 failed (23 new).

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| Where the refs for pruning come from | The context's local and remote branches, or none while they are empty | An empty list before the first read would wipe every hidden branch |
| What a failed write does to the session | The change still applies in memory; a warning is logged | Hiding is a view preference: the user sees what they asked for even when it cannot be remembered |
| The file's key | The normalised full work-tree path | What `RecentRepositoryStore` keys by; one repository per process |
