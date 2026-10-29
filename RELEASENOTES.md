# Release notes

## 6.13.0 — 2026-10-29

A minor release:

- the application **starts straight into its first window**: the splash screen is gone;
- the **application icon is a file** beside the executable, `app.ico`, in the build output and in
  every published folder;
- **publish scripts for both platforms**: `publish.bat` joins `publish.ps1` on Windows, and
  `publish.sh` joins `install.sh` and `uninstall.sh` on Linux.

### No splash screen

- **The first window is the first thing on screen.** It is the start window, or the repository
  window for a repository named on the command line, as before. The splash window and its one-second
  minimum are gone, so a start no longer waits out that second.
- **The version is still in the About dialog**, with the build it was cut from. It is the one place to
  read it.
- **A repository named on the command line** is opened first, and its window then appears on it
  directly.

### The icon beside the executable

- **`app.ico`**, the icon the executable and the windows carry, all ten frames from 16 to 256 pixels,
  is written next to `Enigma.GitClient.Desktop(.exe)`. It lands in the build output
  (`bin/<Configuration>/net10.0/`) and in every published folder. A shortcut, a launcher or an
  installer can point at the file instead of extracting the icon from the executable.
- On Linux, `install.sh` still installs the launcher's icons from `packaging/linux/icons`, in eight
  sizes. Nothing changes there.

### Publish scripts

- **One command per platform, from any directory.** Each script is a Release build, compiled
  ReadyToRun so the application starts faster. It runs on an installed .NET 10 runtime, and the script
  exits with `dotnet`'s exit code.

| Platform | Script | Runs | Result |
|---|---|---|---|
| Windows | `packaging/windows/publish.ps1` or `publish.bat` | `dotnet publish src/Enigma.GitClient.Desktop -c Release -r win-x64 -p:PublishReadyToRun=true` | `src/Enigma.GitClient.Desktop/bin/Release/net10.0/win-x64/publish/` |
| Linux | `packaging/linux/publish.sh` | the same with `-r linux-x64` | `src/Enigma.GitClient.Desktop/bin/Release/net10.0/linux-x64/publish/` |

- **`publish.bat`** runs from a `cmd` prompt with no PowerShell execution policy involved.
- **`install.sh`** remains the way to build and install on Linux in one step, with the runtime bundled
  by default.

### Upgrading from 6.12

- **On Linux, run `packaging/linux/install.sh` again**, as for any upgrade.
- **Nothing you saved changes.** `settings.json` stays at version 7, `repository-lists.json` at
  version 1, and the integrations (`host-accounts.json`) and the tokens (`tokens.json`) keep their
  format. Going back to 6.12 keeps everything.

### Dependencies

- Nothing is outdated: every package is already at its latest version (`dotnet list package
  --outdated`, 2026-10-29). The Avalonia set stays at **12.1.4**, with Enigma.Avalonia.Desktop
  **1.3.0**.

### Version

- **6.13.0** is a minor release under Semantic Versioning. It removes a start-up screen and adds files
  beside the executable and in the repository. It changes no public Core API, keeps every stored
  format and moves no dependency.

## 6.12.0 — 2026-10-28

A minor release:

- a **new application icon**: the same line of commits and branch, in softer colours, and drawn in
  more sizes, so Windows' display scaling and the Linux desktop find a frame made for their size;
- the Avalonia set moves to **12.1.4**, with Enigma.Avalonia.Desktop **1.3.0**.

### A new application icon

- **The same drawing, softer colours.** The line of commits is a lighter blue (`#6A9BF4`, was
  `#3574F0`) and the branch a lighter green (`#63C77D`, was `#3FB950`), on the same dark rounded
  square. The application's own colours — the HEAD badge, the accent — do not change.
- **Wherever the icon shows:** the windows and the taskbar, the splash screen, the About dialog, the
  Windows executable and, on Linux, the launcher.
- **More sizes.** The Windows icon carries ten frames from 16 to 256 pixels, where it carried six: 20,
  24, 40 and 96 are new, so at 125 % and 150 % display scaling Windows shows a frame drawn for that
  size rather than scaling another one.
- **On Linux,** `install.sh` installs the icon in eight sizes, 24 and 96 added, and `uninstall.sh`
  removes all eight.

### Upgrading from 6.11

- **On Linux, run `packaging/linux/install.sh` again** to put the new icon in the launcher, as for any
  upgrade.
- **Nothing you saved changes.** `settings.json` stays at version 7, `repository-lists.json` at
  version 1, and the integrations (`host-accounts.json`) and the tokens (`tokens.json`) keep their
  format. Going back to 6.11 keeps everything.

### Dependencies

- The Avalonia set **12.1.1 → 12.1.4**: `Avalonia`, `Avalonia.Desktop`, `Avalonia.Themes.Fluent`,
  `Avalonia.Fonts.Inter` and, for the tests, `Avalonia.Headless` and `Avalonia.Skia`.
- **Enigma.Avalonia.Desktop 1.2.0 → 1.3.0**, built against that set. It is backward compatible with
  1.2.0 (no API, key or value changes) and brings Enigma.Core 1.0.0 with it.
- Nothing else is outdated.

### Version

- **6.12.0** is a minor release under Semantic Versioning. It changes the icon and moves dependencies
  compatibly; it changes no public Core API, keeps every stored format and removes nothing.

## 6.11.0 — 2026-10-27

A minor release:

- a local branch's **badge in the history renames the branch**: `Rename "<name>"…` in its menu asks
  for the new name in the dialog the branches dialog already uses, and the history is drawn again
  under it;
- that dialog now opens with the caret in the name box and the current name selected, so the new
  name can be typed straight away, wherever it is opened from.

### Rename a branch from its badge

- **In the badge's menu.** `Rename "<name>"…` sits right before `Delete "<name>"…`, with a pencil.
  It is offered on every local branch, the checked-out one included, and on a badge that draws a
  branch with its upstream. A remote branch's own badge does not offer it: git renames a
  remote-tracking branch only by pushing it again under another name.
