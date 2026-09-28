# BUG-58A7 — Azure DevOps sign-in answers 400

**Status:** TODO
**Type:** BUG
**Branch:** `bugfix/bug-58a7-azure-preview-api`
**Run:** bugfix/2026-09-28-history-stash-menus-release

## Objective

Connecting an Azure DevOps account always fails with "Azure DevOps answered 400 Bad Request."

## Context & constraints

- `AzureDevOpsProvider.ValidateCredentialAsync`
  (`src/Enigma.GitClient.Core/Hosting/Providers/AzureDevOpsProvider.cs`) calls
  `_apis/connectionData?api-version=7.1`. `connectionData` is a preview-only resource, so Azure DevOps
  rejects a plain version with 400 `VssInvalidPreviewVersionException` ("The -preview flag must be
  supplied in the api-version"). Checked against the live service: `7.1` → 400, `7.1-preview` → 200.
- `git/repositories` is a released resource at 7.1 and stays on `ApiVersion`.

## Steps

1. `AzureDevOpsProvider`: `private const string ConnectionDataApiVersion = ApiVersion + "-preview";`
   with a comment saying why, used only for the `connectionData` call.
2. `AzureDevOpsProviderTests.ValidatingATokenAsksWhoTheConnectionBelongsTo`: the expected URI ends in
   `?api-version=7.1-preview`.
3. Run the Core unit tests (and the whole suite, per the Definition of Done).

## Acceptance criteria

- The validation request uses `api-version=7.1-preview`; the repository listing still uses `7.1`.
- Build clean with zero warnings; the Core unit tests and the whole suite green.

## Out of scope

- Normalising the instance URL (a project segment such as `/ProjectName` gets a 404).
- Showing the `X-TFS-ServiceError` reason in error messages.

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| Fix shape | Exactly as specified: a derived `-preview` constant for `connectionData` only | The spec is precise and verified against the live service | Bumping `ApiVersion` globally; a hard-coded string |
