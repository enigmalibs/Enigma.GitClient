# BUG-58A7 — Azure DevOps sign-in answers 400

**Item:** BUG-58A7 — Azure DevOps sign-in answers 400
**Branch:** `bugfix/bug-58a7-azure-preview-api`
**Run:** bugfix/2026-09-28-history-stash-menus-release

## Summary

Connecting an Azure DevOps account always failed with "Azure DevOps answered 400 Bad Request". Token
validation calls `_apis/connectionData`, which only exists as a preview resource, and Azure DevOps
refuses a plain `api-version=7.1` for it with `VssInvalidPreviewVersionException`.

`AzureDevOpsProvider` now has `private const string ConnectionDataApiVersion = ApiVersion + "-preview";`,
with a comment saying why, and uses it for the `connectionData` call only. `git/repositories` is a
released resource at 7.1 and stays on `ApiVersion`.

## Files / modules touched

**Modified**

- `src/Enigma.GitClient.Core/Hosting/Providers/AzureDevOpsProvider.cs` — the constant and its use.
- `tests/Enigma.GitClient.Core.UnitTests/Hosting/AzureDevOpsProviderTests.cs` —
  `ValidatingATokenAsksWhoTheConnectionBelongsTo` now expects `?api-version=7.1-preview`.
- `docs/roadmap.md`, `docs/plan/BUG-58A7.md` — statuses.

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| Where the reason is written | An XML `<remarks>` on the constant, naming the exception and its message | The file documents every member with XML comments, and it keeps the reason next to the value it explains |

## Deviations & follow-ups

- None from the plan.
- Out of scope, as the spec says:
  - normalising an instance URL that carries a project segment (`/ProjectName` → 404);
  - showing the `X-TFS-ServiceError` reason in error messages.
- Line endings: no CRLF churn.

## Documentation sweep

The README's *Connecting a host* table is unaffected: scopes and instance URLs are unchanged. No edit.

## Build/test evidence

- `dotnet build Enigma.GitClient.slnx`: 0 warnings, 0 errors.
- `dotnet test --project tests/Enigma.GitClient.Core.UnitTests`: **1087 passed**, 0 failed.
- `dotnet test --solution Enigma.GitClient.slnx`: **2368 passed**, 0 failed, with no fix cycle.
