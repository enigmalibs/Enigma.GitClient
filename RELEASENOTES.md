# Release notes

## 2.0.0 — 2026-09-27

A major release. Your hosting integrations now belong to a profile, so each profile can have its
own GitHub, GitLab or Azure DevOps accounts, and a profile without any integration stays local: it
never pushes. The Identity page is now the **Profiles** page, and the Integrations page is gone; what
it did moved onto the Profiles page. Pushes that 1.x ran can now be refused, which is why this is
2.0: read *Upgrading from 1.x* below.

### Profiles and their integrations

- The **Identity** page is now the **Profiles** page, in both windows. The global identity, the
  profiles and a repository's own identity work as before.
- Every profile lists the accounts it is connected to. **Connect an account** under a profile signs
  in with a personal access token as before, and the host still checks the token before anything is
  stored. **Browse repositories** and **Disconnect** are on every integration.
- **Browse repositories** opens the account's repositories in a dialog: filter them, ask the host for
  all, public or private ones, open one on the host, or **Clone** it. The dialog closes and the clone
  runs with its usual progress and cancel.
- Deleting a profile disconnects its integrations and deletes their tokens. The confirmation says so
  first, naming them.
- The **Integrations** page and its place on both rails are gone. Nothing it did is lost.

### Pushing

- **A profile pushes only where one of its integrations leads.** A repository pushes as the profile
  whose name and email match the identity git commits with there: the repository's own, or the global
  one, conditional includes counted. A push then goes ahead only when that profile has an integration
  for the remote's host (over HTTPS or SSH).
- A profile with no integration never pushes, to a host or to a folder, and the Profiles page shows
  it as *Local only*.
- A refused push runs nothing and says which profile refused it, and for which host. For example:
  *Work does not push to github.com — connect an account for it to Work on the Profiles page*. When
  the profile cannot be checked, nothing is pushed either.
- The integration is the permission, not the credential: git still signs in with its own credential
  helper or SSH key, and no token is ever handed to git.
- A repository whose identity matches no profile pushes exactly as before.

### The diff

- Opening the diffs of a commit always selects its first file, and its diff is shown straight away.
  The file you had selected before is never carried over, neither to another commit nor to the same
  one reopened. Selecting a line without opening its diffs reads no patch at all.

### Upgrading from 1.x

- **If you use profiles**, every push now needs an integration: connect an account to each profile
  you push from, on the Profiles page. Until you do, pushing from a repository that commits as one
  of your profiles is refused, with a message saying so. If you do not use profiles, nothing changes.
- **Integrations connected with 1.x belong to no profile.** They are listed under **Earlier
  integrations** on the Profiles page. Pick **Move to…** to give each one to the profile it is for;
  its token moves with it. Until then no profile uses them, so none pushes through them.
- `host-accounts.json` is now written as version 2, which adds each account's profile. 1.x reads the
  new file and ignores that field, so going back to 1.1 keeps your accounts.
  `identity-profiles.json`, `settings.json` and the tokens are unchanged.

### Dependencies

- No package had an update outside the Avalonia set.
- The Avalonia set (Avalonia, Avalonia.Desktop, Avalonia.Themes.Fluent, Avalonia.Fonts.Inter, and
  Avalonia.Headless and Avalonia.Skia in the tests) is held back at **12.1.1**. That is the set
  Enigma.Avalonia.Desktop 1.1.0 is built against. 12.1.3 is out; the set moves as a whole, as a
  decision of its own.

### Version

- **2.0.0** is a major release under Semantic Versioning. An upgrade can refuse pushes that 1.1 ran
  until you connect your profiles, and a page is removed. The file formats stay compatible both ways.

## 1.1.0 — 2026-09-27

A minor release: notifications that no longer get in the way, and diff colours you can see at a
glance in both themes. Nothing is removed, and nothing changes in how the settings, the repositories
list or the tokens are stored.

### Notifications

- A notification never holds anything up. Before, a refresh, the end of a busy state, or the result
  of a merge, a pull or a commit waited until its message had been closed; now the work carries on
  with the message on screen.
- A success or an informational message closes itself after 5 seconds. A warning or an error stays
  until you close it, as before.
- A history that could not be read no longer stays busy behind its error: "load more" and the
  automatic refresh work again at once.

### The diff

