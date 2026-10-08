# Release notes

## 5.8.0 — 2026-10-08

A minor release:

- the files panel beside the history opens or closes every folder of its tree at once;
- a folder in that panel is never selected: a click folds it, and the file you were reading stays;
- the diff area's messages stand alone instead of half hidden behind an empty diff;
- the About dialog is down to the essentials.

### The files panel

- **Expand all, collapse all.** While the panel shows its files as a tree, two buttons to the left of
  the list/tree toggles open every folder, or close every folder. They are there for the tree only,
  in a commit's files and in both halves of the working tree. The selected file stays selected, with
  its diff, and the automatic refresh leaves the folders as you set them.
- **A folder is never selected.**
  - A click on a folder's line folds or unfolds it, as its arrow does; a double-click does it once.
  - The file you had selected keeps the selection, and its diff stays open. With nothing selected,
    the history stays on screen.
  - The arrow keys pass over a folder without selecting it.
  - A folder's right-click menu is unchanged.

### The About dialog

- **Just the essentials:** the icon, the name, the version, the build, the copyright, and a *Close*
  button. The *About* title, the info icon beside the content and the *Built with* list are gone.

### Fixes

- **One click on a file selects it.** After a folder, or a file at the top of the tree, a click on a
  file inside a folder could leave nothing selected. The file had to be clicked again to show its
  diff. It now takes one click, whatever was selected before.
- **A folder no longer leaves an empty diff on screen.** Selecting a folder while a file's diff was
  open left the diff page saying *Select a file to see what changed in it*. Getting back took a click
  to let go of the folder and another on a file. Folders are no longer selected, so this cannot
  happen.
- **The diff area's messages are no longer half hidden.**
  - The messages are for a binary file, a file only renamed, a mode change, or no file selected.
  - Each one used to be drawn behind an empty diff, line-number gutter and all.
  - It now stands alone, centred.

### Upgrading from 5.7

- **Nothing you saved changes.** `settings.json` stays at version 6, and going back to 5.7 keeps
  everything.
- A click on a folder in the files panel folds it instead of selecting it.

### Dependencies

- Microsoft.Testing.Extensions.CodeCoverage 18.11.2 → 18.12.0 (the test projects only).
- The Avalonia set is still held back at **12.1.1** with Enigma.Avalonia.Desktop 1.2.0 (12.1.3 is
  out; the set moves as a whole).

### Version

- **5.8.0** is a minor release under Semantic Versioning. It adds backward-compatible behaviour (the
  expand and collapse buttons) and fixes. The About dialog's *Built with* list is the only thing taken
  out, and the stored settings do not change.

## 5.7.0 — 2026-10-06

A minor release with one fix: a stash's line in the history lists every file the stash holds,
including the untracked files it took.

### Fixes

- **A stash's line lists its untracked files.**
  - Selecting a stash in the history listed only its changes to tracked files. New files that git
    did not track yet were missing, although the stash had taken them and a pop or an apply brought
    them back. Nothing was ever lost; they just were not shown.
  - Now every file is listed. An untracked file shows as added, and its diff shows every line
    added.
  - Stashes are made with their untracked files, and git keeps those apart from the rest of the
    stash, which is where the list did not look.

### Upgrading from 5.6

- **Nothing you saved changes.** `settings.json` stays at version 6, and going back to 5.6 keeps
  everything.
- A stash made before 5.7 shows its untracked files too: they were always in it.

### Dependencies

- No package had an update outside the Avalonia set. The set is still held back at **12.1.1** with
  Enigma.Avalonia.Desktop 1.2.0 (12.1.3 is out; the set moves as a whole).

### Version

- **5.7.0** is a minor release under Semantic Versioning. It carries a fix only, which a patch
  release (5.6.1) could also have carried. Nothing is removed, and the stored settings do not
  change.

## 5.6.0 — 2026-10-06

A minor release:

- the history's search finds a commit by its SHA and its author as well as by its message, and steps
  through what it found;
- the diff of an uncommitted file no longer jumps back to the top at every automatic refresh;
- the start window's rows open their repository's folder and a terminal in it, as the repository
  window's toolbar does.