- **One dialog, wherever the rename starts.** *Rename branch* says `Renaming <name>` and asks for the
  *New name*. *Rename* stays disabled, with the reason shown, while the name is not a valid branch
  name, is the one the branch already has, or belongs to another branch (*A branch called "x"
  already exists.*). *Cancel* changes nothing.
- **What a rename changes.** Only the branch's name: its commits, its upstream and the remote branch
  stay as they were, and a renamed checked-out branch stays checked out. The history reloads and the
  badge carries the new name. When git refuses, the bar says `Could not rename "<name>"` with git's
  reason.
- **The merge source.** A branch set as the merge source is no longer one once renamed; set it again
  under its new name.
- **Finishing a squash** (6.10) can now be done from the graph alone: delete the original branch from
  its badge, then rename the squashed one from its own.

### Upgrading from 6.10

- **Nothing you saved changes.** `settings.json` stays at version 7, `repository-lists.json` at
  version 1, and the integrations (`host-accounts.json`) and the tokens (`tokens.json`) keep their
  format. Going back to 6.10 keeps everything.

### Dependencies

- No package moved.
- The Avalonia set is still held back at **12.1.1** with Enigma.Avalonia.Desktop 1.2.0 (12.1.4 is
  out; the set moves as a whole).

### Version

- **6.11.0** is a minor release under Semantic Versioning. It adds backward-compatible behaviour (the
  badge's rename) and changes no public Core API. It keeps every stored format and removes nothing.

## 6.10.0 — 2026-10-27

A minor release:

- **squash a range of commits onto a new branch** from the History page: mark a base, then squash
  everything since it, up to any line above it, into one commit on a branch you name. Nothing that
  exists is rewritten or moved, so the original branch keeps every commit until you delete it;
- on the start window's Repositories page, **one button gives the picked profile's name and email to
  every repository on its list**, so each of them keeps committing as that profile whatever git's
  global identity becomes.

### Squash a range onto a new branch

- **Two steps, as a merge from the graph.** *Set as squash base* in a commit's line menu flags that
  line (a flag before its subject, the tooltip *Squash base*). A `Squash base: <short sha>` chip
  appears beside the merge source's, and its × clears the base.
- **Then squash from a line above it.** On any commit line above the base on its line of first
  parents, the menu offers *Squash N commits since <short sha> into a new branch…*. N counts the
  commits that lead from the base to that line, the merges and the line itself included; commits a
  merge brought in from outside the range are counted apart, in the dialog. The item is greyed out,
  as *Squash since <short sha> into a new branch…*, on a line the base does not lead to through first
  parents: a base that came in through a merge is no base for that line. The group sits right after
  the reset and revert items. It is greyed out on the uncommitted line and absent from stash lines.
- **The dialog says what will happen before anything does.**
  - *Base* and *Tip*, with the branches on the tip, and *Replaces*: `N commits (M merges)`, plus what
    the merges brought in from outside the range.
  - *New branch*: the tip's branch with `-squashed` added, or `squash/<short sha>` when no local
    branch ends at the tip. `-2`, `-3`… are added while the name is taken.
  - *Switch to the new branch*: ticked when HEAD is at the tip, where switching changes no file.
  - *Message — the first line is the title*: an empty title line, the caret on it, then the subjects
    of the range's commits as a list, oldest first, the merges left out. *Squash* stays disabled, with
    the reason shown, until the title is written and the name is valid and free.
  - Warnings when they apply: a merge that brings in a branch from outside the range (the new branch
    keeps its files but loses the link to it, so merging that branch again later may conflict
    again); a range that changes no file in total (the commit is empty); and no local branch at the
    tip (nothing above the tip is on the new branch).
- **What a squash makes.** One new commit, whose parent is the base and whose files are exactly the
  tip's, on a new branch. It is made with your name and email, now, and commit hooks do not run on
  it. No rebase and no cherry-pick: the original branch, its commits, its tags and its upstream stay
  exactly as they were. The history reloads and selects the squash commit, and the bar says which
  commits the squash commit replaces and that the original branch "is untouched and still holds
  them; deleting it later warns about unmerged commits — that is expected." To finish, delete the
  original branch and rename the new one, as any branch.
- **Nothing half-done.** The squash is refused, and nothing is written, during a merge, a revert, a
  cherry-pick or another operation in progress, or while conflicts wait to be resolved. When
  uncommitted changes are in the way of switching to the new branch, no branch is created and the bar
  names the files.
- **The base lasts until it is used.** A refresh, a fetch or a branch switch keeps it. A squash, the
  chip's ×, *Clear the squash base* in the menu, opening another repository, or a reload that no
  longer lists the commit clears it.

### The profile's identity in every repository

- **A button after the profile picker** on the start window's Repositories page (the tooltip reads
  *Use this profile's name and email in every repository on its list*).
- **One question first.** *Use <label> in every repository* asks to make the profile's name and email
  those of the repositories on its list. They are written to each repository's own configuration, so
  its commits are made as them whatever the global identity becomes later. A repository that has a
  name and email of its own gets these instead. *Use it* goes ahead; *Cancel*, the default, changes
  nothing.
- **Every repository on the list,** those the filter box hides included.
- **Only what needs it changes.**
  - A repository that already commits as the profile is not rewritten.
  - A listed folder that is gone or is no longer a repository is skipped. A folder inside another
    repository is skipped too, so the repository around it is never written.
  - A repository that cannot be changed does not stop the others.
  - git's global identity is left alone: picking the profile already set it.
- **One bar says how it went.**
  - *Using <label> in every repository*: how many repositories now commit as the profile.
  - *Nothing to change*: every repository already did.
  - *Some repositories were left as they were*: names those not found and those that could not be
    changed, with git's reason.
- **Disabled when there is nothing to do:** for a profile without a name and email, for an empty
  list, and while the page is busy.
- Names and emails are never logged.

### Upgrading from 6.9

- **Nothing you saved changes.** `settings.json` stays at version 7, `repository-lists.json` at
  version 1, and the integrations (`host-accounts.json`) and the tokens (`tokens.json`) keep their
  format. Going back to 6.9 keeps everything.

### Dependencies

- Microsoft.Extensions.Hosting, Microsoft.Extensions.Http and
  System.Security.Cryptography.ProtectedData 10.0.13 → 10.0.14.
- The Avalonia set is still held back at **12.1.1** with Enigma.Avalonia.Desktop 1.2.0 (12.1.4 is
  out; the set moves as a whole).

### Version

- **6.10.0** is a minor release under Semantic Versioning. It adds backward-compatible behaviour (the
  squash, the identity button) and public Core services (`ISquashService` with `SquashRange`, and
  `IBulkLocalIdentityService`). It keeps every stored format and removes nothing.

## 6.9.0 — 2026-10-23

A minor release:

- the **repository strip** draws the open repository larger, with the history's own badge for the
  checked-out branch;
- the repository window's commands are on **one command bar across the top of the History page**:
  twelve named commands in four groups at the bar's centre, the search on its right, and icons only
  when the window is too narrow for the names;
- **Escape clears the History search** while the search box has the focus;
- on the **Profiles page** each profile is a card: the active one is outlined and says *Active*, the
  others offer **Make active**, and the accounts and the buttons on a profile or an account are
  larger.

### The repository strip

- **The open repository reads as the window's title.** Its name is larger (18 px, semibold), and so
  are its icon and the tree a linked worktree adds after it.
- **The checked-out branch** is shown by the badge the history gives it, with the same blue, the same
  check, the same size and the same tooltip. It was a grey pill.
- **A detached HEAD** shows no branch badge. Its warning pill says where HEAD is (`detached at
  abcdef1`), where the strip showed two pills, the commit and `detached HEAD`.
- **The strip has no buttons left.** They are on the History command bar.

### The History command bar

- **One bar for the repository's commands.** The strip's buttons (fetch, pull, push, folder,
  terminal, refresh, close) and the History page's toolbar (stash, branches, worktrees, tags,
  remotes, the search) are now one command bar across the top of the History page. It spans the
  whole page, above the details panel and above a diff, and neither narrows nor covers it.
