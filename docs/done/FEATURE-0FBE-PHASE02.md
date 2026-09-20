# FEATURE-0FBE-PHASE02 — The branches page is only branches

**Item:** FEATURE-0FBE — Tags get their own page
**Branch:** `feature/feature-0fbe-phase02-branches-only`
**Run:** feature/2026-09-21-refs-tags-and-dragging

## Summary

The branches page is about branches, and nothing else.

Gone from `BranchesPageViewModel`: `Tags`, `ShowTags`, `ShowBranches`, `SelectedTag`,
`ShowBranchesCommand`, `ShowTagsCommand`, `CreateTagCommand`, `CheckoutTagCommand`,
`DeleteTagCommand` and their handlers, the `ITagOperations` dependency, and the tag branch of
`IsEmpty`, `EmptyMessage`, `Rebuild` and `RestoreSelection` — which loses its second parameter and
becomes the one-line expression it always wanted to be. The page's title is "Branches", its filter
box says "Filter branches", and its empty state offers to open a repository "to manage its
branches".

Gone from the view: the `TagRow` template, the `TagList`, the two segmented toggles and the "New
tag" button. The branch list and the "New branch" button stop being conditional — there is nothing
left to switch to — and the header grid drops from five columns to four. `ForgetHover` in the
code-behind walks one list now instead of two.

The tag tests followed the feature. `TagsAndCheckoutTests` keeps what it is for — the dialogs, the
delete confirmation's wording, the detached-HEAD warning and the offer to make a branch instead —
retargeted at `TagsPageViewModel`; what it held that `TagsPageTests` already covers verbatim (the
list with its kinds, the filter, the annotated create, the confirmed delete) is gone, as is the test
for the switch that no longer exists.

## Files / modules touched

**Modified — App**

- `ViewModels/Pages/BranchesPageViewModel.cs` — the tag half removed, the class remark pointing at
  `TagsPageViewModel`, `RestoreSelection` down to one parameter
- `Views/Pages/BranchesPageView.axaml` — the tag template, the tag list, the toggles and the "New
  tag" button removed; the branch list unconditional; four header columns
- `Views/Pages/BranchesPageView.axaml.cs` — `ForgetHover` over the one list, and the type's summary

**Modified — tests**

- `TagsAndCheckoutTests.cs` — an `OpenTagsAsync` helper; the tag tests retargeted at the tags page;
  four duplicated tests and the switch test removed; the snapshot renders `TagsPageView`
- `BranchesPageTests.cs` — `Selection_OfATagIsItsOwn` removed; the tags page's own suite covers it
- `ShellRenderTests.cs` — the branches toolbar draws two icons, not four

**Modified — docs**

- `docs/roadmap.md`, `docs/plan/FEATURE-0FBE.md`

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| What to do with `TagsAndCheckoutTests` | Retarget it, and remove only what `TagsPageTests` already covers word for word | Its subject is the questions the operations ask, not the page; deleting it would lose the delete wording, the default button and the "create a branch instead" flow |
| The `Page_DrawsItsTags` snapshot | Kept, now rendering `TagsPageView` | It is the assertion that the tag rows actually paint; the page they paint on changed, the question did not |
| The branches page's empty title | "Branches" | It said "Branches and tags" and would now be naming something that is not there |

## Deviations & follow-ups

- **One deviation from the plan.** Step 5 said the documentation sweep would correct
  `RELEASENOTES.md` to say tags have their own page. The sweep found nothing to correct: neither
  `README.md` nor `RELEASENOTES.md` ever said *where* tags live, only that the client creates and
  deletes them and that their list keeps its selection — all still true. Per the sweep's own rule
  (edit only what the diff made factually wrong) no edit was made. Announcing the new page is a
  release-notes decision for whoever cuts 1.0.0, not a correction.
- `TagsPageView` has no drag gesture and takes no drops, unchanged from PHASE01.
- **Line endings (recommendation only):** no CRLF churn in the touched files. No action taken.

## Build/test evidence

```
dotnet build Enigma.GitClient.slnx
  0 Warning(s)
  0 Error(s)

dotnet test --solution Enigma.GitClient.slnx
  Test run summary: Passed!
  total: 1792  failed: 0  succeeded: 1792  skipped: 0
```

Six duplicated or obsolete tests removed (1798 → 1792). **One fix cycle:** the first run left
`ShellRenderTests.AToolbarButtonsIconIsTheToolbarSize` red — it required at least four toolbar icons
on the branches page, counting the two segmented toggles this dev removed. The assertion now expects
the two that are actually there and says why the others went.

## Documentation sweep

`README.md` ("Tag management — create (lightweight or annotated) and delete", "Select a branch, tag
or remote in its list") and `RELEASENOTES.md` ("The branches, tags and remotes lists select a
row…", "Tags: create (lightweight or annotated) and delete.") describe what the client can do with
tags, never which page does it. Nothing in either became wrong, so no edits. There is no
`CLAUDE.md`, `CHANGELOG.md` or `CONTRIBUTING.md` in the repository.