- Pastel colours for what changed, in both themes: added and removed lines stand out from the
  background, the changed words stand out from their line, and the code stays readable on all of
  them.
- The status chips beside each changed file — A, M, D, R, U — are pastel with a dark letter that
  reads in both themes.

### Working with the repository

- The branches, tags and remotes dialogs use the control library's own darker dialog surface. They
  look as they did in 1.0.

### Dependencies

- Enigma.Avalonia.Desktop **1.0.0 → 1.1.0**, for the timed notifications and the secondary dialog
  surface.
- The Avalonia set (Avalonia, Avalonia.Desktop, Avalonia.Themes.Fluent, Avalonia.Fonts.Inter, and
  Avalonia.Headless and Avalonia.Skia in the tests) is held back at **12.1.1**, the set
  Enigma.Avalonia.Desktop 1.1.0 is built against. 12.1.3 is out; the set moves as a whole, as a
  decision of its own.
- No other package had an update.

### Version

- **1.1.0** — a minor release under Semantic Versioning: new, backward-compatible behaviour, nothing
  removed or changed incompatibly.

## 1.0.0 — 2026-09-26

The first release: a cross-platform git client built around two things, the commit graph and the
diff.

### The graph

- A commit graph with coloured lanes, merge curves, reference badges and virtualised scrolling, so a
  repository with a hundred thousand commits scrolls like a list of ten.
- Author, timestamp and the 7-character short hash on every row, with the full date in the tooltip.
- The uncommitted changes sit at the top of the graph and lead to the working directory.
- Double-click a line — or pick "Show what it changed" from its menu — and what it changed takes the
  whole page: the changed files on the left, the diff on the right, under a header naming the
  commit. The back button at its top left — or Escape, which works the moment it opens — returns to
  the graph, exactly where it was, with the line still selected.
- Checking out: a branch from its badge's own menu, or the commit itself from the line's menu, with
  a warning before HEAD is detached.
- Resetting the branch you are on to a commit, from that commit's line menu. "Soft (keep all
  changes)" moves the branch and keeps everything, staged. "Hard (discard all changes)" asks first,
  naming the files whose uncommitted changes it throws away, and leaves untracked files alone.
- Every branch badge has a menu of its own, so a line carrying several branches is never ambiguous:
  check it out, set it as the merge source, merge the source into it, merge it into the current
  branch, pull it, push it, or delete it. Pulling a branch that is not checked out only ever
  fast-forwards it: HEAD does not move, and a branch that has diverged is left for you to merge. The line's menu offers the same merge source and merges for every branch it
  carries, and the toolbar shows the merge source until it is cleared.
- The branches sit in a column of their own beside the graph, aligned on every line.
- The columns have a header, and each one can be resized by dragging the boundary beside its title;
  the message column takes whatever is left, so a long subject is trimmed rather than pushing the
  rest of the line out of view.
- A line's menu opens from anywhere on the line.
- A merge is drawn at half the size of a commit, so the commits stand out in a busy graph.
- The search box marks the commits it finds and hides nothing: the graph you are reading stays the
  graph git drew. It says how many lines it found, and searches the messages of the commits you have
  loaded.
- The graph always shows every branch and follows every parent of every merge — and any branch but
  the one you are on can be hidden from it, with the eye on its row in the branches dialog: the
  commits only that branch brings and its badge leave the graph, while a commit another branch still
  reaches stays. The toolbar says how many branches are hidden and shows them all again in one click,
  and the choice is remembered for the repository.

### The diff

- Colour-coded additions and deletions with word-level highlighting inside a changed line, unified
  or side by side.
- Tints tuned for contrast in both themes, so a changed word is readable rather than merely coloured.
- Side by side shows the whole file on both sides — not the changed parts alone — and the two sides
  scroll together, vertically and sideways, so a line on the left is always beside its counterpart.
- Unified shows the change itself, with the context you chose; expand it around a change, or open
  the whole file.
- Instead of a vertical scrollbar, a minimap beside the patch: it draws where the additions and the
  removals are over the whole file, marks the part you are looking at, and scrolls there when you
  click or drag it.
- The changed files of a commit as a list or a tree, whichever you prefer.

### Working with the repository

- Branches: create, rename, delete, set an upstream, check out — including anything in the graph,
  with a plain warning before HEAD is detached.