### The history

- **The search finds SHAs and authors.**
  - A commit is found by the start of its SHA, so a short hash pasted from a terminal finds it, in
    either case.
  - A commit is found by its author's name or email, anywhere in them.
  - The message is searched as before, subject and body.
  - The box says so: *Search messages, SHAs and authors*.
- **Step through what it found.** On the right of the box, `match 3/12` says which of the lines
  found is selected, and `match –/12` that the selected line is not one of them. The up and down
  arrows beside it select the previous and the next one, and bring it into view; the details panel
  follows as it does for a click. In the box, **Enter** goes to the next match and **Shift+Enter**
  to the previous one. Both wrap around the lines that are loaded. `no match` still says when
  nothing was found.
- **Roomier ref badges, and a check on the checked-out branch.** The pills naming branches, tags and
  stashes have a little more room around their name. The checked-out branch's pill starts with a
  check, to the left of its branch icon: it says "checked out" in a shape as well as in a colour.

### The start window

- **Open a listed repository's folder, or a terminal in it.** Every row of the list has the two
  buttons the repository window's toolbar has: the folder in the file manager, and a terminal in the
  folder. A repository that has moved says so instead.

### Fixes

- **The diff of an uncommitted file keeps your place.** The automatic refresh (every 15 seconds by
  default) used to send you back to the file's first change and clear your selected text. Now a
  refresh that finds the file unchanged leaves everything alone: the scroll position, the selected
  text, the context you widened, a *Show anyway*. The diff is drawn again only when the file
  changed, and cleared when the file is no longer in the list. A file staged or unstaged in a
  terminal shows the other half's diff.
- **The horizontal scrollbar no longer hides the diff's last line.** The bar grows under the
  pointer, and it covered the last line even with the diff scrolled to its end. Every diff, unified
  and side by side, now leaves the bar's room under its last line.
- **Opening a folder no longer reports a failure on Windows.** *Open the repository's folder* opened
  it and then said *The folder did not open*. Windows hands a folder to the Explorer that is already
  running, which the app mistook for nothing having started. The same mistake could affect a file
  opened from the changed-files list and a link opened in the browser.

### Upgrading from 5.5

- **The search finds more than it did.** A word that starts a SHA, or that is part of an author's
  name or email, now finds those commits too: searching `ada` finds every commit by Ada.
- **The count moved.** What the search found is on the right of the box, as `match xx/yyy`, instead
  of `N matches` on its left.
- **The Refs column starts a little wider**, for the roomier badges and the check.
- **Nothing you saved changes.** `settings.json` stays at version 6, and going back to 5.5 keeps
  everything.

### Dependencies

- No package had an update outside the Avalonia set. The set is still held back at **12.1.1** with
  Enigma.Avalonia.Desktop 1.2.0 (12.1.3 is out; the set moves as a whole).

### Version

- **5.6.0** is a minor release under Semantic Versioning. It adds backward-compatible behaviour: the
  wider search and its navigation, the start window's buttons, and the check on the checked-out
  branch. Nothing is removed, and the stored settings do not change.

## 5.5.0 — 2026-10-05

A minor release. The diff is drawn by a real text editor, AvaloniaEdit, the editor Enigma's
MarkdownEditor uses, instead of a list of rows:

- its text selects as in any editor;
- Ctrl+F searches it;
- its code is syntax-highlighted by the file's language;
- it scrolls as one document.

What you read is unchanged: the line numbers, the `+` and `−` markers, the tints, the word-level
highlight, the hunk bands and the minimap.

### The diff

- **Text selection, as in an editor.** Drag across the text, double-click a word, triple-click a
  line, extend with Shift and the arrows, or select everything with Ctrl+A. Ctrl+C or the right-click
  *Copy* copies the code alone: no line numbers, no markers, no hunk bands, no filler lines. Side by
  side, each side selects on its own, and selecting in one clears the other.
- **Ctrl+F searches the diff.** The editor's own search box opens over the text. Side by side, it
  searches the side you are in.
