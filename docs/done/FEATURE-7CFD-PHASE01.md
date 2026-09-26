# FEATURE-7CFD-PHASE01 — Solution scaffolding & config

**Item:** FEATURE-7CFD — Solution foundation & git engine
**Branch:** `feature/feature-7cfd-phase01-scaffolding`
**Run:** feature/2026-09-15-enigma-git-client

## Summary

Bootstrapped the whole solution from the house templates: git hygiene files, the full C#
`.editorconfig`, solution-wide build defaults, Central Package Management, the SDK pin with the
Microsoft Testing Platform runner, the MIT licence, the `.slnx`, five projects, and a smoke test per
test project. The App project carries a deliberately minimal Avalonia bootstrap whose only job today
is to prove the Avalonia 12.1.1 + Enigma.Avalonia.Desktop + Enigma.Icons.Avalonia package graph
restores and that the theme dictionary merges; the real shell arrives with FEATURE-52FB.

## Files / modules touched

**Created**

- `.gitignore`, `.gitattributes` — from `git-repo-hygiene`
- `.editorconfig` — the full C# style/naming/analyzer file from `dotnet-solution-config`
- `Directory.Build.props` — solution-wide defaults, authored for Josué Clément / 2026
- `Directory.Packages.props` — CPM with the version-coupled Avalonia set pinned at 12.1.1
- `global.json` — SDK 10.0.100 `latestFeature` + `test.runner: Microsoft.Testing.Platform`
- `LICENSE.md` (MIT), `RELEASENOTES.md` (empty placeholder)
- `Enigma.GitClient.slnx` — `/src/` and `/tests/` solution folders
- `src/Enigma.GitClient.Core/Enigma.GitClient.Core.csproj` + `Diagnostics/ProductInformation.cs`
- `src/Enigma.GitClient.App/Enigma.GitClient.App.csproj`, `app.manifest`, `Program.cs`,
  `App.axaml`, `App.axaml.cs`
- `tests/Enigma.GitClient.Core.UnitTests/` + `ProductInformationTests.cs`
- `tests/Enigma.GitClient.Core.IntegrationTests/` + `GitAvailabilityTests.cs`
- `tests/Enigma.GitClient.App.UnitTests/` + `ApplicationBootstrapTests.cs`

**Modified**

- `README.md` — replaced the placeholder with the real product README (features, the two non-goals,
  requirements, build/run, repository layout)
- `docs/roadmap.md`, `docs/plan/FEATURE-7CFD.md` — status updates

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| A fifth project, `Enigma.GitClient.App.UnitTests`, was not in the plan | Created it now | Every UI work item from FEATURE-52FB onward needs somewhere to put ViewModel and headless render tests; adding it during scaffolding is cheaper than retrofitting a project later, and it completes the layout the plan describes |
| `GenerateDocumentationFile` on Core under `TreatWarningsAsErrors` | Kept it on | It makes CS1591 a build error, so every public member of the engine carries XML documentation by construction. That is the house library template's default and worth the typing |
| Avalonia version | 12.1.1, not the newer 12.1.2 | `Enigma.Avalonia.Desktop` 1.0.0's nuspec pins Avalonia 12.1.1 and CommunityToolkit.Mvvm 8.4.2; matching the set it was built and tested against removes an avoidable variable. Bumping the whole coupled set together is its own future work item |
| `Avalonia.Headless` declared in CPM but not yet referenced | Declared now | It belongs to the same version-coupled set as the rest of Avalonia, so pinning it in the same place keeps the coupling visible; the App test project references it from this dev onward |
| The `Enigma.Avalonia` ↔ `Avalonia` namespace collision | Avoided structurally | Inside a namespace starting with `Enigma.`, an inline `Avalonia.Media.IBrush` binds to `Enigma.Avalonia` and fails to compile. Every `using` in the app sits at file scope above the namespace, no type is written as an inline `Avalonia.Xxx` reference, and the rule is documented in `App.axaml.cs` |
| App bootstrap scope for this phase | A code-only `Application` with the theme merged and a bare window | Proves the package graph and the `ResourceInclude` wiring immediately instead of discovering a restore problem five devs later; FEATURE-52FB replaces it wholesale |

## Deviations & follow-ups

- **Deviation:** the plan listed four projects; five were created (see the decision table).
- **Follow-up:** `src/Enigma.GitClient.App/Assets/` does not exist yet — the `AvaloniaResource`
  glob matches nothing until FEATURE-52FB adds `app.ico`. `<ApplicationIcon>` is deliberately not
  set yet, because pointing it at a missing file fails the build.
- **Line endings (recommendation only):** `.gitattributes` now declares `* text=auto eol=lf`. If the
  working tree ever shows CRLF↔LF churn, `git add --renormalize .` followed by a dedicated commit is
  the fix. No action was taken as part of this dev.

## Build/test evidence

```
dotnet build Enigma.GitClient.slnx
  Build succeeded.
      0 Warning(s)
      0 Error(s)

dotnet test --solution Enigma.GitClient.slnx
  Test run summary: Passed!
  total: 4  failed: 0  succeeded: 4  skipped: 0
```

Warning count read explicitly (0), which also covers the `AVLN*` XAML warnings that
`TreatWarningsAsErrors` does not promote.
