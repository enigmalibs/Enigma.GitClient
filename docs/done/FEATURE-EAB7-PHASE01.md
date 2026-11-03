# FEATURE-EAB7-PHASE01 — Vendor the Enigma.Msi worker CLI

**Item:** FEATURE-EAB7 — Windows MSI build script
**Branch:** `feature/feature-eab7-phase01-vendor-msi-worker`
**Run:** feature/2026-11-03-icons-msi-release

## Summary

The repository now carries Enigma.Msi's headless worker CLI in `packaging/windows/Enigma.Msi.Worker/`,
for PHASE02's `buildmsi.ps1` to run (`Enigma.Msi.Worker.exe build <profile>`). It is Enigma.Msi `1.7.0`
(`a7a1bfb`) built against WixSharp_wix4 2.14.2: the 20 files of its `bin/Release/net472/` output,
without the PDBs, the API XML and `.wixsharp/` (3.4 MB).

- A `README.md` in the folder gives:
  - its source;
  - the CLI's exit codes;
  - the requirements: .NET Framework 4.7.2 and the WiX CLI, with WiX 7's EULA;
  - the components' licences: Enigma.Msi, WixSharp and .NET are MIT; the WiX 4.0.1 assemblies are MS-RL;
  - how to refresh it.
- `.gitignore` ignores `packaging/windows/Enigma.Msi.Worker/.wixsharp/`. WixSharp writes it beside the
  worker on its first build. `git clean -dxf` removes it, and the next build writes it again.

## Files/modules touched

- Created:
  - `packaging/windows/Enigma.Msi.Worker/` (20 binaries: `Enigma.Msi.Worker.exe`,
    `Enigma.Msi.Worker.exe.config`, `Enigma.Msi.dll`, the `WixSharp*`, `WixToolset.*`, `System.*`,
    `Microsoft.Bcl.AsyncInterfaces.dll` and `mbanative.dll`)
  - `packaging/windows/Enigma.Msi.Worker/README.md`
  - `docs/done/FEATURE-EAB7-PHASE01.md`
- Modified:
  - `.gitignore`
  - `docs/roadmap.md`, `docs/plan/FEATURE-EAB7.md`: statuses.

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| Which build to copy | Enigma.Msi's existing `bin/Release/net472/` output | Its `Enigma.Msi.dll` carries `1.0.0+a7a1bfb`, the `1.7.0` tag's commit, and the Enigma.Msi tree is clean. Rebuilding would have written into another repository |
| WiX 7's EULA in the README | Named, with `wix eula accept wix7` | WiX 7 refuses to build until it is accepted; checked with `wix eula --help` |
| Link to Enigma.Msi in the README | Plain text, no relative link | A link out of the repository works only with the sibling checkout |

## Deviations & follow-ups

- `Enigma.Msi.Worker.exe.config` is the only file that differs from its source, and only in line
  endings. The build writes CRLF; `.gitattributes` (`* text=auto eol=lf`) stores it as LF. .NET
  Framework reads either. Every `.exe`/`.dll` is byte for byte (`.gitattributes` marks them binary).
- The `.gitignore` had no newline at its end; the new entry adds one.
- `DisplayNameConsistencyTests.NothingShippedStillSaysTheFormerName` reads every file under `packaging/`
  as text, the new binaries included. It passes and stays fast (3.4 MB).

## Build/test evidence

- Baseline: see `docs/done/FEATURE-35EB.md` (0 warnings; Core unit 1 known failure; Core integration
  green).
- `dotnet build Enigma.GitClient.slnx -c Release`: **0 warnings, 0 errors**. Desktop tests compiled.
- Core unit: 1,392 total, 1,389 passed, 2 skipped, 1 failed (the baseline's
  `TokenStoreTests.AfterTheResetOfAnUnreadableStore_SecretsAreStoredAndReadAgain`; nothing new).
  `NothingShippedStillSaysTheFormerName` run on its own: passed.
- Core integration: 414 total, 412 passed, 2 skipped, 0 failed.
- `cmp` of each copied file against its source: all identical before staging.
- `git status --porcelain --ignored -uall packaging/windows/Enigma.Msi.Worker/`: the 21 files (20 binaries and the README) are
  untracked, none ignored. `git check-ignore` matches `.wixsharp/…` to the new rule.
- `Enigma.Msi.Worker.exe` with no arguments printed "No arguments were given." and the two usage forms,
  exit code **2**.
- Fix budget: 0 cycles used.

## Documentation sweep

- Nothing stale yet: the README and `docs/RELEASE.md` change with the script itself (PHASE02).
