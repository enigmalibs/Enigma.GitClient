# BUG-7E41-PHASE01 — Rename the app project

**Item:** BUG-7E41 — App assembly renamed to .Desktop
**Branch:** `bugfix/bug-7e41-phase01-rename-app-project`
**Run:** bugfix/2026-10-01-rename-app-to-desktop

## Summary

The application is now `Enigma.GitClient.Desktop`, like every other Enigma desktop application. That
covers its directory, project file, assembly, root namespace and every namespace under it, the XAML
classes, the `using:` namespaces, the `avares://` URIs and the Windows manifest. The launcher the build
produces is `Enigma.GitClient.Desktop`, and the window class Avalonia takes from it is the same name.

- **A pure rename.** The directory and the project file were moved with `git mv`, so every file is
  seen as a rename. The only content changes are:
  - the `Enigma.GitClient.App` → `Enigma.GitClient.Desktop` lines;
  - the renamed `using` directives, moved to their alphabetical place (after
    `Enigma.GitClient.Core.*` now, no longer before it). Every other `using` line stays where it was.
- **The test project's namespace moved with the app** (`Enigma.GitClient.Desktop.UnitTests`). Its
  directory, project file and assembly name stay for PHASE02. See *Deviations*.
- **Linux packaging.**
  - `install.sh` publishes `src/Enigma.GitClient.Desktop` and looks for the `Enigma.GitClient.Desktop`
    launcher. `uninstall.sh` names the same launcher.
  - The desktop entry expects `StartupWMClass=Enigma.GitClient.Desktop`, so the launcher still groups
    the running window.
  - The `enigma-git-client` command, the install directory and the icons are unchanged.
- **Upgrading from 5.1.0 is clean.**
  - `install.sh` replaces the install directory and re-points the symlink, so no old launcher is left.
  - `uninstall.sh` now also removes a symlink pointing at the former launcher
    (`FORMER_APP_EXE='Enigma.GitClient.App'`). Without that, removing an old installation with the
    new script would leave a dangling `~/.local/bin/enigma-git-client`.
- **Nothing stored moves.** The configuration folder (`AppPaths.FolderName`) and the product name
  (`ProductInformation.Name`) are constants, not the assembly's name. Settings, repositories,
  profiles, accounts and tokens are found where they were.
- **A guard for the places the compiler cannot check.** `AssemblyNameConsistencyTests` ties the
  following to the name of the assembly the build really produces:
  - the project directory;
  - `APP_EXE` in both scripts;
  - the desktop entry's `StartupWMClass`;
  - the manifest's `assemblyIdentity`.

  It also asserts the name is `Enigma.GitClient.Desktop`.

## Files / modules touched

**Renamed** (`git mv`, content otherwise unchanged except the namespace lines)

- `src/Enigma.GitClient.App/` → `src/Enigma.GitClient.Desktop/` (all 144 tracked files), with
  `Enigma.GitClient.App.csproj` → `Enigma.GitClient.Desktop.csproj`.

**Modified**

- `src/Enigma.GitClient.Desktop/**`: in `.cs` and `.axaml`:
  - `namespace`, `using`, `x:Class` and `using:` xmlns;
  - `App.axaml`'s three `avares://` includes;
  - `app.manifest`'s `assemblyIdentity`;
  - `RootNamespace`;
  - `InstanceLauncher`'s remark (`dotnet Enigma.GitClient.Desktop.dll`).

  `InternalsVisibleTo` still names `Enigma.GitClient.App.UnitTests`, which is the test assembly's name
  until PHASE02.
- `tests/Enigma.GitClient.App.UnitTests/**`:
  - `RootNamespace` and every namespace → `Enigma.GitClient.Desktop.UnitTests`;
  - the `ProjectReference`;
  - the `using` directives;
  - the fully qualified references (`ApplicationBootstrapTests`, `ConflictResolutionPageTests`,
    `HistoryStashTests`, `MenuIconTests`);
  - the launcher paths in `InstanceTests`.
- `Enigma.GitClient.slnx`: the app's project path.
- `packaging/linux/install.sh`: `APP_EXE`, the publish path, the `--from` help.
- `packaging/linux/uninstall.sh`: `APP_EXE`, and `FORMER_APP_EXE` accepted as a symlink target.
- `packaging/linux/enigma-git-client.desktop.in`: `StartupWMClass`.
- Prose comments that named the app:
  - `src/Enigma.GitClient.Core/Enigma.GitClient.Core.csproj`;
  - `Directory.Build.props` ("Core and Desktop");
  - `src/Enigma.GitClient.Core/Diagnostics/ProductInformation.cs`.
- `README.md`, `docs/RELEASE.md`: see *Documentation sweep*.
- `docs/roadmap.md`, `docs/plan/BUG-7E41.md`: statuses. The plan also gets the adapted PHASE01 step 5
  and PHASE02 step 2.

