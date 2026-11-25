# CODE-REVIEW-6E56-PHASE02 — [High] SEC-02 Atomic token and key files

## Summary

The token store's secrets (`tokens.json`), its data key (`tokens.key`) and the connected-accounts list
(`host-accounts.json`) are now replaced in one step. Before, a crash, a kill or a full disk in the
middle of `File.WriteAllText` / `FileMode.Create` could leave a truncated file:

- a truncated key or secrets file makes every stored token unreadable;
- a truncated accounts list reads as no accounts at all (EXC-01, PHASE03).

The application runs as several instances, so another instance reading during the write saw the same
partial file.

- `AtomicFile.WriteAllBytes(path, bytes, unixCreateMode)` is new. `WriteAllText` takes an optional
  `unixCreateMode` and now goes through it. Both write a temporary file beside the target, created
  new, by this writer alone, and with its final mode outside Windows. That file is then moved over
  the target with the Windows retry `BUG-3163` added. On failure the temporary file is deleted and
  the previous file is left whole.
- `FileTokenStore.WriteKeyFile`, `FileTokenStore.Write` and `HostAccountService.Write` use it with
  `0600`. Their Windows / Unix branching is gone.
- The text encoding keeps `File.WriteAllText`'s behaviour: the encoding's byte-order mark is written
  when it has one. The default stays UTF-8 without a BOM.

## Files/modules touched

- Modified: `src/Enigma.GitClient.Core/Configuration/AtomicFile.cs`
- Modified: `src/Enigma.GitClient.Core/Security/TokenStore.cs`
- Modified: `src/Enigma.GitClient.Core/Hosting/HostAccountService.cs`
- Modified: `tests/Enigma.GitClient.Core.UnitTests/Configuration/AtomicFileTests.cs`
- Modified: `tests/Enigma.GitClient.Core.UnitTests/Security/TokenStoreTests.cs`
- Modified: `docs/roadmap.md`, `docs/plan/CODE-REVIEW-6E56.md`
- Created: `docs/done/CODE-REVIEW-6E56-PHASE02.md`

## Deviations & follow-ups

- `WriteAllTextAsync` writes through the same temporary-file options. It takes no mode, because no
  async caller writes a secret. Adding the parameter before its `CancellationToken` would have broken
  its positional callers.
- The reads (`File.ReadAllText` / `ReadAllBytes`) stay synchronous. That is PERF-05, a Low finding
  out of this run's scope.
- Pre-existing (baseline): the Windows-only `TokenStoreTests.AfterTheResetOfAnUnreadableStore_SecretsAreStoredAndReadAgain`
  failure. PHASE07 fixes it.

## Build/test evidence

- Baseline: see `docs/done/CODE-REVIEW-6E56-PHASE01.md`.
- **This dev:**
  - build: 0 warnings, 0 errors.
  - Core unit: 1527 run, 1523 passed, 1 failed (the baseline's), 3 skipped. The new Linux-only mode
    test is the third skip, as expected on Windows.
  - Core integration: 449 run, 447 passed, 0 failed, 2 skipped (8 min 17 s).
  - Desktop: compiled, **not run** (untouched by this dev).
- **New tests (Core, run):**
  - `AtomicFileTests`: a BOM encoding is honoured; `WriteAllBytes` replaces the file and leaves
    nothing beside it; with a mode it creates the file `0600` even over a wider one (Linux only,
    skipped on Windows); a replace that fails leaves the previous file whole (Windows only).
  - `TokenStoreTests`: the key and the secrets leave no temporary file beside them, and read back.
  - The existing mode test (`TheKeyAfterAReset_IsOnlyReadableByItsOwner`) still covers `0600` on
    Linux.
- **Fix budget:** 0 cycles.

## Documentation sweep

- Nothing stale. The README says the tokens are protected by DPAPI and `0600`, which still holds.

## Decisions taken at build time

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| Shape of the extension | `WriteAllBytes` plus an optional mode on `WriteAllText`, both over one temporary-file writer | One code path for every atomic write, the BUG-3163 retry included | A separate secret-file writer (a second copy of the replace logic) |
| Showing that a failed write keeps the old file | A Windows-only test where a held target refuses the replace | Windows is the only platform where a replace can be made to fail without faking the file system | A file-system abstraction for one test |