- **Syntax highlighting.** The code is coloured by the file's extension, in Visual Studio Code's
  Dark+ or Light+ to match the theme, over the diff's own tints: a keyword on an added line is green
  behind, the keyword's colour in front. A file whose language is not known stays plain. Each side of
  the side-by-side view is one file's code and highlights cleanly. In the unified view, an added line
  right after a removed one can lose its colours, because the two files' lines are read as one.
- **One document, scrolled smoothly.** The diff is one text instead of a list of rows, built once per
  file. The line numbers stay put while long lines scroll sideways. Side by side, the two sides
  scroll together, down and sideways, and each stops at its own longest line.
- **Unchanged:**
  - a click on a hunk band still shows more lines around it;
  - the minimap still shows where the changes are and takes you there;
  - a file still opens at its first change;
  - *Show spaces and tabs*, the tab width, the font and the theme apply as before.

### Fixes

- **A drag to the end of a diff no longer freezes the application.** Dragging a selection past the
  last line, with the pointer at the bottom-right, used to hang the window. The I-beam cursor stayed,
  the close button did nothing, and memory kept climbing. The selection now runs to the end of the
  file, and the editor settles there.

### Upgrading from 5.4

- **Side by side no longer wraps long lines.** Its two sides are two editors, and two editors
  wrapping on their own would put the rows out of line. The toolbar's wrap toggle is greyed out while
  side by side is shown. *Settings → Diff → Wrap long lines in the unified view* is the same setting
  as before, and the unified view still wraps when it is on. Long lines side by side scroll sideways.
- **Rows can no longer be selected in the diff.** Select the text instead: *Copy* copies what is
  selected, and *Copy the whole patch* on the toolbar is unchanged.
- **Nothing you saved changes.** `settings.json` stays at version 6, and going back to 5.4 keeps
  everything.

### Dependencies

- **New:** `Avalonia.AvaloniaEdit` **12.0.0** and `AvaloniaEdit.TextMate` **12.0.0** (MIT), the pair
  Enigma.MarkdownEditor uses. They are built against Avalonia 12.0 and run on the 12.1 this app pins,
  so they are versioned on their own, apart from the Avalonia set.
  - They bring TextMateSharp **2.0.3** and its grammars (MIT).
  - They also bring the native regular-expression library `onigwrap` **1.0.10** (MIT), built on
    Oniguruma (BSD). A `linux-x64` or `win-x64` publish carries it, so the Linux installer needs
    nothing new.
- No other package had an update outside the Avalonia set. The set is still held back at **12.1.1**
  with Enigma.Avalonia.Desktop 1.2.0 (12.1.3 is out; the set moves as a whole).

### Version

- **5.5.0** is a minor release under Semantic Versioning. It adds backward-compatible behaviour:
  selection, search and highlighting in the diff. What it takes away are ways of viewing, not data:
  side-by-side wrapping and row selection. The stored settings do not change.

## 5.4.0 — 2026-10-05

A minor release. A commit can be reverted from its line in the history, next to the reset. The
changed files open as a tree of folders instead of a flat list. A profile can name a base directory,
where the start window's Open, Clone and Create begin while that profile is picked.

### The history

- **Revert this commit…** — every commit line's menu offers it after the two reset items, while HEAD
  is on a branch. It asks *Revert abc1234?*, naming the commit's subject and the branch, with
  *Revert* as the default. Confirmed, it records a new commit on that branch, with git's own message
  (*Revert "…"*), that undoes what the commit changed. The commit itself stays in the history, and the
  history reloads with the revert on top.
  - **A merge** is reverted against the branch it was merged into: what it brought in is undone.
  - **A revert that would conflict** is abandoned on the spot. Nothing changes, and the warning names
    the files. The app never leaves a revert in progress.
  - **A change that is already undone** commits nothing, and you are told so.
  - **It is refused, with a sentence:** when uncommitted work is in the way, on a detached HEAD, and
    while a merge, cherry-pick or another revert is in progress. It is greyed out on the uncommitted
    line.

### The changed files

- **A tree of folders by default.** A commit's files and the uncommitted files in the details panel,
  and a stash's files, open as a tree instead of a flat list. *Settings → Changed files → Show them as*
  still switches every panel, and each panel's own toggle still switches the one in front of you.

