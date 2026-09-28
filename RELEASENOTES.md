# Release notes

## 4.1.0 — 2026-09-28

A minor release. A tag can be pushed to the remote on its own, from its badge in the history or from
its line in the Tags dialog, and *Create a tag* suggests a bare `1.0.0`. It also fixes the history's
column titles, and dialog questions that were cut off at the dialog's edge. Nothing is removed, and
nothing that is stored changes.

### Tags

- **Push one tag to the remote:**
  - `Push "<name>"` on a tag's badge in the history, above *Copy tag name*;
  - *Push to the remote* on a tag's line in the Tags dialog, after *Check out*.

  Only that tag is pushed, lightweight or annotated. Before, a tag only went along with a branch
  push, and only if it was annotated.
- **Where it goes:** to the remote the current branch pushes to, or `origin` when it has no upstream,
  as a push of the branch does. The push goes out under the repository's profile like any other: a
  profile with no integration for that remote does not push, and says so.
- **A tag the remote already has on another commit is never replaced.** You are told so, and can
  delete it there or give yours another name.
- **Create a tag** suggests `1.0.0` as the name instead of `v1.0.0`. It is only a suggestion; a name
  with a `v` is accepted as before.

### Fixes

- **Every column title in the history has the same room on its left.** *Author*, *Date* and *Commit*
  used to touch the separator before them, and *Graph* the page's edge. Their separators now sit in
  the gap before the column, as the others do, and *Graph* starts where the lanes do.
- **A long question is never cut off.** *Discard uncommitted files…* asks a question that used to
  run past the dialog's edge. It now goes onto as many lines as it needs, and so does every question
  the app asks in plain text: deleting a stash, a tag or a branch, and the conflict page's questions.
  A long name with no space in it is broken too.

### Upgrading from 4.0

- Nothing changes in the way you work, and nothing is migrated. `settings.json`,
  `host-accounts.json`, `identity-profiles.json` and the tokens stay as 4.0 wrote them, so going back
  to 4.0 keeps them.

### Dependencies

- No package had an update outside the Avalonia set.
- The Avalonia set (Avalonia, Avalonia.Desktop, Avalonia.Themes.Fluent, Avalonia.Fonts.Inter, and
  Avalonia.Headless and Avalonia.Skia in the tests) is still held back at **12.1.1**. That is the set
  Enigma.Avalonia.Desktop 1.1.0 is built against. 12.1.3 is out; the set moves as a whole, as a
  decision of its own.

### Version

- **4.1.0** is a minor release under Semantic Versioning. It adds backward-compatible functionality,
  pushing one tag, along with fixes. Nothing is removed or changed incompatibly, and nothing that is
  stored changes.

## 4.0.0 — 2026-09-28

A major release. A commit's details open in a dialog, as text you can select, and the diff view's
header shrinks to one line. The Changes page gets a blue way back to the history, Escape included.
Every discard is red and asks one plain question, and the history's uncommitted line can discard all
the uncommitted work at once. About opens from the start window's home. It also fixes the start
page's empty state and the file lines' menus. The Changes page no longer amends or signs off a commit,
and that is why this is 4.0: read *Upgrading from 3.x* below.

### Commit details

- **The diff view's header is one line:** the way back, a details button, and the commit's subject.
  The author, the date, the hash and the description no longer stretch it to four lines.
- **The details button opens *Commit details*,** which shows:
  - the title and the description;
  - the author's name and email;
  - the date, followed by how long ago it was in parentheses;
  - the full hash.

  Every value is text you can select and copy; none of it is a text box.
- **Show commit details** is on every history line that is a commit, stashes included, right after
  **Show what it changed**. It opens the same dialog for that line's commit.

### The Changes page

- **A back button** at the head of the page's header returns to the history. So does **Escape**,
  from anywhere on the page, the commit message included (what you typed is kept).
- **Both back buttons are blue:** the Changes page's and the diff view's. A white arrow on the
  application's blue is the first thing the eye finds.
- **The commit box has no *Amend* and no *Sign off* any more.** It records a new commit from what is
  staged, and nothing else.

### Discarding

- **Discard everything** is red for as long as there is something to discard, and looks like any
  disabled button when there is not.
- **It asks one plain question:** "Throw away every change in *N files*? This cannot be undone." There
  is no repository name to type any more. The confirm button is red, and *Cancel* is the default.
- **Every discard confirms in red,** a one-file discard included. The window's other questions keep
  their usual colours.
- **Discard uncommitted files…** on the history's uncommitted line, beside **Stash all changes…**,
  throws every uncommitted change away:
  - staged or not, untracked files included, back to the last commit;
  - a staged rename is undone;
  - files git ignores stay.

  It asks first, in red, naming how many files. It is not offered before the first commit, or while
  a merge or another operation is in progress — *Abandon the merge* is the way out of one.

### The start window

- **About** opens from the Info button at the end of the Repositories page's header, as it does from
  the repository window's toolbar.

