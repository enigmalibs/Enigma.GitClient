# BUG-7E41-PHASE02 — Rename the test project

**Item:** BUG-7E41 — App assembly renamed to .Desktop
**Branch:** `bugfix/bug-7e41-phase02-rename-test-project`
**Run:** bugfix/2026-10-01-rename-app-to-desktop

## Summary

The app's test project is now `Enigma.GitClient.Desktop.UnitTests`: its directory, its project file and
its assembly. It follows the siblings' `<Project>.UnitTests` shape (`Enigma.HardCopy.Desktop.UnitTests`,
`Enigma.MarkdownEditor.Desktop.UnitTests`). Its namespace had already moved in PHASE01. This phase is
the move itself, plus the references to the project's name. With it, `Enigma.GitClient.App` is gone
from everything outside the historical records, except the uninstaller's deliberate
`FORMER_APP_EXE`.

## Files / modules touched

**Renamed** (`git mv`, content unchanged)

- `tests/Enigma.GitClient.App.UnitTests/` → `tests/Enigma.GitClient.Desktop.UnitTests/` (all 72
  tracked files), with `Enigma.GitClient.App.UnitTests.csproj` →
  `Enigma.GitClient.Desktop.UnitTests.csproj`.

**Modified**

- `src/Enigma.GitClient.Desktop/Enigma.GitClient.Desktop.csproj`:
  `InternalsVisibleTo Include="Enigma.GitClient.Desktop.UnitTests"`, the test assembly's new name.
- `Enigma.GitClient.slnx`: the test project's path.
- `README.md`: the *Repository layout* row.
- `docs/roadmap.md`, `docs/plan/BUG-7E41.md`: statuses (the phase, and the item with it).

**Created**

- `docs/done/BUG-7E41-PHASE02.md`

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| `global::Enigma.GitClient.Desktop.App` in `ApplicationBootstrapTests` | Left fully qualified | It was fully qualified before the rename, and it still compiles and reads unambiguously. Shortening it is a style change this rename does not need |
| The README layout table's padding | The renamed row fills the column exactly (42 characters) | The table was already ragged on two rows (the Core engine's description and the integration tests' path). Only the touched row is aligned |

## Deviations & follow-ups

- **PHASE02 step 2 was already done** in PHASE01 (the namespace move, see its completion record). This
  phase kept the directory, project file, assembly name, `InternalsVisibleTo`, solution entry and
  README row, as the adapted plan says.
- **Stale build output:** `tests/Enigma.GitClient.Desktop.UnitTests/obj/` still holds ignored files
  generated under the old assembly name (e.g. `Enigma.GitClient.App.UnitTests.AssemblyInfo.cs`). The
  build does not compile them. `dotnet clean` or deleting `bin/` and `obj/` tidies them.
- Line endings: no CRLF churn.

## Documentation sweep

- `README.md`: the *Repository layout* row now names `tests/Enigma.GitClient.Desktop.UnitTests`.
- `docs/RELEASE.md` and the other prose docs do not name the test project. No other edit.

## Build/test evidence

- `dotnet build Enigma.GitClient.slnx --no-incremental`: 0 warnings, 0 errors.
- `dotnet test --solution Enigma.GitClient.slnx`: **2647 passed**, 0 failed, 0 skipped. That is the
  same count as PHASE01, now reported by `Enigma.GitClient.Desktop.UnitTests.dll`. The internals the
  tests reach (`Program`, `App.ConfigureServices`, `InstanceLauncher.BuildStartInfo`) compile against
  the new `InternalsVisibleTo`.
- `git grep 'Enigma\.GitClient\.App\b'` outside `docs/done/`, `docs/plan/`, `docs/roadmap.md` and
  `RELEASENOTES.md` finds only `packaging/linux/uninstall.sh: FORMER_APP_EXE`.
- Fix budget: 0 cycles used.
