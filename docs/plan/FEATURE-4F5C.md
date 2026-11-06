# FEATURE-4F5C — Repository tiles: menu and icon

**Status:** IN PROGRESS
**Type:** FEATURE
**Branch:** one per phase, see below
**Run:** feature/2026-11-06-repo-tiles-history-search

## Objective

The start window's repository tiles get easier to read and to use:

- the path under the repository's name is legible, not drawn like disabled text;
- the glyph left of the name is 24 px instead of 20;
- a right-click on a tile (or Shift+F10 / the menu key on it) opens a menu, every item with an icon:
  Open · Open in a new window · — · Open the repository's folder · Open a terminal in the repository's
  folder · Copy repository path · — · Forget this repository · — · Choose an icon… (· Use the default
  icon, while a custom icon is set);
- the glyph can be replaced by an image of the user's choosing — PNG, JPEG, BMP, GIF, ICO, WebP or
  SVG — kept for the repository whatever profile lists it.

## Context & constraints

- **The tile** is the `RepositoryTile` `DataTemplate` in
  `src/Enigma.GitClient.Desktop/Views/Pages/RepositoriesPageView.axaml`, used twice: by the list
  (`RepositoryList`) and by the drag copy (`DragGhost`). Its `DataContext` is a
  `Services.ListedRepository` (record: `Path`, `Name`, `[JsonIgnore] Exists`); its buttons reach the
  page through `$parent[ItemsControl].((vm:RepositoriesPageViewModel)DataContext)`.
- **The glyph** is `<ei:Icon Kind="Folder" Size="20" Classes="dim" />`; the path is a `TextBlock`
  with `Classes="faint"` (`EnigmaForegroundTertiaryBrush`), which in the dark theme is the grey
  `Styles.axaml` already calls "too close to the background to read" (the `refdetail` comment). `dim`
  is the secondary foreground, legible in both themes.
- **The icon scale** (`Themes/Styles.axaml`, top): an icon names its role (`title`, `command`,
  `header`, `toolbar`, `row`, `pill`, `menu`) and the role sets its size.
- **The commands** already exist on `RepositoriesPageViewModel`: `OpenListedCommand`,
  `OpenInNewWindowCommand`, `OpenFolderCommand`, `OpenTerminalCommand`, `ForgetCommand` (each takes
  the `ListedRepository`). A copy goes through `ISystemInterop.CopyTextAsync`. The file picker is the
  library's `IFileDialogService` (registered; its storage provider is set per window in `AppWindows`).
- **Dragging** (`RepositoriesPageView.axaml.cs`): a row is dragged by its body. A right-click must open
  the menu and never start a drag.
- **Context menus elsewhere** carry `ei:Icon Classes="menu"` icons, and `MenuIconTests` checks every
  menu item has one; the History line menu shows how a menu inside a template binds to the page.
- **Storage:** `IAppPaths.ConfigurationDirectory` (`$XDG_CONFIG_HOME/Enigma.GitClient`, `%APPDATA%\…`).
  Lists are per profile (`repository-lists.json`, `RepositoryListStore`); `AddAsync` rewrites an
  existing entry, so anything stored on the entry would be lost when a listed repository is added
  again — the icon is kept apart, by repository path.
- **SVG:** `Enigma.Icons.SvgIconParser.Parse(Stream)` (already referenced) parses a static subset
  (paths, basic shapes, groups, transforms, fill/stroke, opacity), closes XXE, caps documents at 1 MiB,
  and keeps each layer's raw `Fill` / `Stroke` strings. Gradients, text, `<use>`, clip paths and
  embedded images are not drawn.
- **Raster:** Avalonia's `Bitmap` decodes through Skia: PNG, JPEG, BMP, GIF (first frame), ICO, WebP.

## PHASE01 — Legible path, icon at 24

**Branch:** `feature/feature-4f5c-phase01-tile-path-icon-size`
**Status:** DONE

### Steps

1. `Themes/Styles.axaml`: add the `tile` role to the icon scale (24) and its line in the scale's
   comment ("a repository tile's leading glyph").
2. `RepositoriesPageView.axaml`: the tile's folder glyph takes `Classes="tile dim"` and no `Size`; the
   path's `TextBlock` takes `Classes="dim"` instead of `faint`. Keep the tile's height reasonable (the
   glyph grows by 4 px; the two text lines are taller than it).
3. Tests (`RepositoriesPageTests` or a new `RepositoryTileLookTests`, headless): the glyph's `Size` is
   24; the path's foreground is the secondary foreground brush, not the tertiary one.

### Acceptance criteria

- The tile's glyph is 24 px; the path reads in the secondary foreground in both themes.
- The drag copy (same template) looks the same.
- The build has zero warnings; the whole suite is green against the baseline.

## PHASE02 — The tile's context menu

**Branch:** `feature/feature-4f5c-phase02-tile-menu`
**Status:** DONE

### Steps

