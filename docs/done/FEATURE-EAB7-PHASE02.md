# FEATURE-EAB7-PHASE02 — The buildmsi script

**Item:** FEATURE-EAB7 — Windows MSI build script
**Branch:** `feature/feature-eab7-phase02-buildmsi-script`
**Run:** feature/2026-11-03-icons-msi-release

## Summary

`packaging/windows/buildmsi.ps1` builds the Windows installer in one command, from any directory:

1. It checks that `git`, `dotnet`, `wix` and the vendored worker are there, **before** anything is
   cleaned.
2. It reads the version from `Directory.Build.props` and checks it is `X.Y.Z`.
3. `git -C <root> clean -dxf`.
4. `publish.ps1`, the existing `-c Release -r win-x64 -p:PublishReadyToRun=true` publish. Then it checks
   that the exe and `app.ico` are in the publish folder.
5. It writes `artifacts/Enigma.GitClient.X.Y.Z.msipkg.json`, an Enigma.Msi profile:
   - absolute paths, a new `productId` each build;
   - upgrade code `871622d1-a9c6-4c72-b6a3-e8d8104ad5c3`;
   - "Enigma Git Client", "Josué Clément", `PerMachine`, `%ProgramFiles%\Enigma Git Client`;
   - `compression` `None`;
   - `app.ico` as the Control Panel icon and both shortcuts' icon;
   - shortcuts in `%Desktop%` and `%ProgramMenu%`.
6. It runs `Enigma.Msi.Worker.exe build` on that profile into `artifacts/Enigma.GitClient.X.Y.Z.msi`.

Every failure prints `buildmsi: <reason>` and exits non-zero, with the failing tool's exit code when
there is one. `buildmsi.bat` runs it from `cmd` with `-ExecutionPolicy Bypass` and passes the exit code
on.

The upgrade code is the one the installed 6.13.0 MSI carries, so the next MSI upgrades it instead of
installing beside it. `WindowsInstallerIdentityTests` pins it.

## Files/modules touched

- Created:
  - `packaging/windows/buildmsi.ps1`
  - `packaging/windows/buildmsi.bat`
  - `tests/Enigma.GitClient.Core.UnitTests/WindowsInstallerIdentityTests.cs`
  - `docs/done/FEATURE-EAB7-PHASE02.md`
- Modified:
  - `tests/Enigma.GitClient.Core.UnitTests/DisplayNameConsistencyTests.cs`
  - `tests/Enigma.GitClient.Desktop.UnitTests/AssemblyNameConsistencyTests.cs`
  - `README.md`, `docs/RELEASE.md`
  - `docs/roadmap.md`, `docs/plan/FEATURE-EAB7.md`: statuses.

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| Failure reporting | `Stop-Build`: one `buildmsi: …` line on stderr, then `exit` with the tool's code | Clearer than a PowerShell error record, and the code is still meaningful, as in `publish.ps1` |
| Reading the version | `XmlDocument.Load` + `/Project/PropertyGroup/Version` | `Get-Content` would read the UTF-8 file (`Josué` in `<Authors>`) as ANSI in Windows PowerShell 5.1 |
| What the tests pin | `$ProductName`, `$UpgradeCode` (Core) and `$Assembly` (Desktop), as whole lines | The script derives the install folder, the shortcuts, the publish folder and the exe from those three |
| Elapsed time | Printed on the last line | The user's goal is a quick build; it shows |
| The PDBs in the publish folder | Packaged, as today | The 6.13.0 MSI shipped the same 86 files, `libSkiaSharp.pdb` (80 MB) and `libHarfBuzzSharp.pdb` (20 MB) included; the publish is FEATURE-0D4E's |

## Deviations & follow-ups

- **Follow-up: the MSI is 189 MB because of native PDBs.** The publish folder carries `libSkiaSharp.pdb`
  and `libHarfBuzzSharp.pdb` from the SkiaSharp/HarfBuzz packages. The 6.13.0 MSI had them too.
  Leaving them out would make the MSI about 100 MB smaller and its build faster, but it changes the
  publish, so it is not done here.
