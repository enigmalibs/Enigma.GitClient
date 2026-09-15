# FEATURE-7CFD — Solution foundation & git engine

**Status:** TODO
**Type:** FEATURE
**Branch:** `feature/feature-7cfd-<phase>-<slug>` (one per phase)
**Run:** feature/2026-09-15-enigma-git-client

## Objective

Stand up the whole .NET solution to house conventions and build the read side of the git engine: a
safe process wrapper around the `git` executable, repository discovery, the commit-log reader, and the
ref/HEAD reader. Everything in this item is headless and fully testable without a UI.

## Context & constraints

- Greenfield repository: one commit, a placeholder `README.md`, nothing else. Every root artifact has
  to be created from the house templates.
- Git access is through the **git CLI**, never a native binding — see the decision table below.
- Argument lists are always passed as an argv array to `ProcessStartInfo.ArgumentList`; a shell is
  never involved, so user-supplied refs, paths and messages cannot inject commands.
- **Rebase is forbidden product-wide.** The command factory rejects it structurally.
- Target frameworks: `net10.0` everywhere. `Enigma.GitClient.Core` is an app-internal library and
  deliberately does not multi-target `netstandard2.0`; the reason is recorded in its csproj.

## Design

```
src/Enigma.GitClient.Core/
  Git/            IGitExecutable, GitProcessRunner, GitCommand, GitResult, GitCommandException
  Repositories/   IRepositoryLocator, RepositoryHandle, RepositoryState
  History/        GitCommit, ICommitLogReader, CommitLogReader, CommitLogQuery/Page
  Refs/           GitRef, GitBranch, GitTag, GitRemote, HeadState, IRefReader, RefReader
src/Enigma.GitClient.App/            (Avalonia desktop app — populated by FEATURE-52FB)
tests/Enigma.GitClient.Core.UnitTests/         (pure logic: parsers, factories, validation)
tests/Enigma.GitClient.Core.IntegrationTests/  (real temp repositories driven by the git CLI)
```

## PHASE01 — Solution scaffolding & config

**Steps**

1. `git-repo-hygiene`: copy `.gitignore` and `.gitattributes` from the bundled templates.
2. `dotnet-solution-config`: copy the **full C#** `.editorconfig`, `Directory.Build.props` (fill
   `{{AUTHORS}}` = `Josué Clément`, `{{YEAR}}` = `2026`), `Directory.Packages.props`, and write
   `global.json` with the SDK pin **and** the MTP `test.runner` entry.
3. `dotnet-solution-setup`: `LICENSE.md` (MIT, 2026, Josué Clément), `RELEASENOTES.md` (empty
   placeholder), `Enigma.GitClient.slnx` with `/src/` and `/tests/` folders.
4. Create the four projects: `Enigma.GitClient.Core` (library, `net10.0`, documentation file on),
   `Enigma.GitClient.App` (`WinExe`, `net10.0`, Avalonia properties but no UI code yet beyond a
   compiling stub), `Enigma.GitClient.Core.UnitTests`, `Enigma.GitClient.Core.IntegrationTests`
   (both `net10.0`, `Exe`, `xunit.v3`).
5. Populate `Directory.Packages.props` with the pinned, version-coupled sets: Avalonia 12.1.1
   (`Avalonia`, `Avalonia.Desktop`, `Avalonia.Themes.Fluent`, `Avalonia.Fonts.Inter`),
   `Enigma.Avalonia.Desktop` 1.0.0, `Enigma.Icons.Avalonia` 1.0.0, `CommunityToolkit.Mvvm` 8.4.2,
   `Microsoft.Extensions.Hosting` 10.0.12, `AvaloniaUI.DiagnosticsSupport` 2.2.3, `xunit.v3` 4.0.1,
   `Microsoft.Testing.Extensions.CodeCoverage` 18.11.2.
6. Write the real `README.md` (what the app is, the platforms, the requirements, how to build, and the
   explicit "no rebase, no issues, no pull requests" scope statement).
7. One smoke test per test project so both suites actually execute.

**Acceptance criteria**

- `dotnet build Enigma.GitClient.slnx` succeeds with **zero warnings**.
- `dotnet test --solution Enigma.GitClient.slnx` runs and passes.
- No `LangVersion` / `Nullable` / `ImplicitUsings` / `TreatWarningsAsErrors` in any `.csproj`.
- No `Version=` attribute on any `PackageReference`.
- `.slnx` (not `.sln`); `src/`, `tests/`, `docs/` all present.

## PHASE02 — Git process runner & repository discovery

**Steps**

1. `IGitExecutable` / `GitExecutable`: resolves the `git` binary (PATH, then the usual Windows
   install locations), reads `git --version`, and exposes a parsed `Version` plus
   `IsSupported` (≥ 2.20).
2. `GitCommand` — an immutable argv value type built through `GitCommandFactory`. The factory
   **throws `NotSupportedException` for the verb `rebase`** (and for `pull --rebase`), which is the
   structural form of the product-wide ban.
3. `GitProcessRunner : IGitProcessRunner` — `RunAsync(GitCommand, CancellationToken)` returning
   `GitResult` (exit code, stdout, stderr, duration); a `RunToLinesAsync` helper that splits stdout on
   `\n` or `\0`; a raw-bytes overload for binary output. Never uses a shell; sets
   `LC_ALL=C`/`LANG=C`, `GIT_TERMINAL_PROMPT=0`, `GIT_OPTIONAL_LOCKS=0` and disables the pager and
   any credential prompt that would hang a UI.
4. `GitCommandException` — exit code, redacted argv, stderr; thrown by `RunAsync` when
   `throwOnError` and the exit code is non-zero.
