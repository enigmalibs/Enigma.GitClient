# CODE-REVIEW-6E56-PHASE07 — [Medium] TEST-02 Token test on Windows

## Summary

`TokenStoreTests.AfterTheResetOfAnUnreadableStore_SecretsAreStoredAndReadAgain` failed on Windows
with `CryptographicException: The parameter is incorrect`.

- The new secret was written through a store whose fake protector left the new key unsealed, then
  read back through `_store`, whose real protector is DPAPI on Windows.
- It passed on Linux only because the platform protector is a pass-through there.
- This was the run's one baseline failure, and the behaviour the test is named for was not actually
  verified on Windows.

The reset still goes through the refusing store, which cannot open its key; that is the situation the
reset exists for. The secret typed again is then written **and** read through `_store`, so the key is
sealed and opened by the same real protector on every platform. The "fresh key" assertion stays.
There is no skip on Windows. The `!` on the read became an `Assert.IsType`.

## Files/modules touched

- Modified: `tests/Enigma.GitClient.Core.UnitTests/Security/TokenStoreTests.cs`
- Modified: `docs/roadmap.md`, `docs/plan/CODE-REVIEW-6E56.md`
- Created: `docs/done/CODE-REVIEW-6E56-PHASE07.md`

## Deviations & follow-ups

- None from the plan. From this dev on, the run's Core unit suite is expected fully green on Windows,
  and later devs are held to that. The baseline's one failure is gone.

## Build/test evidence

- Baseline: see `docs/done/CODE-REVIEW-6E56-PHASE01.md` (1 failure: this test).
- The test on its own, Windows: passed.
- **This dev:**
  - build: 0 warnings, 0 errors.
  - Core unit: 1530 run, **1527 passed, 0 failed**, 3 skipped.
  - Core integration: 449 run, 447 passed, 0 failed, 2 skipped (6 min 34 s).
  - Desktop: compiled, **not run** (untouched by this dev).
- **Fix budget:** 0 cycles.

## Documentation sweep

- Nothing stale. The workflow's memory note about this known failure is outside the repository.

## Decisions taken at build time

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| Which store resets, which writes | The refusing store resets; the real store writes and reads | The reset is what is under test on an unreadable store. The write and read must share a protector | Resetting through `_store` after corrupting its key (no longer tests the refusing case); skipping on Windows (the review rules it out) |