- **A 32-bit package.** Enigma.Msi has no platform setting, so the MSI's template is `Intel;1033`, and
  `%ProgramFiles%` is `Program Files (x86)` on 64-bit Windows. That is where 6.13.0 is installed too.
  `docs/RELEASE.md` says so.
- **WixSharp's "source base directory"** is the current directory (it printed the caller's). It
  resolves relative paths only; the profile's are absolute. The run's repository had no new files
  after the build.
- **Before tagging:** run the Desktop suite on Linux; it carries the new
  `TheWindowsInstallerPackagesAndStartsTheExecutableOfTheAssembly` test (checked here by `grep`).
- **Not run, by design:** installing, launching or uninstalling the MSI.
- Line endings: `buildmsi.bat` is CRLF as `.gitattributes` asks; the `.ps1` is LF.

## Build/test evidence

- Baseline: see `docs/done/FEATURE-35EB.md`.
- `dotnet build Enigma.GitClient.slnx -c Release`: **0 warnings, 0 errors**. Desktop tests compiled.
- Core unit: **1,394** total (the baseline's 1,392 plus the 2 new tests), 1,391 passed, 2 skipped,
  1 failed (the baseline's `TokenStoreTests.AfterTheResetOfAnUnreadableStore_SecretsAreStoredAndReadAgain`;
  nothing new). `WindowsInstallerIdentityTests` and `DisplayNameConsistencyTests` run on their own:
  6/6 passed.
- Core integration: 414 total, 412 passed, 2 skipped, 0 failed.
- The Desktop assertion, checked by `grep -x`: `buildmsi.ps1` holds
  `$Assembly = 'Enigma.GitClient.Desktop'`.
- **End to end, in a scratch clone** of this branch with the dev's tree committed there (the run may
  not `git clean` the user's repository):
  - **No `wix` on the `PATH`:** `buildmsi: wix is not on the PATH: dotnet tool install --global wix.`,
    exit code 1, and an untracked canary file was still there, so nothing was cleaned.
  - **`buildmsi.bat`:**
    - git removed the canary file; the publish ran; the worker built the MSI;
    - exit code **0**, in **26 s** wall time (restore, R2R publish and MSI);
    - `artifacts/` holds `Enigma.GitClient.6.13.0.msi` (189,105,743 bytes; the payload is 186,184,234
      bytes in 86 files, so the MSI is uncompressed) and its `.msipkg.json` (`"compression": "None"`);
    - `git status --porcelain` was empty afterwards; only `packaging/windows/Enigma.Msi.Worker/.wixsharp/`
      appears, as ignored.
  - **The MSI's tables**, read through the Windows Installer API:
    - `ProductName` Enigma Git Client, `Manufacturer` Josué Clément, `ProductVersion` 6.13.0;
    - `UpgradeCode` `{871622D1-A9C6-4C72-B6A3-E8D8104AD5C3}`, `ALLUSERS` 1, `ARPPRODUCTICON`
      `app_icon.ico`;
    - `INSTALLDIR` "Enigma Git Client" under `ProgramFilesFolder`;
    - two "Enigma Git Client.lnk" shortcuts (`DesktopFolder`, `ProgramMenuFolder`) on
      `[INSTALLDIR]\Enigma.GitClient.Desktop.exe`, icon `IconFile1_app.ico`;
    - `Upgrade`: `VersionMax 6.13.0` (attributes 1) and `VersionMin 6.13.0` (attributes 2);
    - 86 files; template `Intel;1033`.
    The same rows as the installed 6.13.0's package, except the product code.
- Fix budget: 0 cycles used.

## Documentation sweep

- `README.md`: a new *Windows installer* section after *Publish*: what `buildmsi` does (with the
  `git clean -dxf` warning), what the MSI installs, and the prerequisites.
- `docs/RELEASE.md`:
  - §6 *Install it*: Windows now has an installer (it said "Windows has no installer"), with what it
    installs;
  - §7: a Windows check after installing the MSI;
  - *Why there is no MSI profile* became *The Windows installer's identity*: the upgrade code is fixed
    in the script, the product code is new per build.
