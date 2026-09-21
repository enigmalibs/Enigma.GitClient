# FEATURE-0FBE — Tags get their own page

**Status:** DONE
**Type:** FEATURE
**Branch:** one per phase, see below
**Run:** feature/2026-09-21-refs-tags-and-dragging

## Objective

Tags are a page of their own, reached from a "Tags" item under "Branches" in the navigation rail and
laid out like the branches page. The branches page is then only about branches.

## Context & constraints

- `Navigation/ShellNavigation.cs` builds the rail in its constructor: Repositories, History, Changes,
  Branches, Remotes in `navigation.Items`, Conflicts held back, Integrations and Settings in
  `navigation.FooterItems`. Each item names a page type and a page-ViewModel type, and
  `ContainerPageFactory` resolves both from the container. `ShellPage` is the enum of the rail's
  pages and `GoTo` the way anything navigates.
- `DependencyInjection/ServiceCollectionExtensions.AddGitClientApp` registers every page view as
  **transient** and every page ViewModel as a **singleton**, on purpose: navigating back rebuilds
  the control and re-attaches the state.
- `ViewModels/Pages/BranchesPageViewModel.cs` currently owns both halves. Tag-side members:
  `Tags`, `ShowTags`, `ShowBranches`, `SearchPlaceholder`, `SelectedTag`, `ShowBranchesCommand`,
  `ShowTagsCommand`, `CreateTagCommand`, `CheckoutTagCommand`, `DeleteTagCommand`, the
  `TagRowViewModel` class, the tag branch of `IsEmpty` / `EmptyMessage`, the tag half of `Rebuild`
  and of `RestoreSelection`, and `OnCreateTagAsync` / `OnCheckoutTagAsync` / `OnDeleteTagAsync`.
  They depend on `ITagOperations`, `ICheckoutOperations` and `IRepositoryContext.Refs.Tags`.
- `Views/Pages/BranchesPageView.axaml` carries the `TagRow` data template, the `TagList` `ListBox`,
  the two `ToggleButton`s of the segmented switch and the "New tag" button. Its code-behind clears
  the hover state on `BranchList` **and** `TagList` when a drag session ends.
- `PageViewModelBase` gives a page `IsRepositoryOpen`, `IsBusy`, `OnAppearingAsync`,
  `OnRepositoryChanged` and `OnRepositoryStateRefreshed`; `RemotesPageView` is the nearest example of
  a single-list page.
- `MainWindowShellTests` asserts the rail's items by name and count — it will need the new item.
- Tag selection is already its own property because the two lists were alternatives; on a page of its
  own it is simply the page's selection.
- **Baseline:** clean build, 1775 tests green.

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| Page shape | A new `TagsPageViewModel` and `TagsPageView`, registered like every other page | "Same look as Branches" with one list, one selection and one search box is what the branches page's tag half already is, minus the toggle | Reusing `BranchesPageViewModel` behind two rail items (one singleton ViewModel, two rail entries, one shared search box and selection — the toggle problem with extra steps) |
| Where in the rail | Directly after Branches, before Remotes | What was asked, and it keeps the reference pages together | A footer item; a child of Branches (the rail has no nesting) |
| Rail icon | `PhosphorIcon.Tag` | It is the icon the tag rows and the tag badges already use | `Bookmark` (used for nothing here) |
| What happens to the branches page | It loses the toggle, the tag list and every tag member; its title becomes "Branches" | Two pages, two jobs; leaving the tag half behind would mean two ways to delete a tag that can disagree | Keeping the toggle as a shortcut (the reader asked to separate them) |
| Tag operations | Still `ITagOperations` and `ICheckoutOperations`, injected into the new page | The dialogs and confirmations are theirs, and the graph's context menu calls the same services | Moving the operations into the page |
| Order of work | The new page first, the branches page's clean-up second | Each phase leaves the application working and reviewable; nothing is ever without a way to reach tags | One commit for both (a large, hard-to-review diff) |
| The drag gesture | Untouched, and it stays on the branches page | Dragging one branch onto another is a branch gesture; the tags page has nothing to merge | Giving the tags page a drag it has no meaning for |

## PHASE01 — The tags page and its rail item

**Branch:** `feature/feature-0fbe-phase01-tags-page`
**Status:** DONE — see `docs/done/FEATURE-0FBE-PHASE01.md`

### Steps

