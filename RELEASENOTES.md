# Release notes

## 1.0.0 — unreleased

The first release: a cross-platform git client built around two things, the commit graph and the
diff.

### The graph

- A commit graph with coloured lanes, merge curves, reference badges and virtualised scrolling, so a
  repository with a hundred thousand commits scrolls like a list of ten.
- Author, timestamp and the 7-character short hash on every row, with the full date in the tooltip.
- The uncommitted changes sit at the top of the graph and lead to the working directory.
- Double-click checks out a row's branch, or the commit itself.

### The diff

- Colour-coded additions and deletions with word-level highlighting inside a changed line, unified
  or side by side.
- Tints tuned for contrast in both themes, so a changed word is readable rather than merely coloured.
- Expand the context around a change, or open the whole file.
- The changed files of a commit as a list or a tree, whichever you prefer.

### Working with the repository

- Branches: create, rename, delete, set an upstream, check out — including anything in the graph,
  with a plain warning before HEAD is detached.
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

### Preferences

- Theme, history page size, first-parent history, date style, graph row height and lane width, the
  file list's shape, the diff's shape, font family and size, context, tab width, whitespace handling
  and wrapping, the pull strategy, and the path to git.

### What this client does not do

- **It never rebases.** `git pull` always carries `--no-rebase`, whatever the repository is
  configured to do, and the command layer refuses the verb outright.
- **It does not handle issues or pull requests.** The hosting integrations cover repositories,
  cloning and deep links; no issue or pull-request scope is ever requested from a host.
