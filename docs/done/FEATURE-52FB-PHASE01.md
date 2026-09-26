# FEATURE-52FB-PHASE01 — Avalonia shell, theme & DI

**Item:** FEATURE-52FB — App shell & repository opening
**Branch:** `feature/feature-52fb-phase01-app-shell`
**Run:** feature/2026-09-15-enigma-git-client

## Summary

The application now exists as an application: an Avalonia 12 window with the Enigma Fluent theme, an
`IHost` composition root, the navigation rail with six pages, the repository strip, the
operation-in-progress banner, the three overlay hosts, and a startup check that explains itself when
git is missing. The shell has been rendered off-screen and looked at — it is not just "compiles and
lays out".

## Files / modules touched

**Created — `src/Enigma.GitClient.App/`**

- `Services/IRepositoryContext.cs`, `Services/RepositoryContext.cs` — the one open repository, its
  reference state, a lifetime token cancelled on every repository change, and the write lock that
  serialises every mutating operation
- `ViewModels/ViewModelBase.cs`, `ViewModels/PageViewModelBase.cs`, `ViewModels/MainWindowViewModel.cs`
- `ViewModels/Pages/*PageViewModel.cs` — History, Changes, Branches, Remotes, Integrations, Settings
- `Views/MainWindow.axaml(.cs)`, `Views/Pages/*PageView.axaml(.cs)`
- `Controls/EmptyState.cs` — the shared "nothing here yet, and here is what to do" control
- `Themes/Controls.axaml` — the `EmptyState` control theme
- `Themes/Styles.axaml` — toolbar buttons, the badge pill, the monospace class
- `Navigation/ContainerPageFactory.cs` — pages resolved from DI instead of `Activator`
- `DependencyInjection/ServiceCollectionExtensions.cs` — `AddEnigmaDesktopServices`, `AddGitClientApp`
- `Assets/app.ico` — a six-resolution icon (16/32/48/64/128/256) of the commit-graph mark

**Modified**

- `App.axaml` / `App.axaml.cs` — theme merge, style include, monospace font resource, the host, the
  five host registrations, and shutdown
- `Enigma.GitClient.App.csproj` — `<ApplicationIcon>`
- `Directory.Packages.props` — `Avalonia.Skia` (test-only, same coupled version)
- `docs/roadmap.md`, `docs/plan/FEATURE-52FB.md`

**Created — tests (`tests/Enigma.GitClient.App.UnitTests/`)**

- `Infrastructure/HeadlessAvaloniaFixture.cs` — one headless Avalonia platform per run, on its own
  UI thread, with a work queue