### The profiles

- **A base directory per profile.** The Add and Edit profile dialogs have an optional *Base directory*
  field with *Browse*. While that profile is picked on the start window:
  - **Open** starts its folder picker there;
  - **Clone** and **Create** suggest it, so their *Browse* starts there too. For a clone it comes
    before the directory the last clone went to, which still serves a profile without one.

  A clone picked from one of the profile's integrations goes there as well. The field takes a full
  path. A directory that is missing when you press the button, such as an unplugged drive, counts as
  none.

### Upgrading from 5.3

- **The changed files open as a tree.** `settings.json` moves to version 6. If it said *a flat list*,
  the default of every release so far, it now says *a tree of folders*. If you prefer the list, choose
  it again under *Settings → Changed files*: from now on that choice is kept.
- `identity-profiles.json` gains an optional `baseDirectory` for each profile. Profiles without one
  read as before.
- **Going back to 5.3** keeps everything: 5.3 reads the newer settings file for the keys it knows, and
  ignores the base directories. A profile that 5.3 saves loses its base directory.

### Dependencies

- No package had an update outside the Avalonia set, which is still held back at **12.1.1** with
  Enigma.Avalonia.Desktop 1.2.0 (12.1.3 is out; the set moves as a whole).

### Version

- **5.4.0** is a minor release under Semantic Versioning. It adds backward-compatible behaviour: the
  revert, the base directory and the tree default. Nothing is removed. The stored settings move
  forward in a way 5.3 still reads.

## 5.3.0 — 2026-10-05

A minor release. Picking a profile in the start window now makes it the identity git commits with, and
a new clone offers to keep that identity as its own. In the history, the uncommitted line is joined to
the checked-out commit by a dashed line, as in GitKraken. The repository window opens maximised, on its
history, fetches the repository as it opens, and has buttons for the repository's folder and a terminal
in it. Three annoyances are fixed: the automatic refresh no longer reopens the folders you closed in the
uncommitted files, the column titles no longer run over the details panel, and a repository no longer
opens on the page the previous one was left on.

### The profiles

- **The start window's picker switches the identity.** Choosing a profile shows its repositories and,
  as the Profiles page's *Use* does, makes its name and email git's global identity (*Using Work — New
  commits are made as …*). A profile with no name and email, such as *Default*, only switches the list:
  git's identity is left alone. Picking the profile git already commits as writes nothing.
- **The Profiles page's *Use* selects that profile in the picker**, so the start window always shows the
  profile git commits as.
- **A clone offers the global identity.** Once a clone has finished, from the clone dialog or from a
  host's repository list, and git has a name and an email, you are asked whether to write them into the
  new repository's own configuration, so its commits keep that identity whatever the global one
  becomes. *Not now* is the default and writes nothing.

### The history

- **The uncommitted line is joined to HEAD.** Its dashed circle now sits in the lane of the checked-out
  commit, and a dashed line runs down that lane to it, past any branch with newer commits, which opens
  beside it. Before, the circle was always in the first lane and joined, by a solid line, whatever was
  drawn there.
- **Collapsed folders stay collapsed.** In the uncommitted files shown as a tree, the automatic refresh
  (every 15 seconds by default) rebuilt the tree and reopened every folder. It now leaves the list alone
  when nothing changed, and keeps the folders you opened or closed when something did.
- **The column titles stop at the details panel**, as the rows do, when the panel leaves the columns
  less room than they need.

### The repository window

- **It opens maximised and centred**, every time a repository is opened.
- **It opens on the history.** Opening a repository after closing another one on its Profiles or
  Settings page, or cloning one from the Profiles page, now shows the new repository's history.
- **It fetches the repository as it opens**: the same quiet fetch as the automatic refresh, in the
  background, so the window does not wait for it — and also when the automatic refresh is off. The
  automatic refresh and the refresh button already fetched from every remote; a fetch that fails there
  is quiet, and the toolbar's *Fetch* button says why.