### Fixes

- **"No repositories yet" is centred** on the start page. It used to sit against the left edge.
- **A file line's menu opens wherever the line is right-clicked:** its padding, its right end and, in
  the tree, its indentation. That covers the Changes page's *Not staged* and *Staged* lists and the
  diff view's file list. Before, it opened only over the line's text.

### Upgrading from 3.x

- **Amending and signing off now need git itself.** The Changes page's *Amend* and *Sign off* are gone:
  - use `git commit --amend` to replace the last commit;
  - use `git commit --signoff` (or `-s`) to add a `Signed-off-by` trailer.

  Nothing else in the way you commit changes.
- **Discard everything no longer asks for the repository's name.** One click on its red confirm button
  discards; *Cancel* stays the default.
- Nothing is migrated. `settings.json`, `host-accounts.json`, `identity-profiles.json` and the tokens
  stay as 3.1 wrote them, so going back to 3.1 keeps them.

### Dependencies

- No package had an update outside the Avalonia set.
- The Avalonia set (Avalonia, Avalonia.Desktop, Avalonia.Themes.Fluent, Avalonia.Fonts.Inter, and
  Avalonia.Headless and Avalonia.Skia in the tests) is still held back at **12.1.1**. That is the set
  Enigma.Avalonia.Desktop 1.1.0 is built against. 12.1.3 is out; the set moves as a whole, as a
  decision of its own.

### Version

- **4.0.0** is a major release under Semantic Versioning. It removes functionality, *Amend* and *Sign
  off*, which stops the workflow of anyone who used them until they switch to git for it. As for 2.0.0
  and 3.0.0, an upgrade that can stop an existing workflow is a major one. Everything else in it is
  new, backward-compatible functionality and fixes, and nothing that is stored changes.

## 3.1.0 — 2026-09-28

A minor release. Stashes are handled in the history the way GitKraken does it, one branch can be
merged into another by dragging it there, the branch and tag lists can be sorted, and every context
menu gains icons and does more. It also fixes badges that arrived late, Azure DevOps sign-in, and the
diff of a new file. Nothing is removed, and `settings.json` only gains keys.

### Stashes

- **Every stash is one line in the history,** as GitKraken draws it. The line sits at the stash itself
  (the commit `git stash list` names), branches off the commit it was made on, and carries a stash
  badge naming its entry: `stash@{0}`, `stash@{1}`, …
  - git's own bookkeeping commits for a stash ("index on …", "untracked files on …") are no longer
    lines of their own;
  - older entries are drawn too, not only the newest.
- **Stash from the history:** the **Stash** button in its toolbar, or **Stash all changes…** on the
  uncommitted line. A dialog asks for an optional message. Untracked files go with the stash.
- **A stash line's menu:**
  - **Apply stash** brings the changes back and keeps the stash;
  - **Pop stash** brings them back and removes the stash. If they conflict with your files, the stash
    is kept, and you are told so;
  - **Delete stash…** asks first, with the harmless button as the default.
- A pop or an apply that git refuses, because uncommitted work would be overwritten, changes nothing
  and says why.
- The Changes page's stash list behaves the same way and uses the same words.

### The history

- **The branch and tag badges appear as soon as git has named them.** Before, opening a repository
  with many branches could draw the lines first and the badges only at the next automatic refresh.
- **A loader** runs along the top of the history while it is still reading its commits or, just after
  opening, its references.
- **The graph is a column like the others:** titled, and resized from its grip. Until you drag it, it
  follows the lanes in view.
- **Drag a branch badge onto another** to merge it there. The menu at the drop names both branches in
  full: `Merge "feature" into "main"` or `Merge "feature" into "main", fast-forward only`. Escape
  cancels the drag.

### Branches and tags

- **Sort the branches dialog and the tags dialog** by name or by date, ascending or descending.
  - Newest first by default: a branch by its tip commit, a tag by the commit it tags.
  - Each list remembers its own choice.
- **Select in the history** on a branch's or a tag's line closes the dialog and selects that commit's
  line, reading further into the history if it is not loaded yet.

### Menus

- The menus of the branches, tags and remotes lines, of changed files and of stashes open wherever the
  line is right-clicked, not only over its text.
- **Copy branch name** on a branch badge, **Copy tag name** on a tag badge, and **Copy short commit
  hash** / **Copy full commit hash** on a commit's line.
- Every menu item that does something has an icon for its kind of action.

### Fixes

- **Azure DevOps:** connecting an account no longer fails with "Azure DevOps answered 400 Bad Request".
  The identity check now asks for the preview version of the API that only exists as a preview.
- **The diff of a new file on the Changes page** shows every line as added (nothing on the left,
  everything green on the right), as a commit's added file does. Before, it said "This change touches
  no lines of text".

### Dependencies

