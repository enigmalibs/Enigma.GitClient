# BUG-3024-PHASE01 — Failure reasons and reset in Core

**Item:** BUG-3024 — Unreadable tokens cannot be reset, PHASE01 — Failure reasons and reset in Core
**Branch:** `bugfix/bug-3024-phase01-core-reasons-reset`
**Run:** bugfix/2026-10-23-crash-net-token-reset

## Summary

The token store now says *why* a secret cannot be read, never silently mints a key over secrets it
cannot open, and has a reset (the way out) and a check (for the Profiles page to ask without handing out
any secret).

- **`TokenProtectionReason`** has four values:
  - `Unspecified`: the exception's three legacy constructors;
  - `KeyUnavailable`: the whole store is unreadable for this account;
  - `EntryUnreadable`: one entry;
  - `FileUnreadable`: the tokens file.

  `TokenProtectionException` gains a constructor `(reason, message, innerException = null, key = null)`
  and three properties: `Reason`, `Key` (the entry's, for `EntryUnreadable`) and `IsStoreWide`. The
  three existing constructors and every existing message are kept.
- **`IDataKeyProtector` / `PlatformDataKeyProtector`** (`Security/DataKeyProtector.cs`) is the one
  OS-dependent step: DPAPI `CurrentUser` on Windows, the bytes unchanged elsewhere.
  `FileTokenStore(IAppPaths, IDataKeyProtector)` is now the only constructor, and the protector is
  registered as a singleton in `AddRepositoryHosting`. Tests replace it with one that refuses to open any
  key, which is what DPAPI does after a Windows password reset.
- **`FileTokenStore`:**
  - The data key is read without being created (`ReadDataKey`). A key that will not unwrap is
    `KeyUnavailable` (existing message). So is an unwrapped key that is not 32 bytes (new message, "is
    damaged"): previously it surfaced as a raw `CryptographicException` from `AesGcm` on a write, or as a
    misleading "entry" failure on a read.
  - `TryGetAsync` with an entry present and no key file is `KeyUnavailable` (new message, "is missing").
    Previously a new key was minted and the entry then failed as "damaged".
  - `SetAsync` creates a key only when no *other* entry needs the old one (`DataKeyFor`). Otherwise it is
    `KeyUnavailable`, and the key file is not recreated.
  - Entry failures are `EntryUnreadable` with the key (existing message). A JSON `null` entry is one too,
    where it used to throw a `NullReferenceException`.
  - A tokens file that is not JSON is `FileUnreadable` (existing message).
  - Decryption is split into `Open` (plaintext bytes, which the caller zeroes) and `Unprotect` (the
    `SecretString`).
- **`ITokenStore.ResetAsync`** deletes `tokens.json` and then `tokens.key`, under the gate. The next
  `SetAsync` creates a fresh key, `0600` on Linux, because the file is created again with
  `UnixCreateMode`.
- **`ITokenStore.CheckAsync`** returns a `TokenStoreStatus`: `Failure` (the store-wide reason, or `null`),
  `Keys`, `UnreadableKeys`, `IsReadable`, `CanRead(key)` and `Holds(key)`. It authenticates every entry
  with `Open` and zeroes each plaintext. A `SecretString` is never built, and the method never throws a
  `TokenProtectionException`.

## Files / modules touched

**Created**

- `src/Enigma.GitClient.Core/Security/DataKeyProtector.cs`
- `docs/done/BUG-3024-PHASE01.md`

**Modified**

- `src/Enigma.GitClient.Core/Security/TokenStore.cs`: reason, exception members, `TokenStoreStatus`, the
  two interface members, and the store as above.
- `src/Enigma.GitClient.Core/DependencyInjection/ServiceCollectionExtensions.cs`: the protector.
- `tests/Enigma.GitClient.Core.UnitTests/Security/TokenStoreTests.cs`:
  - the four existing failure tests now also assert their reason (and key), which only strengthens them;
  - the constructor takes a protector;
  - 15 new tests on the key that cannot be opened (read and write), the key of the wrong size, the key
    file deleted with secrets left (not replaced), replacing the only secret with the key gone, the legacy
    constructors, the reset (both files, empty store, round trip after resetting an unreadable store, the
    new key's `0600`), and the check (healthy, empty, key unavailable, key gone, not JSON, one damaged
    entry among good ones).
- `tests/Enigma.GitClient.Core.UnitTests/Hosting/HostAccountServiceTests.cs`: the constructor; new
  `AfterTheResetOfAnUnreadableStore_ConnectingWorks`. It updates the existing account and adds another.
- `docs/roadmap.md`, `docs/plan/BUG-3024.md`: statuses.

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| Message for a wrong-size or missing key | New sentences ("is damaged" / "is missing", each ending with the existing "Remove them and sign in again.") | Those cases had no message of their own. "A different machine or user account" is wrong for them. Every existing message stays where it was |
| Overwriting the only entry when the key is gone | Allowed, with a fresh key | No other secret depends on the old key, so nothing is lost silently |
| Order of the reset | Secrets, then key | A reset stopped half-way never leaves secrets without the key's file being removed later by the next reset |
| Where `CheckAsync` reports a key failure with an unreadable file | `FileUnreadable` with no keys | Without the file there is no key list to give |
| Registration place | `AddRepositoryHosting`, beside `ITokenStore` | Where the store is registered (the plan said `AddGitClientCore`, which calls it) |

## Deviations & follow-ups

- **Limit (as planned):** on Linux, a *well-sized* but wrong key (another machine's 32 bytes) cannot be told
  from damaged entries. Every entry reports `EntryUnreadable`, because the file format has no key check
  value and adding one would change the scheme. On Windows, which is the reported case, DPAPI refuses the
  key, so it is `KeyUnavailable`.
- **Plan wording:** the plan's PHASE01 test list said a connect via `AddAsync`. The test covers both
  `UpdateAsync` of the existing account (the PHASE02 reconnect path) and `AddAsync` of a new one.

## Build/test evidence

- `dotnet build Enigma.GitClient.slnx`: 0 warnings, 0 errors.
- `dotnet test --solution Enigma.GitClient.slnx`: 3392 total, 3389 passed, 3 skipped, 0 failed (+16 new).
