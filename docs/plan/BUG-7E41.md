# BUG-7E41 — App assembly renamed to .Desktop

**Status:** IN PROGRESS
**Type:** BUG
**Branch:** one per phase, see below
**Run:** bugfix/2026-10-01-rename-app-to-desktop

## Objective

The application's assembly is named `Enigma.GitClient.App`. Every other Enigma desktop application
takes the `.Desktop` suffix: `Enigma.HardCopy.Desktop`, `Enigma.MarkdownEditor.Desktop`,
`Enigma.Msi.Desktop`, `Enigma.Licensing.Desktop`, `Enigma.Backup.Desktop`. Rename it
`Enigma.GitClient.Desktop` everywhere: the directory, the project file, the assembly, the root
namespace and every namespace under it, the XAML classes and `using:` namespaces, the `avares://`
resource URIs, the Windows manifest, the solution, the Linux packaging and the living documentation.
Its test project follows the siblings' `<Project>.UnitTests` shape and becomes
`Enigma.GitClient.Desktop.UnitTests`.

## Context & constraints

- **Rename surface.**
  - `src/Enigma.GitClient.App/`: about 160 tracked files. `RootNamespace`, every `namespace`, every
    `using Enigma.GitClient.App…`, every `x:Class` and `xmlns:…="using:Enigma.GitClient.App…"`.
  - `App.axaml`: three `avares://Enigma.GitClient.App/Themes/…` includes.
  - `app.manifest`: `assemblyIdentity name`.
  - The project file: `InternalsVisibleTo Include="Enigma.GitClient.App.UnitTests"`.
  - `tests/Enigma.GitClient.App.UnitTests/`: about 70 files, its `RootNamespace`, its namespaces and
    its `ProjectReference`.
  - `Enigma.GitClient.slnx`: both project paths.
- **Packaging.**
  - `packaging/linux/install.sh` and `uninstall.sh`: `APP_EXE='Enigma.GitClient.App'`, plus the
    publish path and the `--from` help text in `install.sh`.
  - `enigma-git-client.desktop.in`: `StartupWMClass=Enigma.GitClient.App`. Avalonia's X11 backend
    takes the window class from the entry assembly's name, so this must follow the rename for the
    launcher to group the running window. Enigma.MarkdownEditor already ships
    `StartupWMClass=Enigma.MarkdownEditor.Desktop`.
  - The application id `enigma-git-client`, the `enigma-git-client` command, the install directory
    and the icons are not the assembly's name and do not change.
- **No stored data depends on the assembly name.**
  - The configuration folder is the constant `AppPaths.FolderName = "Enigma.GitClient"`.
  - The product name is the constant `ProductInformation.Name`.
  - The version is read from the Core assembly.
  - Settings, the repositories list, profiles, accounts and tokens are found where they were. Nothing
    to migrate.
- **Upgrade path on Linux.**
  - `install.sh` replaces the install directory wholesale and re-points the symlink with `ln -sfn`, so
    an upgrade from a pre-rename install leaves no trace of the old launcher.
  - `uninstall.sh` only removes the symlink when it points at `<install dir>/$APP_EXE`. A pre-rename
    install, removed by the new script before any reinstall, would leave a dangling symlink behind
    unless the former launcher name is also recognised.
- **Inside the new namespace**, the class `App` resolves as before: `Enigma.GitClient.Desktop.App`
  sits where `Enigma.GitClient.App.App` sat. The tests spell it `global::Enigma.GitClient.App.App`
  today, because the namespace `Enigma.GitClient.App` hides the class from inside
  `Enigma.GitClient.App.UnitTests`.
- **Living documentation that names the project:**
  - `README.md`: the start-window feature line, *Build and run*, *Repository layout*.
  - `docs/RELEASE.md`: version statement, the `strings` check, the publish command.
  - The prose comments in `Enigma.GitClient.Core.csproj`, `Directory.Build.props` and
    `ProductInformation.cs`, which say "Core and App" or name the App assembly.
- **Historical records stay as written:** `docs/done/`, `docs/plan/`, the roadmap's footnotes and the
  released sections of `RELEASENOTES.md` describe what was true when they were written.
- Build: zero warnings (`TreatWarningsAsErrors`, `EnforceCodeStyleInBuild`); the suite is xUnit v3 on
  the Microsoft Testing Platform (`dotnet test --solution Enigma.GitClient.slnx`), and the integration
  tests need `git` on the `PATH`.

## PHASE01 — Rename the app project

**Branch:** `bugfix/bug-7e41-phase01-rename-app-project`
**Status:** DONE — see `docs/done/BUG-7E41-PHASE01.md`

### Steps

1. `git mv src/Enigma.GitClient.App src/Enigma.GitClient.Desktop`, then
   `git mv` its `Enigma.GitClient.App.csproj` to `Enigma.GitClient.Desktop.csproj`.
2. In the project: `RootNamespace` → `Enigma.GitClient.Desktop`. `InternalsVisibleTo` keeps naming
   the test assembly, still `Enigma.GitClient.App.UnitTests` in this phase.
3. Every `.cs` and `.axaml` under the project: `Enigma.GitClient.App` → `Enigma.GitClient.Desktop`
   (namespaces, usings, `x:Class`, `using:` xmlns, `avares://` URIs). Then `app.manifest`'s
   `assemblyIdentity name`.