- **Two buttons for the repository's folder**, after fetch, pull and push: one opens it in the file
  manager (Explorer on Windows), the other opens a terminal in it — Windows Terminal or the command
  prompt on Windows, Terminal on macOS, `$TERMINAL` or the usual emulators on Linux.

### Upgrading from 5.2

- **The picker now writes git's global configuration.** In 5.2, choosing a profile in the start window
  only changed the list shown; in 5.3 it also sets `user.name` and `user.email` in your global git
  configuration, unless the profile has none.
- Nothing is migrated. `settings.json`, `repository-lists.json`, `host-accounts.json`,
  `identity-profiles.json` and the tokens stay as 5.2 wrote them, so going back to 5.2 keeps them.

### Dependencies

- No package had an update outside the Avalonia set, which is still held back at **12.1.1** with
  Enigma.Avalonia.Desktop 1.2.0 (12.1.3 is out; the set moves as a whole).

### Version

- **5.3.0** is a minor release under Semantic Versioning. It adds backward-compatible behaviour and
  fixes three defects. Nothing is removed, and nothing that is stored changes; the one behaviour that
  changes on purpose — the picker setting the identity — is described above.

## 5.2.0 — 2026-10-02

A minor release. A tag can be deleted here or on the remote, from its badge in the history and from
its line in the Tags dialog. In the history, a branch and its upstream on the same commit are one
badge, as in GitKraken. Checking out a remote branch whose local branch is elsewhere offers to reset
the local branch to it, and a double-click on a branch badge checks the branch out. Nothing is
removed, and nothing that is stored changes.

### The tags

- **Delete a tag here or on the remote.** A tag badge's menu in the history now reads *Push "x"*,
  *Delete "x" locally…*, *Delete "x" from the remote…*, *Copy tag name*. A line's menu in the Tags
  dialog has *Delete locally…* and *Delete from the remote…*, and its trash button still deletes
  locally.
- **Both ask first, with *Cancel* as the default.** The remote delete names the remote and keeps the
  tag here. It goes to the remote a tag push goes to: the one the current branch pushes to, or
  `origin`.
- **A profile that does not push to that remote does not delete there either.** The delete is
  refused with the push's own explanation, before anything is asked.

### The branch badges

- **One badge for a branch and its upstream.** When a local branch and the remote branch it tracks
  are on the same commit, the history draws one badge:
  - it is the local branch's, with the branch icon and the remote's cloud;
  - its tooltip names both (*main and origin/main*);
  - its menu is the local branch's, plus *Delete "origin/main"…*.

  Only the configured upstream joins. Any other remote branch on the commit keeps its own badge, and
  the line's own menu still names both.
- **Reset local to here.** Checking out a remote branch whose local branch is on another commit (from
  its badge, from the Branches dialog or with a double-click) asks *Reset "main" to "origin/main"?*:
  - **Reset local to here** moves the local branch to the remote's commit and checks it out;
  - **Check out "main"** checks the local branch out where it is (not offered when it is already
    checked out);
  - **Cancel**.

  The question lists the commits only the local branch has, which a reset would leave behind. The
  reset is the default button only when there are none. Uncommitted changes come along, as for any
  checkout, and git stops rather than overwrite one. Before 5.2, this checkout was refused with *A
  branch called "main" already exists*. When the two branches are on the same commit, the local one
  is checked out without a question.
- **A double-click on a branch badge checks the branch out**, exactly as its menu's *Check out* does.
  On the checked-out branch it does nothing, and a double-click on the line beside the badges is
  unchanged.

### Upgrading from 5.1

- Nothing changes in the way you work, and nothing is migrated. These stay as 5.1 wrote them, so going
  back to 5.1 keeps them: `settings.json`, `repository-lists.json`, `host-accounts.json`,
  `identity-profiles.json` and the tokens.
- **Coming from 5.1.0**, read *Upgrading from 5.1.0* under 5.1.1 too: the program was renamed
  `Enigma.GitClient.Desktop` there, and on Linux `packaging/linux/install.sh` has to run again.

### Dependencies

- No package had an update outside the Avalonia set, which is still held back at **12.1.1** with
  Enigma.Avalonia.Desktop 1.2.0 (12.1.3 is out; the set moves as a whole).

