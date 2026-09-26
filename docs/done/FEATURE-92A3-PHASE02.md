# FEATURE-92A3-PHASE02 — No first-parent history

**Item:** FEATURE-92A3 — History: no scope, no first parent
**Branch:** `feature/feature-92a3-phase02-no-first-parent`
**Run:** feature/2026-09-26-release-1-0-0

## Summary

The History toolbar no longer has its "First parent only" checkbox, and nothing in the app can make
the history follow first parents any more: the graph always follows every parent of every merge.

What went with the checkbox — everything that existed to serve it:

- `HistoryPageViewModel.FirstParentOnly`, and the branch of `ApplySettings` that copied the preference
  into it;
- the Settings page's "Follow first parents only" toggle and `SettingsPageViewModel.FirstParentOnly`
  (with its change notification);
- the preference itself, `AppSettings.FirstParentOnly`.

A `settings.json` written by an earlier build still carries `"firstParentOnly"`; `System.Text.Json`
ignores the member it no longer knows, so the file loads with every other preference intact, and the
key disappears at the next save — a test pins both.

## Files / modules touched

**Modified**

- `App/Views/Pages/HistoryPageView.axaml` — the checkbox removed, the toolbar grid renumbered
- `App/ViewModels/Pages/HistoryPageViewModel.cs` — `FirstParentOnly` removed; `ApplySettings` rebuilds on
  the date style or the page size alone; the class remark says every parent is followed
- `App/ViewModels/Pages/SettingsPageViewModel.cs`, `App/Views/Pages/SettingsPageView.axaml` — the toggle
  and its property removed
- `Core/Configuration/AppSettings.cs` — `FirstParentOnly` removed
- `RELEASENOTES.md` — "first-parent history" dropped from the preferences (sweep)

**Tests**

- `App.UnitTests/HistoryPageTests.cs` — `Page_HidesMergedInBranchesWithFirstParentOnly` removed;
  `Page_FollowsEveryParentOfAMerge` (new: a commit reachable only through a merge's second parent is in
  the graph); `Toolbar_HasNoBranchScopeSelectorAndNoFirstParentSwitch` (no combobox, no checkbox)
- `App.UnitTests/SettingsPageTests.cs` — the history-preferences test without the first-parent lines; the
  rendered Settings page names no first-parent preference
- `Core.UnitTests/Configuration/SettingsServiceTests.cs` — the restart round trip without it;
  `AFileFromWhenTheHistoryCouldFollowFirstParents_StillLoads_AndForgetsIt` (new)

## Deviations & follow-ups

- The engine's `CommitLogQuery.FirstParentOnly` and its integration test stay, as planned: an engine
  option like the author, message, path and date filters the UI does not use either.
- No settings-version bump: nothing needs migrating, and a file of the current version with the old key
  is exactly the case the new test covers.

## Build/test evidence

- `dotnet build Enigma.GitClient.slnx` — 0 warnings, 0 errors.
- `dotnet test --solution Enigma.GitClient.slnx` — 2153 passed, 0 failed (2 new, 1 removed).

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| How the removal is pinned in the history | A test that a commit reachable only through a second parent is shown | The positive statement of what the page does now, rather than the absence of a property |
| The release-notes preferences line | Rewrapped after the removal | The sweep edits the sentence it made wrong, and keeps it readable raw |