5. `ArgumentRedactor` — strips tokens/passwords out of URLs and argv before logging.
6. `IRepositoryLocator` / `RepositoryLocator` — `TryDiscoverAsync(path)` walks up to the work tree
   root via `git rev-parse --show-toplevel`, rejects bare repositories, and returns a
   `RepositoryHandle` (work-tree path, git-dir path, display name).
7. `ILogger` used for every invocation at `Debug` with the redacted argv and elapsed time.

**Acceptance criteria**

- Unit tests: the factory rejects `rebase` in every spelling tested; the redactor removes
  `https://user:token@host` credentials and `--password=` style arguments; argv building quotes
  nothing (values are passed verbatim through `ArgumentList`).
- Integration tests against a temp repository: `git --version` parses; discovery from a nested
  sub-directory finds the work-tree root; a non-repository path returns `null`; a bare repository is
  rejected; a failing command throws `GitCommandException` carrying the exit code and stderr; a
  cancelled run throws `OperationCanceledException` and kills the child process.

## PHASE03 — Commit log reading & model

**Steps**

1. `GitCommit` record: `Sha`, `ShortSha` (7), `ParentShas`, `Author`/`Committer` (`GitSignature`:
   name, email, `DateTimeOffset`), `Subject`, `Body`, `IsMerge`.
2. `CommitLogReader : ICommitLogReader` — `git log` with a `--pretty=format:` template using `%x1f`
   field and `%x1e` record separators (never a newline-based format, so multi-line bodies survive),
   plus `--date=iso-strict`, `-z`-safe parsing, `--skip`/`--max-count` paging, and the selector
   options the UI needs: all refs (`--all`), a single ref, author/message search, path filter,
   `--first-parent` toggle and a date range.
3. `CommitLogQuery` (immutable query object) and `CommitLogPage` (`Commits`, `HasMore`).
4. `CommitReader.GetCommitAsync(sha)` for a single commit.
5. Robustness: empty repository (`git log` fails with "does not have any commits yet") maps to an
   empty page, not an exception; unborn HEAD, detached HEAD and shallow clones all read normally.

**Acceptance criteria**

- Unit tests parse a captured multi-commit payload including: a multi-line body with blank lines, a
  subject containing the field separator's neighbours, a merge with two and with three parents, a
  commit with no body, and non-ASCII author names.
- Integration tests on a temp repository with a known shape (linear + a merge + a detached tag) assert
  order, parents, subjects, timestamps and paging (`--skip`/`--max-count`, `HasMore`).
- An empty repository returns an empty page.

## PHASE04 — Refs, branches, tags & HEAD state

**Steps**

1. `GitRef` hierarchy: `GitBranch` (name, full ref, tip sha, `IsRemote`, upstream, ahead/behind),
   `GitTag` (name, tip sha, target sha, `IsAnnotated`, tagger, message), `GitRemote` (name, fetch
   URL, push URL).
2. `RefReader : IRefReader` — one `git for-each-ref` call with an explicit `--format` covering all of
   the above (`%(refname)`, `%(objectname)`, `%(*objectname)`, `%(upstream:short)`,
   `%(upstream:track)`, `%(objecttype)`, `%(taggername)`, `%(contents:subject)`), split on `%00`.
3. `HeadState` — `IsDetached`, `IsUnborn`, `BranchName`, `Sha`, plus the in-progress operation
   detected from the git dir (`MERGE_HEAD`, `CHERRY_PICK_HEAD`, `REVERT_HEAD`, `BISECT_LOG`) so the UI
   can surface it. `REBASE_HEAD` is reported as `Rebase` for detection purposes only — the app can
   warn about a repository left mid-rebase by another tool, and still never starts one.
4. `RemoteReader` — `git remote -v` parsing into `GitRemote`.
5. A `RefDecorationIndex` mapping commit sha → the refs pointing at it, for the graph's ref badges.

**Acceptance criteria**

- Unit tests parse a captured `for-each-ref` payload: local branch with upstream and `[ahead 2,
  behind 1]`, local branch with no upstream, remote-tracking branch, lightweight tag, annotated tag
  with a multi-word message, and `refs/stash`.
- Integration tests on a temp repository assert: branches, tags (both kinds), remotes, ahead/behind
  against a local "remote", detached HEAD, unborn HEAD, and a repository left mid-merge.
- `RefDecorationIndex` groups multiple refs on one commit in a stable order (HEAD, local branches,
  remote branches, tags).

## Out of scope

- Any UI (FEATURE-52FB onwards).
- Any write operation (branches, tags, commits, merges) — later items.
- Rebase, issues and pull requests — permanently out of scope for the product.

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| Git access layer | Git CLI process wrapper | Full SSH and credential-helper support, no per-RID native assets, identical semantics to the user's own git on Windows and Linux | LibGit2Sharp (its shipped native binaries have no SSH support and bypass credential helpers); a pure-.NET git implementation (absurd scope) |
| Test project split | `.UnitTests` (pure logic) + `.IntegrationTests` (real repos) | Parsers must be validated against genuine git output, but process-spawning tests must not slow or flake the pure suite | one combined project |
| Core TFM | `net10.0` only | The library ships only inside this app; `netstandard2.0` would cost polyfills for no consumer | `netstandard2.0;net8.0;net10.0` |
| Log record format | `%x1f` field / `%x1e` record separators | Newline-based formats corrupt multi-line commit bodies | `--pretty=format:` with newlines; `-z` alone (does not separate fields) |
| Rebase ban enforcement | `GitCommandFactory` throws on the `rebase` verb, covered by a test | Makes the prohibition structural rather than a convention a later dev can forget | documentation-only ban |
| Minimum git version | 2.20 | `--pretty` separators, `for-each-ref` format atoms and `status --porcelain=v2` are all stable from there | requiring a very recent git (needlessly narrow) |
