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
- Checkout of anything in the graph from a row's own menu, including a detached commit
- Drag one branch badge onto another in the graph to merge it, or to fast-forward only
- Create, clone and open repositories, with a recent-repositories list
- Working directory: status, stage/unstage, discard and commit
- Remotes: fetch, pull (merge only), push (with `--force-with-lease`), and stash
- Merge conflict resolution with a three-way view, per-hunk selection and a live preview of the
  file that will be written
- Integrations with **GitHub**, **GitLab** and **Azure DevOps**: sign in with a personal access
  token, browse and clone your repositories, and open a commit, branch or file on the host — on the
  public instances and on self-hosted ones (GitHub Enterprise Server, self-hosted GitLab, Azure
  DevOps Server)
- Preferences that stick: theme, history and graph metrics, the file list's shape, the diff's shape,
  font and context, the pull strategy and the path to git — every one of them applied without a
  restart

## Non-goals

These are deliberate, permanent exclusions — not gaps waiting to be filled:

- **No rebase.** The client never rebases, and its command layer structurally refuses the verb.
  `git pull` is always invoked with `--no-rebase`, even in a repository configured otherwise.
- **No issues and no pull requests.** The hosting integrations cover repositories, cloning and deep
  links only; no issue or pull-request scope is ever requested from a host.

## Connecting a host

Each integration signs in with a personal access token you create on the host itself, and asks for
the smallest scope that can list and clone repositories:

| Host | Scope to grant | Where to put the instance URL |
|------|----------------|-------------------------------|
| GitHub | `repo` — or, for a fine-grained token, read access to **Contents** and **Metadata** | `https://github.com`, or your Enterprise Server's own address |
| GitLab | `read_api` and `read_repository` | `https://gitlab.com`, or your instance's own address |
| Azure DevOps | **Code: Read** | `https://dev.azure.com/your-organisation`, `https://your-organisation.visualstudio.com`, or a Server collection such as `https://tfs.example.com/tfs/DefaultCollection` |

No issue, work-item, merge-request or pull-request scope is ever requested, and the client never
calls those APIs. Tokens are encrypted at rest — AES-GCM with a key protected by DPAPI on Windows
and by file permissions (`0600`) on Linux — and are redacted from every log line and error message.

## Where your things are kept

Everything the client remembers about you lives in one per-user directory —
`$XDG_CONFIG_HOME/Enigma.GitClient` on Linux, `%APPDATA%\Enigma.GitClient` on Windows:

| File | What is in it |
|------|---------------|
| `settings.json` | Your preferences, as plain readable JSON |
| `recent-repositories.json` | The repositories you have opened, and the ones you pinned |
| `host-accounts.json` | The hosting accounts you connected — never their tokens |
| `tokens.json` + `tokens.key` | Those tokens, encrypted, and the key that reads them |

Nothing is written anywhere else, and nothing is sent anywhere: the client talks to your git and to
the hosts you connected, and to nothing else.

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