- **Named commands, in four groups.** The groups are Fetch · Pull · Push · Stash, then Folder ·
  Terminal, then Branches · Worktrees · Tags · Remotes, then Refresh · Close. Each command is an
  icon over its name, the row sits at the bar's centre, and a line with room on either side sets the
  groups apart. Every command keeps its tooltip and its accessible name.
- **The pull and push counts** are a small pill on the icon's corner, so a count appearing never
  moves the row.
- **A narrow window gets a compact bar.** The names need a window about 1350 px wide or more. Below
  that (1280 px, or 1920 px at 150 % scaling), the commands keep their icons and tooltips and drop
  their names, and nothing leaves the bar. When the window is widened again, the names come back.
- **The search** is on the bar's right, 200 px wide at most, and is the first thing to give way on a
  narrow window. Its placeholder is *Search commits*, and its tooltip says what it searches: the
  message, the SHA and the author.
- **The theme switch and About are no longer on the bar.** In a repository's window, the theme is
  the *Theme* setting on the Settings page, and *About Enigma Git Client* is under *About* on the
  same page. The start window keeps its own theme switch and About.
- **The commands are on the History page only.** The Profiles, Settings and Resolve conflicts pages
  no longer carry fetch, pull, push, folder, terminal, refresh and close. History is one click away
  on the rail, and the banner of a merge in progress keeps its own actions.

### The History search

- **Escape clears the search** while the search box has the focus, as the × button does. The text,
  the highlights and the counter go, and the focus stays in the box for the next search. The ×
  button's tooltip names the key: *Clear the search (Escape)*.
- **With a diff open,** the first Escape clears the search and the second leaves the diff. In an
  empty box, or anywhere else on the page, Escape does what it did before.

### Profiles as cards

- **One card per profile, in larger type.** The label is 15 px semibold, the name and email 13 px.
  The profile's mark is a monogram of its label in a colour of its own (*W* for Work), where every
  profile had the same grey icon. The profile's accounts, its *Local only* line and *Connect an
  account* are inside its card.
- **The active profile is unmistakable.** Its card is outlined in the accent colour and says
  **Active**, with a check; the tooltip says that git commits as this profile. It replaces the
  *Current* pill.
- **Make active** switches to another profile. It is a full-size button with an icon, in place of
  the small *Use*, on every other profile that has a name and an email. A profile without them has
  no button, because switching to it would unset git's identity. The note after a switch still
  reads *Using <label>*.
- **A long label** now ends in an ellipsis instead of running under the buttons.
- **Accounts are easier to read and to hit.**
  - An account's logo is 24 px, and the line under its name (`github.com · octocat`) is 13 px in a
    lighter grey.
  - *Browse repositories*, *Replace token* and *Move to…* use the page's normal type size.
  - Edit and delete on a profile, and disconnect on an account, have larger icons in 36 px targets.
  - The *Earlier integrations* lines look the same.
- **The words follow.** The repository section's button is *Copy from active profile*, and the empty
  page ends "… switch between them with Make active."

### Upgrading from 6.8

- **Nothing you saved changes.** `settings.json` stays at version 7, `repository-lists.json` at
  version 1, and the integrations (`host-accounts.json`) and the tokens (`tokens.json`) keep their
  format. Going back to 6.8 keeps everything.
- **Looking for the theme switch or About in a repository's window?** Both are on the Settings page
  (see *The History command bar*).

### Dependencies

- No package had an update outside the Avalonia set. The set is still held back at **12.1.1** with
  Enigma.Avalonia.Desktop 1.2.0 (12.1.4 is out; the set moves as a whole).

### Version

- **6.9.0** is a minor release under Semantic Versioning. It adds backward-compatible behaviour (the
  command bar and its compact mode, Escape in the search, *Make active*) and one public Core helper
  (`GitSignature.InitialsOf`). It keeps every stored format and removes nothing: the theme switch and
  About moved to the Settings page.

## 6.8.0 — 2026-10-23

A minor release:

- an error nothing expected **no longer closes the application**: a dialog says what went wrong and
  that the application is still running, and you carry on;
- stored tokens that **can no longer be read** (a Windows password reset does this) can be reset from
  the application: connecting offers it, and the Profiles page says which tokens cannot be read, with
  *Reset stored tokens* and *Replace token*;
