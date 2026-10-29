# FEATURE-5096 — App icon in the output directory

**Status:** TODO
**Type:** FEATURE
**Branch:** `feature/feature-5096-icon-in-output-dir`
**Run:** feature/2026-10-29-uniform-enigma-apps

## Objective

The application icon is written to the build output directory — and so to the publish directory — next
to `Enigma.GitClient.Desktop(.exe)`, as `app.ico`. Part of the uniform baseline for every Enigma app:
shortcuts, launchers and installers can point at a file instead of extracting it from the executable.

## Context & constraints

- `src/Enigma.GitClient.Desktop/Assets/app.ico` (ten PNG frames, 16–256) is the `ApplicationIcon` and
  the windows' `Icon`. `Assets/**` is an `AvaloniaResource` (embedded), so nothing lands in `bin/` today.
- Items with `CopyToOutputDirectory` are also published, and flow to referencing projects' output (the
  Desktop test project).
- `AppIconTests` (Desktop tests) pins the icon's frames and its copies.
- Desktop tests are compiled, never run, on this Windows machine (user rule).

## Steps

1. `Enigma.GitClient.Desktop.csproj`: `Assets/app.ico` copied to the output root as `app.ico`
   (`PreserveNewest`, `TargetPath="app.ico"`), with a one-line comment saying why. It stays an
   `AvaloniaResource` too.
2. `AppIconTests`: a test that `app.ico` sits in `AppContext.BaseDirectory`, byte for byte the
   embedded asset.
3. Verify: a Release build puts `app.ico` in `bin/Release/net10.0/`; a `win-x64` publish puts it in
   `publish/`; both byte-identical to `Assets/app.ico`.

## Acceptance criteria

- `bin/<Configuration>/net10.0/app.ico` and the publish folder's `app.ico` exist, byte for byte
  `Assets/app.ico`.
- The embedded resource still resolves (`avares://…/Assets/app.ico` unchanged).
- The Release build has zero warnings (Desktop tests compiled); the Core suites are green.

## Out of scope

- The PNG copy (`app-256.png`) and the Linux hicolor icons (installed by `install.sh`).

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| Which file | `app.ico` only | "The app icon" is the `ApplicationIcon`; the narrowest reading. Linux launchers already get PNGs from `install.sh` | Also copying `app-256.png` |
| Name and place | `app.ico` at the output root | Every Enigma app's asset is `Assets/app.ico`: one predictable name across apps | `Assets/app.ico` sub-folder; `Enigma.GitClient.Desktop.ico` |
| Mechanism | A `None`/`Content` item with `CopyToOutputDirectory` + `TargetPath` | Standard MSBuild; flows to publish with no custom target | A custom `Copy` target after build and publish |
| Test | One Desktop test (compiled here) + a manual build/publish check | Pins the copy where the other icon copies are pinned | A Core test (wrong project for an app asset) |