- Every local branch row says where it stands with its remote: an up arrow with the number of
  commits to push, a down arrow with the number to pull, and a badge saying whether the branch is
  on a remote, is on none, or names an upstream that has been deleted.
- Branches, tags and remotes open from the history's toolbar as dialogs over the graph, drawn on the
  window's own background; a question one of them asks opens above it, and the dialog is still there
  once it is answered.
- The branches, tags and remotes lists select a row, with the pointer or the keyboard, and keep the
  selection while the page refreshes underneath.
- Drag one branch onto another to merge them. The drop opens a menu naming both ends: merge, merge
  fast-forward only, or merge the other way round if that is what you meant. The target is checked
  out first when it is not the branch you are on, and dismissing the menu does nothing. Hold the
  dragged branch near the top or the bottom of the list and it scrolls, so a branch further down is
  still a branch you can drop on, and Escape calls the whole thing off.
- Tags: create (lightweight or annotated) and delete.
- Working directory: status, stage and unstage by file or by directory, discard, and commit with a
  subject/body guide.
- Stash: create, apply, pop and drop, with the files of an entry browsable before you apply it.
- Remotes: add, rename, remove; fetch, pull and push, with a failure classified into a sentence you
  can act on rather than git's stderr.
- Merge: merge a branch, and when it conflicts, resolve it region by region with ours, theirs, both,
  the original or your own text — beside a live preview of exactly the file that will be written.
  A merge is always recorded as a merge commit, even when a fast-forward would do; the
  fast-forward-only merges move a branch without one.

### Hosting

- Connect GitHub, GitLab or Azure DevOps with a personal access token, on the public instances and
  on self-hosted ones, and browse and clone the repositories the account can reach.
- Open a commit, a branch or a file on its host, from the graph and from the file list.
- Tokens are encrypted at rest and redacted from every log line.

### Your git identity

- An **Identity** page of its own — on the start window and in every repository window, apart from
  the settings — for the name and email git records on every commit: your global `user.name` and
  `user.email`, read from git and written back with one Save. It says so when git has no identity
  yet, which is when git refuses to commit.
- Profiles: keep each name and email you commit as under a label — work, personal — and make one
  your global identity with **Use**. The profile matching what git has is marked *Current*, even
  after the identity was changed in a terminal. Adding one starts from your global identity;
  deleting one asks first and never touches git's configuration.
- A repository's own identity: in a repository's window the page gives that repository a name and
  email of its own — written to its local configuration, so its commits use them whatever the
  global identity is — and **Remove** puts it back on the global one. **Copy from current profile**
  fills the two fields from the profile marked *Current*, ready to save. The page says which
  identity the repository's commits will use.

### The application

- A start window with the recent repositories, and the open, clone and create actions; each
  repository opens in a window of its own, and `Enigma.GitClient.App <path>` opens one straight away.
- A splash screen while the application starts — its icon, its name and its version — held for a
  second at the least and gone the moment the first window is up.
- An **About** dialog, from the repository window's toolbar and from the Settings page in either
  window: the version, the build it was cut from, the copyright, and what the application is built
  with, under which licence.

### Installing

- On Linux, `packaging/linux/install.sh` builds and installs the application for you — no root,
  nothing outside your home directory — with **Enigma git client** in your application launcher, an
  `enigma-git-client` command, and its icon. The .NET runtime is bundled by default;
  `--framework-dependent` uses an installed one instead. Run it again to upgrade;
  `packaging/linux/uninstall.sh` takes it all away and leaves your settings alone.

### Preferences

- Theme, history page size, date style, graph row height and lane width, the file list's shape, the
  diff's shape, font family and size, context, tab width, whitespace handling and wrapping, the pull
  strategy, and the path to git.

### Compatibility

- Linux and Windows, from the same code.
- git 2.20 or newer, on the `PATH` or named in the settings.
- .NET 10 — bundled with the application by the Linux installer, or installed, for a
  framework-dependent build.

### What this client does not do

- **It never rebases.** `git pull` always carries `--no-rebase`, whatever the repository is
  configured to do, and the command layer refuses the verb outright.
- **It does not handle issues or pull requests.** The hosting integrations cover repositories,
  cloning and deep links; no issue or pull-request scope is ever requested from a host.
