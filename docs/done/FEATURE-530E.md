# FEATURE-530E — Release 6.13.0

## Summary

Cut **6.13.0** in 6.12.0's shape:

- the version in `Directory.Build.props` is 6.13.0;
- a dated `## 6.13.0 — 2026-10-29` section is on top of `RELEASENOTES.md`;
- the README callout names 6.13 and sums up 6.12 after it;
- `SECURITY.md` supports 6.13.x only;
- `docs/RELEASE.md` lists 6.13.0 among the releases without an MSI profile.

The release carries the run's three `DONE` items:

- FEATURE-0DED: no splash screen;
- FEATURE-5096: `app.ico` beside the executable;
- FEATURE-0D4E: `publish.bat` and `publish.sh`.

No item was quarantined.

## Files/modules touched

- Modified:
  - `Directory.Build.props`
  - `RELEASENOTES.md`
  - `README.md`
  - `SECURITY.md`
  - `docs/RELEASE.md`

## Deviations & follow-ups

- **The spec items that needed no change in this repository**, recorded in the plan and not in the
  notes:
  - the About dialog: this application's is the reference;
  - unhandled exceptions: BUG-9DE3 already catches them and shows a content dialog;
  - the Linux `install.sh` and `uninstall.sh`: already there;
  - .NET 8 targets: none;
  - NuGet packages: all already at their latest version.
- **Before tagging:** run the Desktop suite on Linux. It was compiled but not run on this Windows
  machine, and it carries the three devs' new and changed tests.
- **Before tagging:** settle `TokenStoreTests.AfterTheResetOfAnUnreadableStore_SecretsAreStoredAndReadAgain`.
  It is a pre-existing Windows-only failure (see the evidence in `docs/done/FEATURE-0DED.md`); it passes on Linux.
- Merging into `develop` and `main`, tagging `6.13.0` and pushing are the user's (`docs/RELEASE.md`
  steps 2–4). Nothing here ran them.

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| The splash's history in the notes | Not claimed. The splash shipped with 1.0.0, so no earlier release ever started without it | First draft said "as before the splash"; there was no such release |
| The *Dependencies* section | Kept, saying nothing is outdated | 6.12.0's shape always has it; the check is the evidence |
| The MSI paragraph's reason | "the Linux installer and the publish scripts" | The Windows packaging story is now the publish scripts |

## Build/test evidence

- `git tag --list 6.13.0`: empty (6.12.0 is the latest tag).
- `dotnet list Enigma.GitClient.slnx package --outdated`: "no updates" for all five projects.
- `git diff 191ef4c HEAD -G CurrentVersion`: empty, so no stored format moved. The notes' quoted
  versions match the code: `AppSettings.CurrentVersion = 7`, `RepositoryListStore.CurrentVersion = 1`.
- Every path quoted in the notes exists. `app.ico` holds 10 frames, and `packaging/linux/icons` holds 8
  PNGs.
- `dotnet build Enigma.GitClient.slnx -c Release`: 0 warnings, 0 errors. Desktop tests compiled.
- The built `Enigma.GitClient.Core.dll` and `Enigma.GitClient.Desktop.dll` carry
  `6.13.0+86042baba866fcad7010f643ab4f3a116579457a`.
- Core integration suite: 414 total, 412 passed, 0 failed, 2 skipped (5 min 42 s).
- Core unit suite: 1,392 total, 1,389 passed, 2 skipped, 1 failed. The failure is
  `TokenStoreTests.AfterTheResetOfAnUnreadableStore_SecretsAreStoredAndReadAgain`, the pre-existing
  Windows-only failure. This dev touches no code.
- Nothing in this dev ran `git tag`, `git push`, `dotnet pack` or `dotnet nuget push`.

## Documentation sweep

- The release documents are this dev's subject. The README *Publish* and *Install on Linux* sections and
  `docs/RELEASE.md` §5–§7 were re-read against 6.13.0 and are current.