### Version

- **5.2.0** is a minor release under Semantic Versioning. It adds backward-compatible behaviour (the
  tag deletes, the grouped badge, the reset question, the double-click). Nothing is removed or changed
  incompatibly, and nothing that is stored changes.

## 5.1.1 — 2026-10-02

A patch release. The application now carries the name every other Enigma desktop application does:
the program is `Enigma.GitClient.Desktop`, no longer `Enigma.GitClient.App`. The `enigma-git-client`
command, the launcher entry and everything that is stored stay as they were. Nothing is added or
removed.

### Fixes

- **The application's files take the Enigma desktop name.** The program the build produces, and the
  one the Linux installer puts in place, is `Enigma.GitClient.Desktop`
  (`Enigma.GitClient.Desktop.exe` on Windows), as it is for the other Enigma desktop applications. On
  Linux:
  - the window takes its class from the new name, and the installed desktop entry expects it, so the
    launcher still groups the running window under its icon;
  - `uninstall.sh` also removes an installation made by 5.1.0 or earlier, whose `enigma-git-client`
    link still points at the former program.

### Upgrading from 5.1.0

- **On Linux, run `packaging/linux/install.sh` again.** It replaces the installed application,
  re-points `~/.local/bin/enigma-git-client` and rewrites the desktop entry for the new program. The
  command and the launcher entry keep their names.
- **A script or a shortcut that starts `Enigma.GitClient.App` directly** must start
  `Enigma.GitClient.Desktop` instead (`.exe` on Windows). One that starts `enigma-git-client` needs
  no change.
- **Publish into an empty folder.** `dotnet publish -o` keeps what the folder already holds, so a
  publish over a 5.1.0 one leaves the old `Enigma.GitClient.App` files beside the new ones.
- Nothing is migrated. These stay as 5.1.0 wrote them, so going back to 5.1.0 keeps them:
  `settings.json`, `repository-lists.json`, `host-accounts.json`, `identity-profiles.json` and the
  tokens.
- **Building from source:** the project is `src/Enigma.GitClient.Desktop`
  (`dotnet run --project src/Enigma.GitClient.Desktop`), and its tests are
  `tests/Enigma.GitClient.Desktop.UnitTests`.

### Dependencies

- No package had an update outside the Avalonia set, which is still held back at **12.1.1** with
  Enigma.Avalonia.Desktop 1.2.0 (12.1.3 is out; the set moves as a whole).

### Version

- **5.1.1** is a patch release under Semantic Versioning: the application's files are renamed, and
  nothing is added, removed or changed in what it does. Nothing that is stored changes.

## 5.1.0 — 2026-10-01

A minor release. A click on the selected file in the history's details panel lets go of it and puts
its diff away, as a click on the selected line already closes the panel. The Branches, Tags and
Remotes dialogs keep their header at the top while a long list scrolls, and the info bars are softer
in the dark theme and close sooner. It also fixes the repository browser, which was cut off at both
sides. Nothing is removed, and nothing that is stored changes.

### The history's details panel

- **A click on the selected file lets go of it.** This works in a commit's files and in the
  uncommitted line's *Not staged* and *Staged*, as a list or a tree:
  - its diff goes with it and the graph comes back;
  - the line stays selected and its panel stays open;
  - the next click picks the file and opens its diff again.
- **These leave the selection as it was:**
  - a double-click;
  - a right-click, which opens the file's menu;
  - a Shift or Ctrl click;
  - the row's own *Stage* and *Unstage* buttons.

  A folder's chevron in the tree only folds it.

### The dialogs

- **The Branches, Tags and Remotes dialogs keep their header at the top** (the filter, the sort and
  *Create*) while a long list scrolls under it. In Branches the manual merge band stays there too.
  Only the list scrolls now, not the whole dialog.

### The info bars

- **Softer colours in the dark theme.** The four kinds of info bar are drawn in lighter, pastel tints
  that no longer sink into the panels around them, with a message colour made for them. The light
  theme is unchanged.
