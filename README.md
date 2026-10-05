# Enigma.GitClient

A modern, cross-platform git client for the desktop, inspired by GitKraken and built with
[Avalonia](https://avaloniaui.net/) 12. It runs on **Linux** and **Windows** from the same binary.

Two things matter more than everything else in this app:

1. **The commit graph** — lanes, merges and branches drawn so a real repository's history is readable
   at a glance, with the author, the timestamp and the short hash on every row.
2. **The diff viewer** — colour-coded additions, deletions and intra-line changes, unified or
   side-by-side.

> **What's new in 5.4** — revert any commit from its line in the history, beside the reset; the
> changed files open as a tree of folders again; and a profile can name a base directory, where the
> start window's Open, Clone and Create begin. Coming from 5.1.0? The program was renamed
> `Enigma.GitClient.Desktop` in 5.1.1: read *Upgrading from 5.1.0* there (on Linux, run the installer
> again). Coming from 4.x? Read *Upgrading from 4.x* under 5.0.0 first. See
> [RELEASENOTES.md](RELEASENOTES.md).

## Features

- Commit graph with coloured lanes, merge curves, ref badges and virtualised scrolling
- History list with a column header you can resize — the graph's included — and a search that
  highlights what it found instead of hiding everything else
- Hide a branch from the history with the eye on its row in the branches dialog: the commits only it
  brings and its badge leave the graph, the history says how many branches it is leaving out, and
  the choice is remembered for the repository
- Author, timestamp and 7-character short hash on every commit row
- Changed files for the selected commit in a panel beside the history, as GitKraken has it — click a
  line to open it, click the line again to close it — shown as a **list or a tree** (your choice);
  pick a file and its diff opens over the graph, with the panel still beside it, and click the file
  again to put the diff away
- A commit's details — its title, description, author and email, date and how long ago that was, and
  full hash — in a dialog opened from the details panel's header, the diff view's or the commit's
  line menu, every value selectable to copy
- Colour-coded file diffs with word-level intra-line highlighting
- Side by side shows the **whole file** on both sides, scrolling as one; unified shows the
  change with the context you chose
- A minimap in place of the diff's vertical scrollbar: where the changes are, where you are, and
  click or drag it to go there
- Branch management — create, rename, delete, set upstream, checkout
- Every local branch says where it stands: an arrow and a count for the commits to push and to
  pull, and whether the branch is on a remote at all
- Select a branch, tag or remote in its list, and drag one branch onto another to merge them: the
  drop opens a menu naming both, with the merge, the fast-forward-only merge and the reverse — and
  the list scrolls while you hold a branch near its edge
- Sort the branches and the tags by name or by date, either way — newest first unless you choose
  otherwise, and remembered; select a branch's or a tag's line in the history from its menu
- Tag management — create (lightweight or annotated), push one tag to the remote, and delete one
  here or on the remote, from its badge in the history or its line in the tags dialog
- Checkout of anything in the graph: a branch from its badge's menu, a commit — detached — from its
  line's menu
- Reset the branch you are on to any commit from that commit's line menu: soft keeps every change,
  staged; hard discards them, after naming the files it takes
- Revert any commit from its line menu: after one question, a new commit on the branch you are on
  undoes what it changed (a merge against the branch it went into); a revert that would conflict is
  abandoned on the spot, naming the files, and leaves everything as it was
- Merging from the graph: set a branch as the merge source from its badge or its line, then merge it
  into any other local branch the same way — or drag one branch badge onto another, and pick the
  merge or the fast-forward
- Every merge the app offers records a merge commit, even when the branch could simply be
  fast-forwarded; the fast-forward-only merges are the way to move a branch without one
- Create (with a `README.md` holding its name as the first commit), clone and open repositories from
  a start window that lists a profile's repositories — pick the profile in the page's header: every
  profile keeps a list of its own, in the order you drag its rows into, and a "Default" profile is
  made when there is none; a profile can name a base directory, where Open, Clone and Create start
  while it is picked (a clone from one of its integrations goes there too); the repository you pick
  opens in a window of its own, and `Enigma.GitClient.Desktop <path>` opens one straight away
- Working directory in the same panel: select the history's uncommitted line to see what is not
  staged and what is — stage/unstage, discard and commit (Ctrl+Enter) there, with each file's diff
  over the graph; every discard confirms with a red button, and the uncommitted line's menu discards
  all the uncommitted work at once
- Remotes: fetch, pull (merge only), push (with `--force-with-lease`)
- Stashes in the graph, one line each at the stash itself: stash your uncommitted work from the
  history's toolbar, then apply, pop or delete a stash from its line — a pop that conflicts keeps
  the stash, as git does
- Merge conflict resolution with a three-way view, per-hunk selection and a live preview of the
  file that will be written
- Integrations with **GitHub**, **GitLab** and **Azure DevOps**, connected under a profile: sign in
  with a personal access token, browse and clone your repositories, and open a commit, branch or file
  on the host — on the public instances and on self-hosted ones (GitHub Enterprise Server,
  self-hosted GitLab, Azure DevOps Server)
- A **Profiles** page, on the start window and in every repository window, to edit the
  global name and email git records on every commit, and profiles — work, personal — that switch
  them in one click, each with the hosting accounts it is connected to — a profile pushes only to
  the hosts it is connected to, so one without any integration stays local; in a repository's
  window, give that repository a name and email of its own (copied from the current profile in one
  click), or remove them again
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

Integrations are connected under a profile, with **Connect an account** on the Profiles page, so
each profile has accounts of its own. Each one signs in with a personal access token you create on
the host itself, and asks for the smallest scope that lists and clones repositories — plus write
access to the code if you push with it:

| Host | Scope to grant | To push as well | Where to put the instance URL |
|------|----------------|-----------------|-------------------------------|
| GitHub | `repo` — or, for a fine-grained token, read access to **Contents** and **Metadata** | `repo`, or **Contents: Read and write** | `https://github.com`, or your Enterprise Server's own address |
| GitLab | `read_api` and `read_repository` | `write_repository` instead of `read_repository` | `https://gitlab.com`, or your instance's own address |
| Azure DevOps | **Code: Read** | **Code: Read & write** | `https://dev.azure.com/your-organisation`, `https://your-organisation.visualstudio.com`, or a Server collection such as `https://tfs.example.com/tfs/DefaultCollection` |

When the host refuses the token — it expired, or cannot push — the error says so and names the scope
a push needs; disconnect the account on the Profiles page and connect it again with a new token.

**A profile pushes only where one of its integrations leads.** When a repository's commits are made
as a profile — its name and email, as git resolves them there — a push goes ahead only if that
profile has an integration for the remote's host; a profile with no integration never pushes, to a
host or to a folder. A repository whose identity matches no profile pushes as it always did.

**The integration is also how git signs in.** Every fetch, pull and push — a tag's or a branch's
included — to an HTTPS remote on the integration's host signs in with its token, with nothing to set
up in git; so does a clone, with the integration you browsed the repository from, or else the current
profile's. The token is handed to that one git process through its environment, never through its
command line, its configuration or a credential helper that could store it. SSH remotes keep using
your SSH key, and hosts none of the profile's integrations cover keep using git's own credentials.

An integration connected with a 1.x version belongs to no profile: it is listed under **Earlier
integrations** on the Profiles page until you move it into the profile it is for.

No issue, work-item, merge-request or pull-request scope is ever requested, and the client never
calls those APIs. Tokens are encrypted at rest — AES-GCM with a key protected by DPAPI on Windows
and by file permissions (`0600`) on Linux — and are redacted from every log line and error message.

## Where your things are kept

Everything the client remembers about you lives in one per-user directory —
`$XDG_CONFIG_HOME/Enigma.GitClient` on Linux, `%APPDATA%\Enigma.GitClient` on Windows:

| File | What is in it |
|------|---------------|
| `settings.json` | Your preferences, as plain readable JSON |
| `repository-lists.json` | Each profile's list of repositories, in its order |
| `host-accounts.json` | The hosting accounts you connected, each under the profile it belongs to — never their tokens |
| `tokens.json` + `tokens.key` | Those tokens, encrypted, and the key that reads them |
| `identity-profiles.json` | Your profiles: a label each, and the name and email it sets, if any |
| `hidden-branches.json` | The branches you hid from the history, per repository |

Nothing else is written anywhere — apart from git's own configuration (your global one, or a
repository's), and only when you save or remove a name and email, use a profile on the Profiles
page, pick one in the start window, or let a new clone use your identity — and nothing is sent
anywhere: the client talks to your git and to the hosts you connected, and to nothing else.

## Requirements

- **git 2.20 or newer** on the `PATH` (the app drives the real `git` executable, so your existing
  SSH keys, credential helpers and configuration all keep working — an integration's token only takes
  over HTTPS sign-in to its own host)
- **.NET 10 SDK** to build. The Linux installer bundles the .NET runtime with the application by
  default; a framework-dependent build needs the .NET 10 runtime instead
- Linux or Windows

## Build and run

```bash
dotnet build Enigma.GitClient.slnx
dotnet test --solution Enigma.GitClient.slnx
dotnet run --project src/Enigma.GitClient.Desktop
```

## Install on Linux

`packaging/linux/install.sh` builds the application from this repository and installs it for you —
no root, no `sudo`, nothing outside your home directory — so that **Enigma Git Client** is in your
application launcher:

```bash
./packaging/linux/install.sh
```

| What | Where (`XDG_DATA_HOME` and `XDG_BIN_HOME` are honoured) |
|------|---------------------------------------------------------|
| The application | `~/.local/share/enigma-git-client` |
| A launcher command, `enigma-git-client [repository]` | `~/.local/bin/enigma-git-client` |
| The desktop entry | `~/.local/share/applications/enigma-git-client.desktop` |
| The icon, in six sizes | `~/.local/share/icons/hicolor/<N>x<N>/apps/enigma-git-client.png` |

By default the .NET runtime is bundled with the application (about 116 MB), so it runs whatever is
installed on the machine. `--framework-dependent` builds against an installed .NET 10 runtime instead
(about 37 MB); `--from DIR` installs a directory you published yourself, and `--rid` picks another
runtime identifier. Run the installer again to upgrade in place. `./packaging/linux/uninstall.sh`
removes those four things and nothing else: your settings, accounts and tokens in
`~/.config/Enigma.GitClient` are left as they are.

## Repository layout

| Path                                       | What it is                                            |
|--------------------------------------------|-------------------------------------------------------|
| `src/Enigma.GitClient.Core`                | The headless git engine: process wrapper, parsers, graph layout, diff model |
| `src/Enigma.GitClient.Desktop`             | The Avalonia 12 desktop application                   |
| `tests/Enigma.GitClient.Core.UnitTests`    | Pure-logic tests (parsers, algorithms, validation)    |
| `tests/Enigma.GitClient.Core.IntegrationTests` | Tests driving a real `git` against temporary repositories |
| `tests/Enigma.GitClient.Desktop.UnitTests` | ViewModel and headless render tests                   |
| `docs/`                                    | Roadmap, per-item plans and completion records        |

## Licence

MIT — see [LICENSE.md](LICENSE.md).
