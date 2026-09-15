# Enigma.GitClient

A modern, cross-platform git client for the desktop, inspired by GitKraken and built with
[Avalonia](https://avaloniaui.net/) 12. It runs on **Linux** and **Windows** from the same binary.

Two things matter more than everything else in this app:

1. **The commit graph** — lanes, merges and branches drawn so a real repository's history is readable
   at a glance, with the author, the timestamp and the short hash on every row.
2. **The diff viewer** — colour-coded additions, deletions and intra-line changes, unified or
   side-by-side.

## Features

- Commit graph with coloured lanes, merge curves, ref badges and virtualised scrolling
- Author, timestamp and 7-character short hash on every commit row
- Changed files for the selected commit, shown as a **list or a tree** (your choice)
- Colour-coded file diffs with word-level intra-line highlighting
- Branch management — create, rename, delete, set upstream, checkout
- Tag management — create (lightweight or annotated) and delete
- One-click checkout of anything in the graph, including a detached commit
- Create, clone and open repositories, with a recent-repositories list
- Working directory: status, stage/unstage, discard and commit
- Remotes: fetch, pull (merge only), push (with `--force-with-lease`), and stash
- Merge conflict resolution with a three-way view, per-hunk selection and a live preview of the
  file that will be written
- Integrations with **GitHub**, **GitLab** and **Azure DevOps**: sign in with a personal access
  token, browse and clone your repositories, and open a commit, branch or file on the host

## Non-goals

These are deliberate, permanent exclusions — not gaps waiting to be filled:

- **No rebase.** The client never rebases, and its command layer structurally refuses the verb.
  `git pull` is always invoked with `--no-rebase`, even in a repository configured otherwise.
- **No issues and no pull requests.** The hosting integrations cover repositories, cloning and deep
  links only; no issue or pull-request scope is ever requested from a host.

## Requirements

- **git 2.20 or newer** on the `PATH` (the app drives the real `git` executable, so your existing
  SSH keys, credential helpers and configuration all keep working)
- **.NET 10 SDK** to build; the published app is framework-dependent and needs the matching runtime
- Linux or Windows

## Build and run

```bash
dotnet build Enigma.GitClient.slnx
dotnet test --solution Enigma.GitClient.slnx
dotnet run --project src/Enigma.GitClient.App
```

## Repository layout

| Path                                       | What it is                                            |
|--------------------------------------------|-------------------------------------------------------|
| `src/Enigma.GitClient.Core`                | The headless git engine: process wrapper, parsers, graph layout, diff model |
| `src/Enigma.GitClient.App`                 | The Avalonia 12 desktop application                   |
| `tests/Enigma.GitClient.Core.UnitTests`    | Pure-logic tests (parsers, algorithms, validation)    |
| `tests/Enigma.GitClient.Core.IntegrationTests` | Tests driving a real `git` against temporary repositories |
| `tests/Enigma.GitClient.App.UnitTests`     | ViewModel and headless render tests                   |
| `docs/`                                    | Roadmap, per-item plans and completion records        |

## Licence

MIT — see [LICENSE.md](LICENSE.md).