- **A success or an informational message closes itself after 2.5 seconds** instead of 5. A warning
  or an error still stays until you close it.

### Fixes

- **The repository browser is shown whole.** The dialog listing an account's repositories (*Browse*
  on a profile's integration) was wider than the dialog holding it, and was cut off on both sides:
  part of its filter and visibility toggles, and each row's *Open* and *Clone* buttons. It now fits,
  in both windows.

### Upgrading from 5.0

- Nothing changes in the way you work, and nothing is migrated. These stay as 5.0 wrote them, so going
  back to 5.0 keeps them: `settings.json`, `repository-lists.json`, `host-accounts.json`,
  `identity-profiles.json` and the tokens.

### Dependencies

- **Enigma.Avalonia.Desktop 1.2.0** (from 1.1.0), for the info bars' colours. It is built against
  the same Avalonia 12.1.1, CommunityToolkit.Mvvm 8.4.2 and Enigma.Core 1.0.0 as 1.1.0, so nothing
  else moves.
- No other package had an update outside the Avalonia set.
- The Avalonia set is still held back at **12.1.1**: Avalonia, Avalonia.Desktop,
  Avalonia.Themes.Fluent, Avalonia.Fonts.Inter, and Avalonia.Headless and Avalonia.Skia in the tests.
  That is the set Enigma.Avalonia.Desktop 1.2.0 is built against. 12.1.3 is out; the set moves as a
  whole, as a decision of its own.

### Version

- **5.1.0** is a minor release under Semantic Versioning. It adds backward-compatible behaviour (a
  click that lets go of the selected file, shorter-lived info bars) along with fixes. Nothing is
  removed or changed incompatibly, and nothing that is stored changes.

## 5.0.0 — 2026-09-30

A major release. The history has a details panel beside it: a click on a line lists the files it
changed, and the uncommitted line holds the working tree and the commit box. That is why the Changes
page is gone. The start window lists the repositories of the profile you pick, in the order you drag
them into. The diff's text can be selected and copied, a new repository starts with a README, and the
application is now called **Enigma Git Client**. The Changes page, double-clicking a history line,
*Pin to the top* and the 4.x recent-repositories list are gone, and that is why this is 5.0: read
*Upgrading from 4.x* below.

### The history's details panel

- **A click on a line opens a panel on the right** with the files that commit changed, as a list or a
  tree. A click on the selected line closes it again; a click on another line moves the panel there.
- **A file picked in the panel opens its diff over the graph**, with the panel still beside it. The
  back button and Escape close the diff and keep the line and the panel. *Show what it changed* in a
  line's menu selects the line and opens its first file.
- **The uncommitted line holds the working tree:**
  - *Not staged*, with *Discard all*, *Stash all* and *Stage all*;
  - *Staged*, with *Unstage all*;
  - the commit box, Ctrl+Enter included.

  The history refreshes as soon as you commit, stage, discard or stash, and a tree left clean takes
  the line away. The first commit of a new repository is made there too.
- **Resizable:** drag the panel's left edge, from 280 to 720 wide (380 to start with). Only the
  *Message* column gives up the room; the others keep their widths.
- **The checked-out line is tinted light blue** across its whole width, so the commit you are on is
  easy to find.

### The start window's repositories

- **Each profile has its own list of repositories.** Pick the profile in the combobox in the
  Repositories page's header; the choice is remembered. Picking a list never changes the name and
  email git commits with: that is still the Profiles page's *Use*.
- **Drag a repository by its row** — the grip at its left says it can be dragged — to put the list in
  your order. The order is kept for each profile. Opening a repository never moves it. A new one goes
  at the end, and nothing is dropped to make room.
- **A "Default" profile is made when there is none,** so there is always a list. It has no name or
  email.
- **A profile no longer needs a name and email.** One without them changes nothing in git: it is never
  the current profile, never decides a push and never signs git in, and it has no *Use*.
- **Deleting a profile deletes its list,** never the repositories on it.
- **A theme switch** sits left of About, as it does in the repository window.

### Creating, cloning and tagging