**Created**

- `tests/Enigma.GitClient.App.UnitTests/AssemblyNameConsistencyTests.cs`
- `docs/done/BUG-7E41-PHASE01.md`

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| Where the phase boundary falls | The test project's namespace moves in PHASE01. Its directory, project file and assembly name stay for PHASE02 | The tests reach the app's types by short names through the enclosing namespace (`App.ConfigureServices`, `Views.MainWindow`, `ViewModels.MainWindowViewModel`, `Controls.AuthorAvatar`: 33 references in 11 files). Under `Enigma.GitClient.App.UnitTests` they stop resolving once the app has left `Enigma.GitClient.App`. Qualifying them all, only to shorten them again in PHASE02, would be throwaway churn |
| Order of the renamed `using` lines | Moved to their alphabetical place; every other line left where it was | `Desktop` sorts after `Core`, where `App` sorted before it. A blanket sort would also have "fixed" a few unrelated, pre-existing quirks (e.g. `System.Linq` before `System.Collections.ObjectModel` in `TagsPageViewModel`) and grown the diff |
| How the uninstaller recognises an old installation | A second accepted target, `FORMER_APP_EXE`, in the same exact-path check | The guard stays as strict as it was: only a symlink to one of our two launchers, inside our install directory, is removed. Dry-run below |
| How the new test reaches the assembly | `typeof(App).Assembly` | In `Enigma.GitClient.Desktop.UnitTests`, `App` resolves to the application class, as it does in the other tests |
| The `bin/` and `obj/` directories | Left alone | They are ignored build output and moved with the directory. The build regenerated what it needs. Deleting them is not a git operation this run performs |

## Deviations & follow-ups

- **Phase boundary moved** (see the first decision). The plan file's PHASE01 step 5 and PHASE02 step 2
  record it. PHASE02 keeps the test project's directory, project file, assembly name,
  `InternalsVisibleTo`, the solution entry and the README row.
- **Release notes:** the launcher's name changes. Anyone who starts `Enigma.GitClient.App <path>`
  directly, rather than through `enigma-git-client`, must use `Enigma.GitClient.Desktop <path>`. Worth
  a line in the next release's notes. `RELEASENOTES.md` is written at release time, and its released
  sections stay as written.
- **Elsewhere:** `Enigma.Tasks` still uses `Enigma.Tasks.App` (another repository).
- **Stale build output:** the old `obj/` content (e.g. `Enigma.GitClient.App.UnitTests.AssemblyInfo.cs`
  under `obj/Release`) is ignored and harmless. `dotnet clean` or deleting `bin/` and `obj/` tidies it.
- Line endings: no CRLF churn (the repository normalises to LF).

## Documentation sweep

- `README.md`:
  - the start-window feature line now says `Enigma.GitClient.Desktop <path>`;
  - *Build and run* runs `src/Enigma.GitClient.Desktop`;
  - the *Repository layout* row is `src/Enigma.GitClient.Desktop`;
  - the test project's row stays until PHASE02.
- `docs/RELEASE.md`: the version statement, the `strings` check and the publish command name
  `Enigma.GitClient.Desktop`.
- No `CLAUDE.md`/`AGENTS.md` in the repository. `SECURITY.md` does not name the project.

## Build/test evidence

- First build after the rename: 33 errors, all CS0234/CS0118/CS0246/CS0103 in the test project. They
  were the short references described above. Fix cycle 1 moved the test namespace and cleared them.
  One more error was the new test file: untracked, so the first `git grep` replace had skipped it.
  Corrected in the same cycle.
- `dotnet build Enigma.GitClient.slnx --no-incremental`: 0 warnings, 0 errors. The output is
  `src/Enigma.GitClient.Desktop/bin/Debug/net10.0/Enigma.GitClient.Desktop.dll` and its
  `Enigma.GitClient.Desktop` launcher.
- `dotnet test --solution Enigma.GitClient.slnx`: **2647 passed**, 0 failed, 0 skipped (5 new, in
  `AssemblyNameConsistencyTests`). The headless render tests load `App.axaml` and its `avares://`
  includes, so the renamed resource URIs are exercised.
- `uninstall.sh` dry-run against a fake XDG tree, once per symlink target:
  - former launcher → removed;
  - new launcher → removed;
  - a foreign target → left alone with the usual message.

  `bash -n` is clean on both scripts.
- `git grep 'Enigma\.GitClient\.App\b'` outside the historical records finds only:
  - the test project's directory/project in `Enigma.GitClient.slnx` and the README row;
  - `InternalsVisibleTo`;
  - `FORMER_APP_EXE`.
- Fix budget: 1 cycle used.
