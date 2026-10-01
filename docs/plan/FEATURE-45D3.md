# FEATURE-45D3 — Info bars close after 2.5 seconds

**Status:** DONE — see `docs/done/FEATURE-45D3.md`
**Type:** FEATURE
**Branch:** `feature/feature-45d3-info-bars-2-5-seconds`
**Run:** feature/2026-10-01-polish-release-5-1

## Objective

A success or an informational info bar closes itself after **2.5 seconds** instead of 5. A warning or
an error still stays until the user closes it.

## Context & constraints

- **One place decides it:** `InfoBarServiceExtensions.TransientDisplayDuration`
  (`src/Enigma.GitClient.App/Services/InfoBarServiceExtensions.cs`), used by `Notify` for
  `InfoBarSeverity.Success` and `InfoBarSeverity.Info`. Every report in the app goes through `Notify`
  (FEATURE-A5D3).
- **Tests that assert the 5 s:**
  - `InfoBarNotificationTests` (`FiveSeconds`, used twice);
  - `SettingsPageTests:251`;
  - `MergeOperationTests:392,420`;
  - `ProfilesPageTests:216`.
- The 1.1.0 section of `RELEASENOTES.md` says "closes itself after 5 seconds". That is history and
  stays as written. The 5.1.0 notes say what changed.

## Steps

1. `TransientDisplayDuration = TimeSpan.FromSeconds(2.5)`. Its summary and the `Notify` remark keep
   referring to the constant, not to a number.
2. `InfoBarNotificationTests`: the expected duration becomes a literal 2.5 s, which is where the spec
   value is pinned.
3. The other three tests compare against `InfoBarServiceExtensions.TransientDisplayDuration`. What
   they check is that the report is timed, not the number of seconds.
4. Whole suite.

## Acceptance criteria

- Success and Info bars are shown with a 2.5-second display duration. Warning and Error bars have
  none.
- The spec value is asserted literally in exactly one test.
- Build clean with zero warnings; the whole suite green.

## Out of scope

- Making the duration a setting.
- Changing which severities close themselves.

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| Which bars change | Success and Info only | As asked. A warning or an error asks the user to look, and stays | Timing warnings too |
| How the tests hold the value | One literal 2.5 s in `InfoBarNotificationTests`, the constant elsewhere | The next change to the number touches one test, not four | Replacing every literal `5` with `2.5` |
| Accessibility of a shorter bar | Accepted as asked | Only non-critical confirmations shorten. Anything that needs action stays until dismissed | Keeping 5 s for long messages |