- No package had an update outside the Avalonia set.
- The Avalonia set (Avalonia, Avalonia.Desktop, Avalonia.Themes.Fluent, Avalonia.Fonts.Inter, and
  Avalonia.Headless and Avalonia.Skia in the tests) is held back at **12.1.1**. That is the set
  Enigma.Avalonia.Desktop 1.1.0 is built against. 12.1.3 is out; the set moves as a whole, as a
  decision of its own.

### Version

- **3.1.0** is a minor release under Semantic Versioning: new, backward-compatible features and fixes.
  `settings.json` gains `branchSortKey`, `branchSortDirection`, `tagSortKey` and `tagSortDirection`.
  An older file reads them as the defaults, and 3.0 ignores them. Nothing else changes in what is
  stored.

## 3.0.0 — 2026-09-27

A major release: git now signs in with your profile's integration. Connect an account under a profile
once, and every fetch, pull, push and clone to that host works over HTTPS with nothing to set up in
git. On those hosts the integration's token takes the place of your own credential helper, so a token
that can only read no longer pushes there. That is why this is 3.0: read *Upgrading from 2.0* below.

### Signing in

- **Git signs in with the profile's integration.** In a repository that commits as a profile, every
  network operation to an HTTPS remote on one of that profile's hosts signs in with the integration's
  token: fetch, pull, push, a branch's fast-forward, the quiet background fetch, pushing or deleting a
  tag on a remote, and deleting a remote branch.
- **A clone signs in too.** It uses the integration you browsed the repository from, or else the
  current profile's integration for that host. The current profile is the one matching your global
  identity.
- The token reaches that one git process through its environment. It is never on git's command line,
  never written to git's configuration or to the repository, and never handed to a credential helper
  that could store it.
- On those hosts the integration's token replaces your own credential helper. Everywhere else,
  nothing changes:
  - SSH remotes keep using your SSH key;
  - hosts that none of the profile's integrations cover keep using git's own credentials;
  - a repository whose identity matches no profile works as before.
- The token of an `https` integration is never sent to an `http://` remote.
- When the profiles or a token cannot be read, git runs with its own credentials, and the log says
  why.

### Refused tokens

- When the host refuses a profile's token, the message says so and names the scope a push needs: *The
  host refused this profile's token. It may have expired, or not allow this: pushing needs write
  access…* Replace the token on the Profiles page: disconnect the account and connect it again with a
  new one.
- When no token was used, the message points at the profile's integrations, the credential helper or
  the SSH key instead.
- An HTTP 401 or 403 from the host now reads as a refused sign-in. Before, it read as a network
  problem.
- **Connect an account** names the write scope a push needs for each host, and so does the README.

### The history

- The history follows the remote branches and tags on its own. After a push from the toolbar, the
  remote branch's badge moves to the pushed commit straight away; before, it stayed where it was until
  you pressed **Refresh**.
- A pull or a fetch from the toolbar redraws the history as soon as it ends too, and only when
  something moved: the selected commit and the scroll position stay where they were.
- Whatever moved between two automatic refreshes — a remote branch, a tag, a branch — is redrawn by
  the next one: every 15 seconds by default, as *Fetch and refresh automatically* on the Settings page
  sets it. Before, the automatic refresh only noticed what its own fetch brought.

### Upgrading from 2.0

- **If you push over HTTPS with a token that can only read, replace it.** 2.0 asked for read-only
  scopes, because git signed in on its own; 3.0 pushes with the token. Create one with write access:
  - GitHub: **Contents: Read and write** for a fine-grained token, or the classic `repo` scope;
  - GitLab: `write_repository` instead of `read_repository`;
  - Azure DevOps: **Code: Read & write**.

  Then, on the Profiles page, disconnect the account and connect it again with the new token. Until
  you do, a push to that host is refused, with a message saying so.
- **A fine-grained GitHub token signs in only to the repositories it was given.** Git no longer falls
  back to your own credentials for the others on that host, so give the token every private
  repository you work with under that profile.
- Nothing to do if your GitHub token is a classic one with the `repo` scope, if your remotes use SSH,
  or if you do not use profiles.
- Nothing is migrated. `host-accounts.json`, `identity-profiles.json`, `settings.json` and the tokens
  stay as 2.0 wrote them, so going back to 2.0 keeps them.

### Dependencies

- No package had an update outside the Avalonia set.
- The Avalonia set (Avalonia, Avalonia.Desktop, Avalonia.Themes.Fluent, Avalonia.Fonts.Inter, and
  Avalonia.Headless and Avalonia.Skia in the tests) is held back at **12.1.1**. That is the set
  Enigma.Avalonia.Desktop 1.1.0 is built against. 12.1.3 is out; the set moves as a whole, as a
  decision of its own.

### Version

- **3.0.0** is a major release under Semantic Versioning. On the hosts a profile covers, git now signs
  in with the profile's token instead of your own credential helper. So an upgrade can refuse pushes
  that 2.0 ran with a read-only token, until you replace it. The file formats are unchanged.

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