1. `ViewModels/Pages/TagsPageViewModel.cs`: a page ViewModel over `IRepositoryContext`,
   `ITagOperations` and `ICheckoutOperations` holding `Tags`, `SelectedTag`, `SearchText`,
   `IsEmpty`, `EmptyMessage`, `Title`, `RefreshCommand`, `ClearSearchCommand`, `CreateTagCommand`,
   `CheckoutCommand` and `DeleteCommand`, rebuilding from `RepositoryContext.Refs.Tags` on
   appearing, on a repository change, on a refresh and on every keystroke, and restoring the
   selection by name across a rebuild — the branches page's behaviour, for tags.
2. Same file: `TagRowViewModel` moves here from `BranchesPageViewModel.cs`, owned by the new page.
   The branches page keeps compiling because PHASE02 removes its last use; until then it holds a
   reference to the moved type.
3. `Views/Pages/TagsPageView.axaml` (+ `.axaml.cs`): the branches page's tag half as a page — the
   same header (icon, title, filter box, "New tag", refresh), the same row template, the same
   `EmptyState`, the same `ListBox` conventions (`Padding="0"`, `MinHeight="0"`,
   `HorizontalContentAlignment="Stretch"`, the `pill` style) and the same automation names.
4. `DependencyInjection/ServiceCollectionExtensions.cs`: `TagsPageView` transient,
   `TagsPageViewModel` singleton.
5. `Navigation/ShellNavigation.cs`: a `ShellPage.Tags` member and an `Add(...)` for it between
   Branches and Remotes, with `PhosphorIcon.Tag`.
6. Tests — a new `tests/.../TagsPageTests.cs`: the page lists a repository's tags, filters them,
   keeps its selection across a refresh, reports empty with no repository and with no match, creates
   a tag through the dialog, cancels without creating, deletes through the confirmation, and checks
   one out; the view builds, lays out and shows one row per tag.
7. Tests — `tests/.../MainWindowShellTests.cs`: the rail has six items and Tags sits between
   Branches and Remotes; `tests/.../CompositionRootTests.cs`: the new page and ViewModel resolve;
   `tests/.../ShellRenderTests.cs`: the new view renders like its neighbours.

### Acceptance criteria

- The navigation rail shows Tags directly under Branches, with a tag icon.
- The tags page lists, filters, creates, checks out and deletes tags, with the same dialogs and
  confirmations as before.
- It looks like the branches page: the same header, the same rows, the same empty state.
- Build clean with zero warnings; the whole suite green.

## PHASE02 — The branches page is only branches

**Branch:** `feature/feature-0fbe-phase02-branches-only`
**Status:** DONE — see `docs/done/FEATURE-0FBE-PHASE02.md`

### Steps

1. `ViewModels/Pages/BranchesPageViewModel.cs`: remove `Tags`, `ShowTags`, `ShowBranches`,
   `SearchPlaceholder`, `SelectedTag`, `ShowBranchesCommand`, `ShowTagsCommand`, `CreateTagCommand`,
   `CheckoutTagCommand`, `DeleteTagCommand` and their handlers; `IsEmpty`, `EmptyMessage`, `Rebuild`
   and `RestoreSelection` lose their tag branch; the `ITagOperations` dependency goes; `Title`
   becomes "Branches".
2. `Views/Pages/BranchesPageView.axaml`: remove the `TagRow` template, the `TagList` `ListBox`, the
   segmented `ToggleButton`s and the "New tag" button; the branch list and the "New branch" button
   stop being conditional; the filter box's placeholder is fixed; the empty state is titled
   "Branches".
3. `Views/Pages/BranchesPageView.axaml.cs`: `ForgetHover` walks the branch list only, now that there
   is one list on the page.
4. Tests — `tests/.../BranchesPageTests.cs`: drop the tag tests and the toggle tests that moved to
   the tags page, and keep every branch test; the page's title and empty message name branches.
5. Documentation sweep: `RELEASENOTES.md` describes tags as part of the branches list; it is
   corrected to say tags have their own page.

### Acceptance criteria

- The branches page has no tag list and no branches/tags switch, and shows branches at all times.
- Every branch operation — create, rename, delete, set upstream, check out, merge, drag and drop —
  behaves exactly as before.
- Nothing in the application still reaches the tag half of the branches page.
- Build clean with zero warnings; the whole suite green.

## Out of scope

- New tag operations (pushing a tag, renaming one, annotating an existing one).
- Grouping or sorting tags differently from the branches page's list.
- Changing the create-tag or delete-tag dialogs.
- Tag badges in the history, which are unchanged.