- `Infrastructure/FakeRefReader.cs` — synchronous, in-memory reference state
- `CompositionRootTests.cs` (24) — container validation, lifetimes, every service resolves
- `RepositoryContextTests.cs` (15) — open/close/refresh, lifetime cancellation, exclusive serialisation
- `ShellRenderTests.cs` (11) — theme keys, the `EmptyState` template, every page lays out
- `MainWindowShellTests.cs` (9) — the window, the rail, the page factory, the strip, the banner
- `ShellSnapshotTests.cs` (3) — the window rendered off-screen to a PNG and measured

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| `RequestedThemeVariant` | **`Dark`**, not `Default` | Found by rendering: Enigma.Avalonia.Desktop's dictionary defines `Dark` and `Light` theme dictionaries **only**. Under `Default` on a host whose platform theme cannot be resolved, every `Enigma*` brush resolves to nothing and the window paints almost nothing. Dark is also the right default for this kind of tool; "follow the system" becomes a setting in FEATURE-5D77 |
| `InitializeComponent` | Let the XAML compiler generate it | A hand-written one that only calls `AvaloniaXamlLoader.Load` compiles fine and leaves every `x:Name`d field **null** — so all three overlay hosts would have been null at `RegisterHost` and every dialog would have thrown at runtime. A test now pins that the three hosts are non-null and are the last children of the root panel |
| Off-screen rendering | Added `Avalonia.Skia` (test-only) and switched the headless platform off the no-op renderer | Layout tests prove a tree measures; only a rendered frame proves it was **drawn**. This is how the `Default`-variant defect above was found, and it gives every later UI dev a way to actually look at its work |
| What a snapshot asserts | Distinct-colour count, not just painted coverage | The first version passed a completely blank white frame. Counting distinct quantised colours is what actually distinguishes "drew the shell" from "drew one flat rectangle" |
| Snapshot determinism | Every snapshot pins the theme variant | A snapshot that inherits the ambient variant depends on the host's OS theme, which is not a test |
| Startup git check | On `Window.Opened`, through the content dialog | The dialog needs its host registered and the window on screen. Doing it earlier would mean a service call before `RegisterHost`, which throws by design |
| `async void` | Exactly one, on `Window.Opened`, delegating immediately to a `Task`-returning method | The sanctioned exception in the house async policy, and it is commented as such |
| Page lifetimes | Views transient, ViewModels singleton | A revisited page gets a fresh control and the state it had. A test asserts both |
| Repository write lock | On `IRepositoryContext`, not on each service | Every future service (branch, tag, merge, sync) writes to the same repository; one lock in one place is the only way that stays true |
| Repository swap safety | A lifetime token per repository + a staleness check before publishing a read | Without it, a slow read against the previous repository can overwrite the new one's state |
| `Themes/Styles.axaml` | Added | FluentTheme paints a filled plate on every button state, so the two icon-only toolbar buttons rendered as grey boxes floating on the strip. Visible in the first snapshot, fixed, and visible as fixed in the next |

## Deviations & follow-ups

- **Deviation (additive):** `Avalonia.Skia`, `ShellSnapshotTests` and the monospace font resource were
  not in the plan. The first two paid for themselves immediately by exposing the `Default`-variant
  defect; the third is needed by the diff viewer and the short-hash column and belongs with the theme.
- **Deviation:** the plan said the shell would be verified "headlessly by constructing the window and
  running one layout pass, plus a manual smoke run". The headless verification is stronger than
  planned (a real rendered frame). A manual run on the developer's own desktop was **not** performed:
  it would put a window on the user's screen, and the off-screen render answers the same question
  better. `dotnet run --project src/Enigma.GitClient.App` is documented in the README.
- **Follow-up:** the six pages are real views with real empty states, but their content arrives with
  their owning work items (FEATURE-2326 onwards).
- **Follow-up:** the navigation rail keeps its dark treatment in the Light variant — that is
  Enigma.Avalonia.Desktop's own `NavigationView` theme, not an override of ours.
- **Follow-up:** `Avalonia.Headless.XUnit` was not used; its `[AvaloniaTest]` attribute targets a
  different xUnit generation. The fixture here owns one UI thread and a work queue instead, which
  also keeps the non-UI tests off it.
- **Line endings (recommendation only):** no CRLF churn observed. No action taken.

## Build/test evidence

```
dotnet build Enigma.GitClient.slnx
  0 Warning(s)
  0 Error(s)

dotnet test --solution Enigma.GitClient.slnx
  Test run summary: Passed!
  total: 473  failed: 0  succeeded: 473  skipped: 0
```

62 tests are new in this dev. The warning count was read explicitly (0), which also covers the
`AVLN*` XAML warnings that `TreatWarningsAsErrors` does not promote.

**Fix cycles.** Three were used, and each found a real defect rather than churning:

1. The generated-`InitializeComponent` defect (all three overlay hosts null) plus a test that
   expected a `PropertyChanged` for a record value that had genuinely not changed.
2. The snapshot assertion passed a blank white frame; it was strengthened to count distinct colours,
   which then failed correctly.
3. The blank frame's cause: `RequestedThemeVariant="Default"`. Fixed in the product (the app now
   starts Dark) and in the tests (every snapshot pins its variant).

The suite was green at the end of cycle 3, so no quarantine was needed.