- **A new repository's first commit is a `README.md`** holding its name as a title, committed as
  *Initial commit* with nothing else. A `README.md` the folder already had is committed as it is, never
  overwritten. Without a git name and email, the repository is still created and opened, and you are
  told why it has no first commit yet.
- **A clone is suggested where the last one was made,** as long as that folder still exists.
- ***Create a tag* puts the caret in its name box,** so the name can be typed straight away.

### The look

- **The name is Enigma Git Client:** the splash screen, About, the Settings page's About button, the
  launcher entry, and the installer and uninstaller.
- **Narrower navigation rails:** 72 wide in the repository window and 88 in the start window, the
  narrowest that keep their labels on one line.

### Fixes

- **The diff's text can be selected** — it could not be, committed or uncommitted. Drag across the
  lines in either side of the side-by-side view, or in the unified view:
  - Shift+click extends the selection and a click clears it;
  - the list scrolls when the selection is held at its edge;
  - Ctrl+C or the right-click *Copy* copies it, as the code alone, without line numbers or markers.

  With no text selected, *Copy* copies the selected lines. The diff stays read-only.

### Upgrading from 4.x

- **The Changes page is gone.** Select the history's uncommitted line: the panel beside the history
  has what the page had — *Not staged*, *Staged*, the discards, *Stash all* and the commit box. The
  page's stash list is not there: every stash is a line in the graph, with apply, pop and delete. The
  branch and its upstream are in the window's strip.
- **Double-clicking a history line no longer opens its diffs.** Click the line, then a file in its
  panel, or use *Show what it changed* in the line's menu.
- **The repository list starts empty.** Lists belong to profiles now, and the 4.x list is not carried
  over: open, clone or create your repositories once more, and they join the selected profile's list.
  The lists are kept in `repository-lists.json`. `recent-repositories.json` is left where it was,
  unread, so going back to 4.x finds its list again.
- ***Pin to the top* is gone:** drag a repository where you want it instead.
- **Without profiles, 5.0 adds one, "Default",** with no name or email, so your commits, pushes and
  sign-ins stay exactly as they were. 4.x lists it too; its *Use* there changes nothing, and only says
  there is no name to write.
- `settings.json` gains `selectedProfileId`, which 4.x ignores.
- **The launcher's entry is renamed** "Enigma Git Client" the next time you run `install.sh`. Nothing
  is moved: the install paths and the configuration directory keep their names.

### Dependencies

- No package had an update outside the Avalonia set, which is still held back at **12.1.1** with
  Enigma.Avalonia.Desktop 1.1.0 (12.1.3 is out; the set moves as a whole).

### Version

- **5.0.0** is a major release under Semantic Versioning. It removes functionality — the Changes page,
  the double-click that opened a line's diffs, and *Pin to the top* — and does not carry the 4.x
  repository list over, so an upgrade can stop an existing workflow until you move to the panel and
  add your repositories again. As for 2.0.0, 3.0.0 and 4.0.0, that makes it a major release.
  Everything else is new, backward-compatible functionality and fixes.

## 4.1.1 — 2026-09-29

A patch release that fixes a regression in 4.1.0: every dialog whose content is a view showed a line
of text naming the view's type instead of the view. Nothing is added or removed, and nothing that is
stored changes.

### Fixes

- **Dialogs show their content again** (a regression from 4.1.0). The Branches, Tags and Remotes
  dialogs showed only a line such as `Enigma.GitClient.App.Views.Pages.BranchesPageView` and the
  Close button. So did About, *Commit details*, the branch, tag, stash and remote forms, the
  repository browser, the profile dialogs and the Repositories page's form. 4.1.0's wrapping of long
  questions was applied to every dialog's content, views included; it now reaches only the text of a
  plain question. Long questions still wrap, and are never cut.

### Upgrading from 4.1.0

- Nothing changes in the way you work, and nothing is migrated.

### Dependencies

- No package had an update outside the Avalonia set, which is still held back at **12.1.1** with
  Enigma.Avalonia.Desktop 1.1.0 (12.1.3 is out; the set moves as a whole).

### Version

- **4.1.1** is a patch release under Semantic Versioning: a fix, and nothing else.

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