4. `Enigma.GitClient.slnx`: the project path.
5. The test project, which keeps its directory, project file and assembly name in this phase:
   - its `ProjectReference`;
   - its `using Enigma.GitClient.App…` directives and fully qualified references;
   - `global::Enigma.GitClient.App.App`;
   - the launcher paths in `InstanceTests`;
   - *(adapted at build time)* its `RootNamespace` and every namespace, to
     `Enigma.GitClient.Desktop.UnitTests`. The tests reach the app's types by short names (`App`,
     `Views.MainWindow`, `ViewModels.…`, `Controls.…`) through the enclosing namespace. Under
     `Enigma.GitClient.App.UnitTests` those names stop resolving once the app has left
     `Enigma.GitClient.App`.
6. Packaging:
   - `APP_EXE='Enigma.GitClient.Desktop'` in both scripts;
   - the publish path and the `--from` help text in `install.sh`;
   - `StartupWMClass=Enigma.GitClient.Desktop`;
   - `uninstall.sh` also removes a symlink that points at the former launcher
     (`<install dir>/Enigma.GitClient.App`), with a comment saying why.
7. Living docs and comments: the README lines, `docs/RELEASE.md`, and the comments in the Core
   project file, `Directory.Build.props` and `ProductInformation.cs`.
8. A consistency test in the app's test project ties the non-compiled places to the app assembly's
   real name. It checks:
   - the name is `Enigma.GitClient.Desktop`;
   - `APP_EXE` in both scripts;
   - `StartupWMClass`;
   - the manifest's `assemblyIdentity`.
9. Build (zero warnings), whole suite.

### Acceptance criteria

- `src/Enigma.GitClient.Desktop/Enigma.GitClient.Desktop.csproj` builds the assembly
  `Enigma.GitClient.Desktop`.
- `git grep 'Enigma\.GitClient\.App\b'` outside the historical records finds only:
  - the test project's directory, project file and assembly name (renamed in PHASE02);
  - `InternalsVisibleTo`;
  - the legacy launcher name in `uninstall.sh`;
  - the test fixtures or comments that name it as the former name on purpose.
- The packaging and the manifest name the new launcher. The consistency test proves it.
- `git log --follow` sees the moved files as renames: they are moved with `git mv` and their content
  barely changes.
- Build clean with zero warnings; the whole suite green.

## PHASE02 — Rename the test project

**Branch:** `bugfix/bug-7e41-phase02-rename-test-project`
**Status:** TODO

### Steps

1. `git mv tests/Enigma.GitClient.App.UnitTests tests/Enigma.GitClient.Desktop.UnitTests`, then its
   project file to `Enigma.GitClient.Desktop.UnitTests.csproj`.
2. *(Done in PHASE01, see its step 5)* `RootNamespace` and the namespaces are already
   `Enigma.GitClient.Desktop.UnitTests`.
3. `InternalsVisibleTo Include="Enigma.GitClient.Desktop.UnitTests"` in the app's project.
4. `Enigma.GitClient.slnx` and the README's *Repository layout* row.
5. Build (zero warnings), whole suite.

### Acceptance criteria

- `tests/Enigma.GitClient.Desktop.UnitTests/Enigma.GitClient.Desktop.UnitTests.csproj` builds and runs
  the same tests as before, all green.
- `git grep 'Enigma\.GitClient\.App\b'` outside the historical records finds only the legacy launcher
  name in `uninstall.sh` and the places that name the former name on purpose.
- Build clean with zero warnings; the whole suite green.

## Out of scope

- Rewriting historical records: `docs/done/`, `docs/plan/`, roadmap footnotes, released
  `RELEASENOTES.md` sections.
- Renaming the `App` class (`App.axaml`), which is the Avalonia convention and what the sibling
  Desktop apps use.
- The configuration folder, the product name, the Linux application id `enigma-git-client`.
- `Enigma.Tasks`, which also uses `.App`: another repository.
- A release-notes entry: the next release's notes are written at release time.
- Line endings.

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| Type | BUG | A convention defect: the one Enigma app out of step with the others. The prompt asks to fix it | FEATURE |
| Rename the test project too | Yes: `Enigma.GitClient.Desktop.UnitTests` | The siblings' test projects follow their app (`Enigma.HardCopy.Desktop.UnitTests`, `Enigma.MarkdownEditor.Desktop.UnitTests`); `tests/<Project>.UnitTests` is the house shape | Leave `Enigma.GitClient.App.UnitTests`, which keeps the old name alive |
| Breakdown | Two phases (app project, then test project), each green | One reviewable commit each. The test project's rename is independent once the app's is in, and halves the diff to review | One dev for all; splitting packaging/docs out, which would leave an intermediate commit whose installer publishes a path that no longer exists |
| Historical records | Leave as written | They record what was true at the time; rewriting them falsifies the history and swells the diff | A blanket replace across `docs/` |
| Linux packaging | New launcher name and window class; same app id, command and install dir. `uninstall.sh` also recognises the former launcher's symlink | The scripts must find what `dotnet publish` now produces. The window class follows the entry assembly. Users keep the command they know. A pre-rename install is removed cleanly | Renaming the app id (a pointless break for users); no legacy handling (a dangling symlink after uninstalling an old install) |
| User data | No migration | Nothing stored is keyed by the assembly name | A migration step for a folder that does not move |
| The `App` class | Keep it | Avalonia convention, and the sibling Desktop apps keep it | `DesktopApp` or similar |
| Regression guard | A consistency test tying `APP_EXE`, `StartupWMClass` and the manifest to the assembly's name | Those are the places the compiler cannot check. `DisplayNameConsistencyTests` already guards the product name this way | No test; a blanket "former name" sweep (it would trip on the legacy name the uninstaller must keep) |
| Release notes | No entry now; follow-up recorded | Notes are written per release from the completion records (`docs/RELEASE.md`) | An `unreleased` section now |