1. `RepositoriesPageViewModel`: add `CopyPathCommand` (`AsyncRelayCommand<ListedRepository>`) → copies
   `entry.Path` through `ISystemInterop.CopyTextAsync` (works for a missing repository too).
2. `RepositoriesPageView.axaml`: the tile's `Border.tile` carries a `ContextMenu`, items in this order,
   each with an `ei:Icon Classes="menu"`:
   - Open (`ArrowSquareIn`) → `OpenListedCommand`
   - Open in a new window (`ArrowSquareOut`, the tile button's glyph) → `OpenInNewWindowCommand`
   - separator
   - Open the repository's folder (`FolderOpen`) → `OpenFolderCommand`
   - Open a terminal in the repository's folder (`TerminalWindow`) → `OpenTerminalCommand`
   - Copy repository path (`Copy`) → `CopyPathCommand`
   - separator
   - Forget this repository (`X`, the tile button's glyph) → `ForgetCommand`
   The menu opens in its own popup tree: bind the page's commands the way that works there (the menu's
   `PlacementTarget`/a binding captured on the tile, as the History line menu does) — verify in a test
   that each item's command is the page's and its parameter the tile's repository.
3. The drag gesture: a right-button press never starts a drag (check `RepositoriesPageView.axaml.cs`;
   add the guard if missing). The drag copy (`DragGhost`) needs no menu — leave it on, unused, or
   strip it only if it costs nothing.
4. Tests (headless, new `RepositoryTileMenuTests`): the headers in order with the separators; every
   item has a `menu` glyph; each command/parameter pair; Copy repository path puts the path on the
   (test) interop; a right press on the tile does not start a drag; `MenuIconTests`' coverage extended
   to the start page if it enumerates views.

### Acceptance criteria

- Right-click (and the keyboard's context-menu request) on a tile opens the menu with the 6 items and
  2 separators, in the order above, each with its icon; each item does what the matching button does.
- Copy repository path copies the tile's full path.
- A right-click never starts a reorder drag.
- The build has zero warnings; the whole suite is green against the baseline.

## PHASE03 — Store and draw a repository icon

**Branch:** `feature/feature-4f5c-phase03-icon-store`
**Status:** TODO

### Steps

1. `Services/RepositoryIconStore.cs` — `IRepositoryIconStore` + `RepositoryIconStore` (singleton,
   `IAppPaths`, `ILogger`):
   - `string? Find(string repositoryPath)` — the stored icon file for that repository, or `null`;
   - `Task<string> SetAsync(string repositoryPath, string sourceFile, CancellationToken)` — validates
     (extension allow-list: `.png .jpg .jpeg .bmp .gif .ico .webp .svg`; size ≤ 10 MiB, ≤ 1 MiB for
     SVG; it must decode — through the loader below), copies it to
     `<config>/repository-icons/<key>.<ext>` via a temp file and a move, removes the repository's
     other `<key>.*` files, returns the stored path; throws a dedicated exception
     (`RepositoryIconException`, message for the user) on a refusal;
   - `Task ResetAsync(string repositoryPath, CancellationToken)` — removes `<key>.*`.
   - `<key>`: the first 16 hex digits of SHA-256 over the full path, trailing separators trimmed,
     upper-cased on Windows (the list store's `SamePath` rule).
2. `Services/RepositoryIconLoader.cs` — `static` (or injected) `IImage? Load(string file)`:
   - raster: `Bitmap.DecodeToWidth(stream, 48)` (sharp at 2× on a 24 px slot);
   - SVG: `SvgIconParser.Parse(stream)` → a `DrawingGroup` with one `GeometryDrawing` per layer, each
     painted with its own parsed `Fill` / `Stroke` colour (a `null` / `currentColor` fill takes the
     theme's foreground — resolved when drawn, or a neutral grey), opacity and fill rule kept; a
     `DrawingImage` with `Viewbox` = the glyph's view box;
   - caches by file path + last-write time; returns `null` and logs a warning for a file that is gone
     or does not decode.
3. `ListedRepository` gains `[JsonIgnore] public string? IconFile { get; init; }`;
   `RepositoriesPageViewModel.Replace` fills it from `IRepositoryIconStore.Find` (so the list file is
   unchanged).
4. The tile: a 24×24 `Image` bound to `IconFile` through a converter calling the loader, shown when it
   yields an image; the folder glyph otherwise (missing file, broken file, none set).
5. Register both in `DependencyInjection/ServiceCollectionExtensions.cs`.
6. Tests: store (temp config directory — set, replace with another format, reset, the key's path rule,
   each refusal: extension, size, undecodable, SVG with nothing drawable); loader (a generated PNG, an
   ICO, a two-colour SVG keeps both colours, a missing file → `null`); the tile shows the image when
   `IconFile` is set and the glyph otherwise.

### Acceptance criteria

- A stored icon is drawn at 24×24 on the tile; PNG, JPEG, BMP, GIF, ICO, WebP and SVG are accepted;
  an SVG keeps its own colours.
- Nothing is stored for a refused file, and the reason is a sentence a user can read.
- A missing or broken stored icon falls back to the folder glyph without an error.
- `repository-lists.json` is unchanged in shape.
- The build has zero warnings; the whole suite is green against the baseline.

## PHASE04 — Choose an icon from the tile menu

**Branch:** `feature/feature-4f5c-phase04-choose-icon`
**Status:** TODO

### Steps

1. `RepositoriesPageViewModel` (+ `IFileDialogService`, `IRepositoryIconStore`):
   - `ChooseIconCommand` (`AsyncRelayCommand<ListedRepository>`): the file picker, titled "Choose an
     icon for <name>", one filter "Images" (`*.png *.jpg *.jpeg *.bmp *.gif *.ico *.webp *.svg`) plus
     "All files"; cancelled → nothing; else `SetAsync`, then reload the list so the tile redraws; a
     refusal or an I/O failure → an InfoBar error naming the file and the reason (and a log entry);
   - `ResetIconCommand` (`AsyncRelayCommand<ListedRepository>`): `ResetAsync`, then reload.
2. The tile's menu, after a separator: "Choose an icon…" (`ImageSquare`) and "Use the default icon"
   (`ArrowCounterClockwise`), the latter visible only while the tile has a custom icon.
3. Tests: the picker returns a file → it is stored and the tile shows it; cancelled → nothing changes;
   a refused file → the InfoBar error and the glyph stays; reset → the glyph is back and the reset
   item hides; the menu's full order with the new items; every item still has its glyph.

### Acceptance criteria

- "Choose an icon…" sets the tile's icon from a picked file; "Use the default icon" (shown only then)
  puts the folder glyph back.
- A refused or unreadable file is reported and changes nothing.
- The icon follows the repository into every profile that lists it and survives a restart.
- The build has zero warnings; the whole suite is green against the baseline.

## Out of scope

- Icons anywhere else (the repository window's title strip, the taskbar).
- Animated GIF/WebP (first frame only), SVG features the Enigma.Icons parser does not draw (gradients,
  text, `<use>`, clip paths, embedded images).
- Removing the tile's buttons, which stay beside the menu.
- Cleaning up a forgotten repository's stored icon (kept; it comes back if the repository is listed
  again).
- The release notes (a release item's business).

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| The path's colour | The `dim` role (secondary foreground) | The house already moved ref details from `faint` to `dim` for legibility in the dark theme; it keeps the name/path hierarchy | Full foreground (the path would compete with the name); a new brush |
| How the glyph gets to 24 | A new `tile` role (24) in the icon scale | The scale is the house's single place for icon sizes; no role describes a tile's glyph | A bare `Size="24"`; reusing `title`/`command`, which mean other things |
| The tile's buttons | Kept | The draft adds a menu; it does not ask to remove the buttons | Removing them (a regression for mouse users who know them) |
| Menu titles | The draft's, plus "Choose an icon…" and "Use the default icon" | The draft asked for a better title than "Customize icon": "Choose an icon…" says what happens and the ellipsis says a dialog follows; a custom icon must be removable | "Customize icon"; "Change icon…"; no reset item (a custom icon could never be undone) |
| Menu glyphs | Open `ArrowSquareIn`, new window `ArrowSquareOut`, folder `FolderOpen`, terminal `TerminalWindow`, copy `Copy`, forget `X`, choose `ImageSquare`, default `ArrowCounterClockwise` | The tile's own buttons use `ArrowSquareOut`, `FolderOpen`, `TerminalWindow`, `X`: the menu repeats them; the rest follow the app's menus (`Copy`) | `AppWindow` for the new window (the worktree menus' glyph, but not the tile's) |
| Where the icon is stored | A copy in `<config>/repository-icons/<hash of the path>.<ext>`, keyed by repository path | Survives the source file moving; shared by every profile listing the repository; not lost when `AddAsync` rewrites an entry; no new JSON index to keep consistent between instances | A path to the user's original file; a field on `ListedRepository` in the per-profile list |
| SVG rendering | Enigma.Icons' `SvgIconParser` (already referenced), painting each layer with its own colours | No new dependency; safe parser (XXE closed, size cap); covers typical flat logos | Svg.Skia (`Svg.Controls.Skia.Avalonia`): a new dependency whose Avalonia 12 support is unverified; Enigma.Icons' `Icon` control (recolours every layer with one brush) |
| Raster formats | Whatever Skia decodes: PNG, JPEG, BMP, GIF, ICO, WebP | The draft says "png, ico, svg, etc."; Skia already ships in the app | An explicit converter per format |
| Limits | ≤ 10 MiB raster, ≤ 1 MiB SVG (the parser's cap), decoded at 48 px wide | An icon is tiny; caps keep a mistaken pick (a photo, a huge file) cheap | No cap; downscaling stored copies |
| A broken or missing stored icon | The folder glyph, a warning logged | A tile must always draw; the user can choose again | An error badge on the tile |
