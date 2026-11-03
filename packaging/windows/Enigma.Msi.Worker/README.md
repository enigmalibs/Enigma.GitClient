# Enigma.Msi worker CLI (vendored)

A copy of the headless worker of Enigma.Msi, the Enigma tool that builds Windows MSI installers with
WixSharp (WiX v4 API). `packaging/windows/buildmsi.ps1` runs it to build
`artifacts/Enigma.GitClient.X.Y.Z.msi`:

```text
Enigma.Msi.Worker.exe build <file.msipkg.json>
```

It is committed rather than built so the installer build has nothing extra to compile. The worker is a
`net472` program, and WixSharp is .NET Framework only; neither belongs in a solution that is also built
on Linux.

Exit codes: `0` the MSI was written (its path is printed); `1` the profile is invalid or the WiX build
failed (every reason is printed); `2` bad arguments or an unreadable profile. Enigma.Msi's
`docs/guides/worker-cli.md` documents the CLI, and `docs/guides/model.md` documents the profile.

## Source

| | |
|---|---|
| Repository | Enigma.Msi, tag `1.7.0` (commit `a7a1bfb`) |
| Folder | `src/Enigma.Msi.Worker/bin/Release/net472/` |
| WixSharp | `WixSharp_wix4` 2.14.2 |
| Left out | `*.pdb`, `Enigma.Msi.xml`, `.wixsharp/` |

The files are the build output unchanged, except `Enigma.Msi.Worker.exe.config`. Git stores that file
with LF line endings (`.gitattributes`); the build writes CRLF.

## Requirements

- Windows with .NET Framework 4.7.2 or later.
- The WiX CLI on the `PATH`: `dotnet tool install --global wix`. WiX 7 also needs its licence accepted
  once (`wix eula accept wix7`).

## `.wixsharp/`

On its first build, WixSharp writes a `.wixsharp/` folder beside the worker: the files it packages for
the installer's managed UI. That folder is git-ignored. `git clean -dxf` removes it, and the next build
writes it again.

## Licences

| Files | Project | Licence |
|---|---|---|
| `Enigma.Msi.Worker.exe` (+ `.config`), `Enigma.Msi.dll` | Enigma.Msi | MIT |
| `WixSharp.dll`, `WixSharp.Msi.dll`, `WixSharp.UI.dll`, `WixSharp.UI.WPF.dll`, `WixSharp.MsiEventHost.exe` | WixSharp_wix4 2.14.2 | MIT |
| `WixToolset.Dtf.WindowsInstaller.dll`, `WixToolset.Mba.Core.dll`, `mbanative.dll` | WiX Toolset 4.0.1 | MS-RL |
| `System.*.dll`, `Microsoft.Bcl.AsyncInterfaces.dll` | .NET | MIT |

None of these files goes into the MSI; they only build it.

## Refreshing it

When Enigma.Msi releases a new worker:

1. In Enigma.Msi, on the release tag: `dotnet build src/Enigma.Msi.Worker/Enigma.Msi.Worker.csproj -c Release`.
2. Replace this folder's files with `src/Enigma.Msi.Worker/bin/Release/net472/*`, leaving out `*.pdb`,
   `Enigma.Msi.xml` and `.wixsharp/`. Keep this README.
3. Update the *Source* table, then build an MSI with `packaging/windows/buildmsi.ps1`.