- when git falls back to **its own sign-in** because a token cannot be read, the repository window
  says so;
- connecting an account that is already listed **replaces its token** instead of adding a second line.

### Errors no longer close the application

- **The application keeps running.** An error nothing in the application expected, in a command,
  while a window opens or in work running in the background, used to end the process without a word.
  It is now logged in full (on Windows, in the Event Viewer's *Application* log too) and shown in a
  *Something went wrong* dialog that says the application is still running: carry on, or close it and
  start it again.
- **What the dialog shows:** the error's message and a folded *Details* section with the full text,
  selectable, and *Copy details* to put it on the clipboard for a report. A token in a URL or an
  `Authorization` header is replaced with `***` in the details.
- **One dialog at a time.** Errors that arrive together share one dialog ("2 more errors occurred"),
  and the same error is not shown again within a minute; it is still logged. The dialog never opens
  over a question you are answering: it waits until that question is closed.
- **A cancellation is not an error.** Cancelling an operation, or closing the application while one
  runs, shows nothing.
- **At startup,** a repository given on the command line that fails to open in an unexpected way now
  brings up the start window with the error, where the splash used to close and the application end.
- A few failures .NET lets no application survive still end it. They are logged in full.

### Tokens that can no longer be read

The tokens are encrypted with a key Windows keeps for your account. After a Windows password reset,
Windows can no longer open that key, and the tokens cannot be recovered. In 6.7, connecting an account
then closed the application. Now:

- **Connecting offers the reset.** *Reset stored tokens* asks first, in red: every stored token is
  deleted, for every integration of every profile, and each connected account then needs its token
  again. Nothing on the host is touched and the integrations stay listed. Confirmed, the token you just
  typed is stored; cancelled, nothing is stored and the error says why.
- **The Profiles page says so before you try.** While the stored tokens cannot be read, a warning band
  under the page's header says why and offers *Reset stored tokens*. An integration whose token is
  missing or cannot be read says so on its line, in the warning colour, with a **Replace token**
  button.
- **Replace token** opens the connect dialog filled in with the account's host, instance and name.
  Once the host accepts the new token, the same integration keeps it.
- **One damaged token** is not a reason to delete the others: its line offers *Replace token* and
  *Disconnect*, and no reset is offered.
- **Browse repositories** no longer fails on a token that cannot be read. When no stored token can be
  read, it closes and offers the reset; when only its own cannot, it says so and points at the
  integration's line.
- **The error dialog** offers *Reset stored tokens* when the error it shows is this one.
- **Never without asking.** A key file deleted by hand while tokens remain is now reported as
  unreadable. It is no longer replaced by a new key, which made every remaining token unreadable for
  good.

### Git's own sign-in

- **The repository window says why.** When a fetch, pull or push cannot read an integration's token,
  git still runs with its own sign-in (Git Credential Manager's prompt on Windows), as before. A
  warning band across the top of the window now says *The stored token for <host> could not be read,
  so git is using its own sign-in*, with *Reset stored tokens* when no stored token can be read,
  *Open Profiles* and a dismiss button.
- **Once, not on every fetch.** The band is shown once per integration per session, and once dismissed
  it stays away. It goes by itself when the tokens are reset, or when that integration's token is
  written again.
- **The other integrations keep their logins.** One unreadable token used to drop the logins of every
  integration for the repository; now only its own host is left to git.

### Connecting an account again

- Connecting an account that is already listed (same host, instance, login and profile) gives that
  integration the new token, "Token replaced for GitHub", and keeps its line. It used to add a second
  line, leaving the old one beside it with a token that no longer worked. Another profile or another
  login is still a new integration.

### Upgrading from 6.7

- **Nothing you saved changes.** `settings.json` stays at version 7, `repository-lists.json` at
  version 1, and the integrations (`host-accounts.json`) and the tokens (`tokens.json`) keep their
  format. Going back to 6.7 keeps everything, except tokens you reset, which are gone in either
  version.

### Dependencies

- No package had an update outside the Avalonia set. The set is still held back at **12.1.1** with
  Enigma.Avalonia.Desktop 1.2.0 (12.1.4 is out; the set moves as a whole).

### Version

- **6.8.0** is a minor release under Semantic Versioning. It adds backward-compatible behaviour (the
  error dialog, the reset of the stored tokens, the Profiles page's band and *Replace token*, the
  sign-in notice), keeps every stored format, and removes nothing.

## 6.7.0 — 2026-10-22

A minor release:

- when the history shows *How long ago*, each line's age is **measured again on every automatic
  refresh**: a line that said "just now" says "2 minutes ago" two minutes later, without a click;
- on Windows, **`packaging/windows/publish.ps1`** runs the Windows publish from any directory.

### The history's ages

- **The ages move by themselves.** With *Dates* set to *How long ago*, every line's age is measured
  again at each automatic refresh, at the interval set by *Fetch and refresh automatically* (60
  seconds by default), and at each refresh, fetch, pull or push you ask for.
- **Nothing else moves.** The lines are not redrawn: the selection and the scroll stay where they
  are, and only a line whose age changed is updated.
- **With *The date and time*,** the age in each line's tooltip moves the same way.
- **With the automatic refresh off,** the ages move only when the history is drawn again, as before.
  The uncommitted line has no age.

### Publishing on Windows

- **`packaging/windows/publish.ps1`** runs the publish for `win-x64`, compiled ReadyToRun, from any
  directory. The result, in `src\Enigma.GitClient.Desktop\bin\Release\net10.0\win-x64\publish\`,
  needs the .NET 10 Desktop Runtime on the machine that runs it.
- Under Windows PowerShell's default execution policy, which refuses local scripts, run it with
  `powershell -ExecutionPolicy Bypass -File packaging\windows\publish.ps1`. It ends with a non-zero
  exit code when the publish fails. `Get-Help packaging\windows\publish.ps1` describes it.
- Windows still has no installer: run `Enigma.GitClient.Desktop.exe` from the result.
  `docs/RELEASE.md` names the script under *Publish the application*.

### Upgrading from 6.6

- **Nothing you saved changes.** `settings.json` stays at version 7 and `repository-lists.json` at
  version 1. Going back to 6.6 keeps everything.

### Dependencies

- No package had an update outside the Avalonia set. The set is still held back at **12.1.1** with
  Enigma.Avalonia.Desktop 1.2.0 (12.1.4 is out; the set moves as a whole).

### Version

- **6.7.0** is a minor release under Semantic Versioning. It adds backward-compatible behaviour (the
  history's ages follow the automatic refresh) and a new script, keeps every stored format, and
  removes nothing.

## 6.6.0 — 2026-10-21

A minor release with one fix: selecting or deselecting a file no longer freezes the application.

### Fixes

- **The diff no longer freezes the application.**
  - Selecting a file, in a commit's files or in the working tree's *Not staged* and *Staged* lists,
    sometimes froze the whole window for good, and it had to be killed. It could happen when the diff
    went from a file with syntax colours to one without them (a `.txt`, `.lock`, `.slnx`, `.config`
    or `.resx`, an image, a file with no extension), or when a file with them was deselected.
  - The syntax highlighting library could lock up against its own background colouring when it was
    told the text had no grammar any more. The diff no longer tells it that: it starts the
    highlighting afresh, with no grammar, which cannot lock up.
  - Nothing else changes: colours, the Light and Dark themes, and plain text look as before.

### Upgrading from 6.5

- **Nothing you saved changes.** `settings.json` stays at version 7 and `repository-lists.json` at
  version 1. Going back to 6.5 keeps everything.

### Dependencies

- No package had an update outside the Avalonia set. The set is still held back at **12.1.1** with
  Enigma.Avalonia.Desktop 1.2.0 (12.1.4 is out; the set moves as a whole).

### Version

- **6.6.0** is a minor release under Semantic Versioning. It carries a fix only, which a patch
  release (6.5.1) could also have carried. Nothing is removed, and the stored settings do not
  change.

## 6.5.0 — 2026-10-21

A minor release:

- a repository you open, create or clone for the first time joins the **top** of the list, not its
  bottom;
- the info bar sits in the **bottom-left corner**, a quarter of the window wide, instead of a band
  across the top;
- the branches dialog opens on **every branch**, with *Expand all* and *Collapse all* in its header;
- the words on the right of a branch's or a remote's line are **lighter and a little larger**, so
  they can be read in the dark theme.

### The repositories list

- **A new repository joins at the top.** *Open*, *Create*, *Clone* and
  `Enigma.GitClient.Desktop <path>` put a repository the profile's list does not have yet first in
  it, where you look after adding it.
- **The order is still yours.** A repository already listed stays where you dragged it, whichever way
  you open it, and takes its new name if it has one.

### The info bar

- **Bottom-left, past the rail.** It no longer covers the repository strip or the page headers, and
  it starts right of the navigation rail, so *Profiles* and *Settings* stay in sight.
- **A quarter of the window wide**, at least 360 px, never wider than the room right of the rail. It
  follows the window when you resize it.
- Unchanged: successes and information still close themselves after 2.5 s, and warnings and errors
  stay until you close them. The bar still draws over an open dialog.

### Branches

- **Every node starts open:** *Local*, each remote and every folder, so the dialog opens on every
  branch. A folder you close stays closed when the dialog refreshes.
- **Expand all** and **Collapse all** sit in the header, before the sort controls, with the same
  icons as the changed files' tree. They reach every node, the top-level ones included. What they do
  is remembered like a click, and the selected branches stay selected.

### Branch and remote lines

- **Lighter:** a branch's badges (*checked out*, *worktree*, *hidden*, ahead and behind, *local
  only*), the branch counts and a remote's URL move from the faintest grey to the secondary one, as
  do the badges' icons. The same colour in both themes. In the dark theme it was close to the
  background; in the light theme it is a touch darker than before.
- **Larger:** 12 px instead of 11. A file's line counts in the changed files keep their size and
  colour.

### Upgrading from 6.4

- **Nothing you saved changes.** `settings.json` stays at version 7 and `repository-lists.json` at
  version 1. Your lists keep their order; only the next repository you add lands on top. Going back
  to 6.4 keeps everything.
- **The branches dialog opens fully expanded.** Use *Collapse all* to fold it.

### Dependencies

- No package had an update outside the Avalonia set. The set is still held back at **12.1.1** with
  Enigma.Avalonia.Desktop 1.2.0 (12.1.4 is out; the set moves as a whole).

### Version

- **6.5.0** is a minor release under Semantic Versioning. It adds backward-compatible behaviour (where
  a new repository lands, where the info bar sits, the branch tree's buttons and default, the
  legibility of the ref lines), keeps every stored format, and removes nothing.

## 6.4.0 — 2026-10-16

A minor release:

- a worktree can now be removed from the application — its branch with it if you choose — and a
  *Worktrees* dialog lists them all;
- a branch checked out in another worktree is marked with a tree, and deleting or checking it out is
  refused up front, naming the folder, instead of failing in git;
- the windows open on the screen you are working on: the repository window and the start window hand
  over on the same screen, a fresh start opens where the application was last used, and another
  instance opens on the screen of the window that started it.

### Worktrees

- **Remove worktree…** is in the menu of a branch checked out in another worktree — its line in the
  branches dialog and its badge in the history — and in the new *Worktrees* dialog. Before anything
  goes, it says what the removal costs:
  - the folder that is deleted (or, for a folder already gone, that only git's record of it goes);
  - the uncommitted changes in it, in the warning colour, and *Remove anyway* instead of *Remove* when
    there are any;
  - *Close any window working in it first*;
  - *Also delete the branch "x"*, **ticked when the branch is merged and unticked when it is not**,
    with the commits it would lose listed under it. A detached worktree has no branch to offer.

  The main working tree, the one this window has open and a locked worktree are never removed: the
  first two are not offered, and a locked one is refused, naming `git worktree unlock`.
- **Open it elsewhere:** the same menus offer *Open worktree in a new window*, *Open worktree folder*
  and *Open a terminal in the worktree*, greyed when the worktree's folder is missing.
- **The Worktrees dialog**, a tree in the history's toolbar right after *Branches*, lists every working
  tree, the main one first, with its folder, what it has checked out, and badges for *main*, *this
  window*, *locked* (with its reason) and *folder missing*. Its line menu offers the same four actions,
  and a double-click opens one in a new window. What does not apply is greyed, and its tooltip says
  why. With no other worktree, it says how to make one.
- **The tree mark:** a branch checked out in another working tree carries a tree in the branches
  dialog (a *worktree* badge) and on its pill in the history, its tooltip naming the folder. The branch
  this window has checked out keeps its own badge. A window opened on a worktree shows a tree after the
  repository's name, its tooltip naming the main working tree.
- **Refused up front:**
  - deleting such a branch says *"x" is checked out in the worktree at <folder>. Remove the worktree
    first.*, and a batch skips it with that reason while deleting the rest;
  - checking it out, from the branches dialog, a badge, a drop-merge or a revision, offers to open
    that worktree in a new window instead.

  git's *used by worktree* error is no longer what you meet.

### Windows and screens

- **The hand-over keeps the screen.** Opening a repository maximises its window on the screen the
  start window was on. *Close repository* brings the start window back, centred on the screen the
  repository window was on. A window handing over while minimised uses the screen it was last seen on.
  Each window is sized for the scaling of the screen it opens on, so a 100 % and a 150 % screen side by
  side both centre it right.
- **A fresh start opens where you left off.** The splash and the first window open on the screen the
  application was last used on, remembered in the settings as windows open, move and close. If that
  screen is gone, they open on the primary one.
- **Another instance opens on its caller's screen.** *Open in a new window* on a repository tile, or on
  a worktree, starts the new instance on the screen of the window you clicked in. It is passed on the
  command line as `--screen=x,y,width,height`, ahead of the path. A command line without it opens as
  before.
- **Unchanged:** the start window is still centred and the repository window still maximised. The
  repository window's ✕ still quits.
- **On Linux** the window manager has the last word on X11; native Wayland compositors ignore where an
  application asks to put its windows.

### Upgrading from 6.3

- **Nothing you saved changes.** `settings.json` stays at version 7. It gains one remembered value,
  `lastScreen`, that an older version ignores, so going back to 6.3 keeps everything.
- **New in the menus:** a branch checked out in another worktree has four more items, and the history's
  toolbar has one more button, *Worktrees*.

### Dependencies

- No package had an update outside the Avalonia set. The set is still held back at **12.1.1** with
  Enigma.Avalonia.Desktop 1.2.0 (12.1.4 is out; the set moves as a whole).

### Version

- **6.4.0** is a minor release under Semantic Versioning. It adds backward-compatible behaviour (the
  worktrees' removal, their dialog and their marks; the windows' screens), keeps every stored format,
  and removes nothing.

## 6.3.0 — 2026-10-15

A minor release:

- a worktree — a second working directory of the same repository, on a new branch — can be created
  from a commit's line, a branch's badge or a branch's line in the branches dialog, and opens in a
  window of its own;
- creating a repository offers your identity to it, as a clone already did;
- the repository window's toolbar no longer has the *Open another window* button.

### Worktrees

- **Create worktree…** is offered in three places:
  - a commit's line in the history: *Create worktree here…*, after *Create branch here…*. The new
    branch starts at that commit. It is greyed on the uncommitted line and absent on a stash's;
  - a branch's badge in the history: *Create worktree from "main"…*, after *Check out "main"*, for
    local and remote branches (a remote branch is tracked, as git sets it up);
  - a branch's line in the branches dialog: *Create worktree…*, after *Check out*, for one branch at
    a time.
- **The dialog asks for:**
  - the branch to create, refused when its name is invalid or a local branch already has it;
  - the working directory, proposed **next to the repository, never inside it**:
    `<repository>.worktree.<branch>`, with any `/`, `\` or other character a folder name cannot hold
    turned into `-` (`feature/login` → `project.worktree.feature-login`). It follows the branch as
    you type until you edit it, and *Browse* picks where it goes. A folder inside the repository, a
    file, or a directory that is not empty is refused;
  - whether to **open it in a new window** once it is created, checked by default.
- **Afterwards:** the new branch shows in the history and the branches dialog, and the info bar says
  where it is checked out. A worktree git refuses leaves no branch behind.

### The new repository's identity

- **Create a repository asks what a clone asks:** *Use your identity in this repository* — make your
  global name and email the new repository's own, so its commits are made as them whatever the
  global identity becomes when you pick another profile.
  - *Use it* writes them into the repository's own configuration, before its first commit, so the
    README commit is already made as them. *Not now*, the default, writes nothing.
  - Nothing is asked when git has no complete global name and email.
  - A write that fails says *The repository has no identity of its own*; the repository is still
    created, committed and opened.

### The toolbar

- **The *Open another window, for another repository* button is gone** from the repository window's
  toolbar. To work on another repository beside this one, use *Open in a new window, beside this one*
  on a repository listed in the start window.

### Upgrading from 6.2

- **Nothing you saved changes.** `settings.json` stays at version 7, and going back to 6.2 keeps
  everything.
- **One toolbar button less:** see *The toolbar*.

### Dependencies

- No package had an update outside the Avalonia set. The set is still held back at **12.1.1** with
  Enigma.Avalonia.Desktop 1.2.0 (12.1.4 is out; the set moves as a whole).

### Version

- **6.3.0** is a minor release under Semantic Versioning. It adds backward-compatible behaviour (the
  worktrees, the identity offered to a new repository), and leaves the stored settings as they were.
  The toolbar button it removes was a shortcut: what it did is still on the start window.

## 6.2.0 — 2026-10-14

A minor release with one change: the application icon joins its green commit to the line of commits
the way the History graph draws a branch, with one rounded elbow.

### The icon

- **One rounded elbow.** The line leaves the bottom blue commit horizontally, turns a quarter
  circle, and rises into the green commit from below. It used to hang the green commit off the
  middle blue commit on a short horizontal stub, then drop back to the bottom commit on a diagonal.
- **Everything else is kept:** the dark tile and its rounded corners, the blue line of commits and
  its three dots (the top one a ring), the green commit, and both colours. The blue dots are now
  evenly spaced.
- **The small sizes are drawn for their size,** as before: at 16 and 32 pixels the ring is filled
  and the lines are heavier, so the elbow stays visible.
- **Wherever the icon shows:** the window and the taskbar, the splash screen, the About dialog, and
  the Linux application launcher.
- **The icon has a source:** two SVG drawings in `packaging/icon/`, one for 48 pixels and up and one
  for 16 and 32, each saying how its frames are rendered. They reproduce every frame the
  application and the installer carry.

### Upgrading from 6.1

- **Nothing you saved changes.** `settings.json` stays at version 7, and going back to 6.1 keeps
  everything.
- **On Linux,** running the installer again puts the new icon in place of the old one and refreshes
  the icon cache. A launcher that keeps showing the old icon shows the new one after logging out and
  in again.

### Dependencies

- No package had an update outside the Avalonia set. The set is still held back at **12.1.1** with
  Enigma.Avalonia.Desktop 1.2.0 (12.1.4 is out; the set moves as a whole).

### Version

- **6.2.0** is a minor release under Semantic Versioning. It changes how the application looks,
  backward-compatibly: nothing is removed, and the stored settings do not change.

## 6.1.0 — 2026-10-14

A minor release:

- the start window's list of repositories can be filtered by name or path, and a button at the
  box's right edge clears the filter;
- reordering that list carries the whole tile under the pointer, and a dashed tile shows where it
  will land;
- a push the remote refuses because the branch no longer matches it offers a force push, behind a
  red warning, that can replace only what the warning named.

### The start window's list

- **A filter box above the tiles:** *Filter by name or path*. A repository stays when its name or
  its folder's path contains the text, ignoring case and the spaces around it.
  - The box is there while the profile lists any repository. Switching profile keeps the text and
    filters the new list.
  - **The × at the box's right edge clears it**, whenever the box holds anything, and the caret
    stays in the box. Escape does the same.
  - A filter that keeps nothing says *No repository matches this filter*, not *No repositories
    yet*.
- **No reordering while the list is filtered.** A tile's place among those shown is not its place in
  your order, so the grip is hidden and a drag does not start. Clear the filter to reorder.
- **A drag shows what moves.**
  - A copy of the whole tile follows the pointer up and down, and the tile itself fades in its
    place.
  - Where it would land, a small dashed tile opens a gap, and the rows below make room for it.
  - Dropping, Escape or losing the pointer leaves nothing behind.

### Pushing

- **A refused push offers a force push.** The remote refuses a push when the branch no longer
  matches it: the branch was rewritten here, or someone else pushed to it. The toolbar's *Push* and
  a branch badge's *Push "x"* then ask:
  - *Force push "main"?*, with how many commits `origin/main` has that `main` does not, and that
    they are removed from the remote for everyone who uses it;
  - the button that goes ahead is red, and Cancel is the default. To keep those commits, cancel and
    pull first.
- **It replaces exactly what it named.** The force push is a `--force-with-lease` pinned to the
  remote commit the warning described, never a bare `--force`. If the remote moved while the
  question was open, the push is refused rather than overwriting it.
- **No force over what was never fetched.** When the remote's new commits are not in this
  repository, nothing is offered. The message says to fetch, look at what arrived, and push again.

### Upgrading from 6.0

- **Nothing you saved changes.** `settings.json` stays at version 7, and going back to 6.0 keeps
  everything.

### Dependencies

- Microsoft.Extensions.Hosting, Microsoft.Extensions.Http and
  System.Security.Cryptography.ProtectedData 10.0.12 → 10.0.13.
- xunit.v3 4.0.1 → 4.0.2 (the test projects only).
- The Avalonia set is still held back at **12.1.1** with Enigma.Avalonia.Desktop 1.2.0 (12.1.4 is
  out; the set moves as a whole).

### Version

- **6.1.0** is a minor release under Semantic Versioning. It adds backward-compatible behaviour (the
  filter and its clear button, the drag's tile, the force-push offer), removes nothing, and leaves
  the stored settings as they were.

## 6.0.0 — 2026-10-08

A major release. What changes on disk — a file saved in an editor, a commit, checkout or stash made
in a terminal — shows in the app within a second, because the repository's files are watched. The
Branches, Tags and Remotes pages are drawn like the changed files beside the history: one line each,
with the rest in its tooltip, and the branches as a tree. Several branches, tags or remotes can be
selected and deleted after one question. The window's title says **Enigma Git Client**. The three
pages' buttons on every row moved into each line's menu, and the automatic refresh now runs every 60
seconds: that is why this is 6.0 — read *Upgrading from 5.9* below.

### The repository is watched

- **What changes outside the app shows within a second.** A file saved, added or deleted in an
  editor, or a commit, checkout, stash or merge made in a terminal, shows about 0.3 s after the last
  change, without waiting for the automatic refresh.
- **A burst is one refresh.** A build writing thousands of files, or a checkout changing ten
  thousand, makes one refresh when it stops, and one every two seconds at most while it goes on. A
  changed file reads the working tree once; a moved branch or HEAD reads the references too.
- **The app's own operations are not refreshed twice:** what it writes while it commits, checks out
  or fetches is not taken for a change from outside.
- **What git does for itself is left alone:** its lock files, `FETCH_HEAD`, the object store and the
  logs. A linked worktree is watched where its references live.
- **Turn it off** in Settings, *Git*, *Watch the repository for changes*, for a repository on a
  network share or a WSL `/mnt` drive, which report no changes, or for a tree too large for the
  system's file-watch limits. The change applies at once.
- **When the system refuses** — on Linux, its inotify limits reached — the app says so once in its
  log and carries on with the automatic refresh.

### The automatic refresh

- **Every 60 seconds by default** (it was 15). It still fetches and refreshes; while the repository
  is watched, what changes on this machine already shows, so the interval mostly sets how often the
  remote is fetched. An interval you chose is kept: see *Upgrading from 5.9*.

### Branches, tags and remotes

- **Drawn like the changed files:** one 22-pixel line each, a dim icon where a file has its status
  chip, the name, and the rest in the line's tooltip.
  - **A branch's tooltip:** its full name, what it tracks (or *No upstream*), and its last commit's
    subject, author and date.
  - **A tag's:** its kind, its message, who tagged it, and the short hash with the commit's date.
  - **A remote's:** the host, and where pushes go when that is elsewhere. The fetch URL is on the
    line, faint, on the right.
- **The branches are a tree:** *Local* first, then each remote, alphabetically. Every `/` in a name
  is a folder, and a chain of folders that each hold one folder is a single line
  (`release/2026/10`). Folders come first, by name; the branches follow in the page's order.
  - A click on a folder's line opens or closes it, and never selects it; the arrow keys do not
    select it either.
  - The top-level nodes start open and the folders closed. What you open or close stays so across
    refreshes, until you open another repository.
  - The filter opens the folders above every branch it keeps.
  - Dragging one branch onto another still offers the merge.
- **A branch's badges**, each only when it applies, each with a tooltip: *checked out*; *hidden*
  from the history, with an eye; ↑ and ↓ with the commits to push and to pull; *local only*, for a
  branch on no remote.

### Selecting and deleting several

- **Select several lines:** Ctrl+click adds or takes away one, Shift+click a range, Ctrl+A every
  line the page shows — on the branches, every branch inside an open node. A folder is never
  selected.
- **Delete them at once:** the header's *Delete (n)* button (*Remove (n)* on the remotes page), the
  Delete key, or the menu of any selected line, which then offers only the deletes. On the tags page
  the button and the key delete the tags here; *Delete from the remote…* is in the menu.
- **One question names everything that goes:**
  - each unmerged branch, with the commits it would lose — ten, then "…and n more";
  - the checked-out branch, marked skipped: it is never deleted;
  - what deleting from a remote does: it changes the remote for everyone who uses it;
  - for a remote, the tracking branches that go with it. Nothing on the remote itself is touched.

  A long question scrolls inside its card.
- **The batch goes on past a failure.** One notification says what went, and what did not and why,
  and the page is read once, at the end.
- **A single delete asks in its own words, as before,** and the button that goes ahead is red, as
  every button that loses something is. A branch with more than ten commits of its own gives their
  true number.

### The window's title

- **Enigma Git Client:** the start window's title, and the repository window's after the
  repository's name (`my-repo — Enigma Git Client`).

### Changes

- **The buttons on every row are gone** from the three pages. Each line's right-click menu has them,
  and answers anywhere on the line:
  - branches: the eye, *Check out* and *Delete*;
  - tags: *Check out* and *Delete*;
  - remotes: *Fetch*, *Edit* and *Remove*.
- **The second lines are gone:** what they said is in each line's tooltip.
- **The branches' markers changed:**
  - the checked-out branch is no longer bold: it says *checked out*;
  - a hidden branch is no longer dimmed: it says *hidden*;
  - *upstream gone* is gone: such a branch is on no remote, and says *local only*;
  - so is the *published* mark: a branch that does not say *local only* is on a remote.
- **The branches page's group headings** are the tree's top-level nodes.

### Upgrading from 5.9

- **The row buttons are in the menus:** right-click a line (see *Changes*).
- **`settings.json` moves to version 7.**
  - An automatic refresh of exactly 15 seconds, 5.9's default, becomes 60.
  - Any other value is kept, and 0 still turns it off. From 6.0 on, 15 is kept like any other value.
  - The file gains `watchFileSystem`.
- **Going back to 5.9** reads the version 7 file and keeps your interval. It ignores
  `watchFileSystem`, and a preference saved in 5.9 drops it, so 6.0 watches again.
- **On Linux,** each open repository takes 3 inotify instances (`fs.inotify.max_user_instances`, 128
  on some distributions) and one watch per folder (`fs.inotify.max_user_watches`). Near those
  limits, raise them, or turn the watcher off in Settings.

### Dependencies

- No package had an update outside the Avalonia set. The set is still held back at **12.1.1** with
  Enigma.Avalonia.Desktop 1.2.0 (12.1.3 is out; the set moves as a whole).

### Version

- **6.0.0** is a major release under Semantic Versioning. It removes functionality: the three pages'
  row buttons, their second lines, and the branches' *upstream gone* and *published* markers. It
  also changes a default, the automatic refresh's interval, and moves it for settings that kept the
  old one. As for 2.0.0 to 5.0.0, that makes it a major release. Everything else is new,
  backward-compatible functionality.

## 5.9.0 — 2026-10-08

A minor release with one fix: on Windows, a change to your settings, your profiles or the recent
repositories is no longer lost when another instance of the app reads the same file at that moment.

### Fixes

- **A saved change is no longer lost on Windows.**
  - The app saves your settings, your identity profiles, the recent repositories and the hidden
    branches by writing a new file and putting it in place of the old one.
  - On Windows, putting it in place is refused while another program has the old file open, for
    instance a second instance of the app reading it at that very moment. The refusal was logged,
    and the change was lost.
  - The app now waits for the reader to let go, for half a second or so, and the change lands.
    A reader holds the file for far less than that. A file held open longer still fails the save, as
    before, with a warning in the log.
  - Linux was never affected.

### The test suites

- **The racing write test can no longer hang.**
  `AtomicFileTests.AReaderRacingTheWriter_AlwaysReadsAWholeDocument` spun forever on Windows. It now
  passes in seconds, and a writer that fails makes the test fail instead of hang.
- **The integration suite is about 18% faster** where it was measured.
  - Its throwaway repositories no longer start git's automatic maintenance, which was 920 of the
    suite's 7,054 git processes.
  - The history that `RefReaderTests` and `CommitLogReaderTests` read is built once per run and
    copied into each test, instead of being rebuilt for each of their 45 tests.

### Upgrading from 5.8

- **Nothing you saved changes.** `settings.json` stays at version 6, and going back to 5.8 keeps
  everything.

### Dependencies

- No package had an update outside the Avalonia set. The set is still held back at **12.1.1** with
  Enigma.Avalonia.Desktop 1.2.0 (12.1.3 is out; the set moves as a whole).

### Version

- **5.9.0** is a minor release under Semantic Versioning. It carries a fix and faster test suites,
  which a patch release (5.8.1) could also have carried. Nothing is removed, and the stored settings
  do not change.

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
