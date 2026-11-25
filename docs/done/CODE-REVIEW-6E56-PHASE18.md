# CODE-REVIEW-6E56-PHASE18 — [Medium] XAML-01 Field label styles

## Summary

Forty-odd field labels wrote `FontSize="12"` and the secondary foreground by hand, and about a dozen
validation lines wrote `FontSize="12"`, `TextWrapping="Wrap"` and the error brush. A change to how a
form label or an error reads was fifty-odd edits, and each new dialog copied whichever neighbour it
started from.

What changed:

- `Themes/Styles.axaml` has two `TextBlock` classes next to `dim` and `faint`. They use the brush keys
  the sites used:
  - `fieldlabel`: size 12, `EnigmaForegroundSecondaryBrush`;
  - `validation`: size 12, wrapping, `EnigmaErrorBrush`.
- 55 sites in the 16 views of the review use them: 43 labels and 12 validation lines. A script made
  the change, and a second run finds no site left. Each site lost exactly the attributes its class now
  sets, and kept every other one where it was. For example, `SquashDialogView`'s "Untouched" line is
  a label that also wraps: it keeps `TextWrapping="Wrap"` inline.

## Files/modules touched

- Modified: `src/Enigma.GitClient.Desktop/Themes/Styles.axaml`
- Modified (Dialogs): `AddHostAccountDialogView.axaml`, `CloneRepositoryDialogView.axaml`,
  `CreateBranchDialogView.axaml`, `CreateTagDialogView.axaml`, `CreateWorktreeDialogView.axaml`,
  `IdentityProfileDialogView.axaml`, `InitRepositoryDialogView.axaml`, `RemoteDialogView.axaml`,
  `RemoveWorktreeDialogView.axaml`, `RenameBranchDialogView.axaml`, `SquashDialogView.axaml`,
  `StashDialogView.axaml`, all under `src/Enigma.GitClient.Desktop/Views/Dialogs/`
- Modified (Pages): `ConflictResolutionPageView.axaml`, `ProfilesPageView.axaml`,
  `SettingsPageView.axaml`, under `src/Enigma.GitClient.Desktop/Views/Pages/`
- Modified: `src/Enigma.GitClient.Desktop/Views/Panels/ChangedFilesPanelView.axaml`
- Modified: `docs/roadmap.md`, `docs/plan/CODE-REVIEW-6E56.md`
- Created: `docs/done/CODE-REVIEW-6E56-PHASE18.md`

## Deviations & follow-ups

- **55 sites, not 52.** Three sites in the review's own files carry the same attribute set but are
  not on its list: `CreateWorktreeDialogView` (the validation line), `RemoteDialogView` (the
  validation line) and `RenameBranchDialogView` (its first label). They were converted too, so no
  file mixes the class and the inline copy. The review also counted `SquashDialogView`'s "Untouched"
  line (a secondary, wrapping note) as a validation line. Its attributes make it a label that wraps,
  and that is how it was converted.
- Two of the listed sites are not form labels in the strict sense: the conflict page's progress line
  and the squash dialog's "Untouched" note. They are listed by the review and carry exactly the
  label's look, so they take the class.
- Four sites outside the review's sixteen files write the same pair: `SetUpstreamDialogView.axaml:9`,
  `WorkingTreePanelView.axaml:18` and `:69`, and `Themes/Controls.axaml:70`. They are not touched: a
  follow-up sweep can move them to the classes.

## Build/test evidence

- Baseline: see `docs/done/CODE-REVIEW-6E56-PHASE01.md`.
- **This dev:**
  - `dotnet build Enigma.GitClient.slnx --no-incremental`: 0 warnings (`AVLN*` included), 0 errors.
  - Core unit: 1534 run, 1531 passed, 0 failed, 3 skipped.
  - Core integration: 449 run, 447 passed, 0 failed, 2 skipped (6 min 24 s).
  - Desktop: compiled, **not run** (this machine's standing rule).
- **Rendering check (throwaway harness outside the repository).** On the Avalonia 12.1.4 headless
  platform with the application's own `App` styles, each of the 16 views was created in a window, in
  the dark and the light theme. Every realised `TextBlock` with the new classes resolved to the values
  it used to carry inline: size 12, the theme's secondary or error brush, and wrapping for the
  validation lines. That covers 38 labels and 10 validation lines, all passing. The other 7 sites sit
  in item templates (the profile cards, the conflict page's file header) that are not realised without
  data. They use the same classes and the same style.
- A scan for unclassed `TextBlock` selectors that set a size or a brush found none that reaches these
  sites, so no style now outranks what used to be a local value.
- **Fix budget:** 0 cycles.

## Documentation sweep

- Nothing stale: no user doc describes the styles.

## Decisions taken at build time

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| Sites of the same pattern beyond the review's list, in its files | Converted | A file half on the class and half inline is the drift the finding is about | Leaving them for a later sweep |
| Where the class goes in the tag | In place of the first removed attribute | The smallest diff, and each site's other attributes keep their order | Moving `Classes` to the front of every tag |
| How the change was made | A script that rewrites only `TextBlock` tags carrying the exact attribute set, run twice (apply, then confirm nothing is left) | Fifty-five identical edits by hand invite a slip | Editing each site by hand |
