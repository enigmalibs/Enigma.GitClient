# Release notes

## 1.0.0 — unreleased

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

### Preferences

- Theme, history page size, first-parent history, date style, graph row height and lane width, the
  file list's shape, the diff's shape, font family and size, context, tab width, whitespace handling
  and wrapping, the pull strategy, and the path to git.

### What this client does not do

- **It never rebases.** `git pull` always carries `--no-rebase`, whatever the repository is
  configured to do, and the command layer refuses the verb outright.
- **It does not handle issues or pull requests.** The hosting integrations cover repositories,
  cloning and deep links; no issue or pull-request scope is ever requested from a host.
