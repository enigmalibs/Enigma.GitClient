# FEATURE-1406-PHASE05 — A profile pushes only where it may

**Item:** FEATURE-1406 — Profiles that own their integrations
**Phase:** PHASE05 — A profile pushes only where it may
**Branch:** `feature/feature-1406-phase05-push-guard`
**Run:** feature/2026-09-27-diff-profiles-release

## Summary

A push now goes out only where the repository's profile has an integration. A profile without any
integration is **local only**: it never pushes.

- **Which profile.** `IGitIdentityService.GetEffectiveAsync(repository)` reads the identity git commits
  with in the repository. It runs `git config -z --get-regexp ^user\.(name|email)$` in the work tree
  with no scope flag, which covers every scope and their includes, conditional ones too; the last value
  of each key wins. The repository's profile is the first one matching that identity, which is also
  the rule that marks a profile *Current*.
- **The rule.** `ProfilePushRule.Evaluate` in Core is a pure function of the profiles, the accounts,
  the identity and the push URL. It returns one of four reasons:

  | Reason | When | Push |
  |---|---|---|
  | `NoProfile` | no profile matches | goes ahead, as before |
  | `Integration` | an account of that profile owns the push URL's host (`HostAccount.Owns`: HTTPS, SSH, scp-like) | goes ahead |
  | `NoIntegration` | anything else under a profile, including a path on this machine and an earlier integration that belongs to no profile | refused |
  | `CheckFailed` | what decides could not be read | refused |

  The `Target` of a refusal is the host, or the address itself (redacted) when it names no host.
- **The check.** `IPushGuard.CheckAsync(repository, remote)` reads the profiles first, and with none
  it stops there: people who do not use profiles pay one small file read. Otherwise it reads the
  effective identity, the remote's push URL and the accounts, then applies the rule.
  - It **fails closed**: an I/O, git or token-store failure refuses the push (`Unchecked`).
  - A remote that does not exist is left to git, which reports it in its own words.
  - A refusal is logged by profile id and host, never by name or email.
- **Where.** `SyncOperations.PushAsync` (the toolbar's Push) and `PushBranchAsync` (a branch's own
  push in the history) ask the check before running. These are the only two ways the app pushes. A
  refusal is a warning such as *Work does not push to github.com*: *Nothing was pushed: the profile
  Work has no integration for github.com. To push there, connect an account for it to Work on the
  Profiles page.*
- **The page.** A profile without integrations says so: a lock and *Local only — no integration, so
  this profile never pushes.*

## Files / modules touched

**Created**

- `src/Enigma.GitClient.Core/Hosting/ProfilePushRule.cs` — `PushPermissionReason`, `PushPermission`,
  `ProfilePushRule`
- `src/Enigma.GitClient.App/Services/PushGuard.cs` — `IPushGuard`, `PushGuard`
- `tests/Enigma.GitClient.Core.UnitTests/Hosting/ProfilePushRuleTests.cs` — 14 cases:
  - no matching profile, and no profile at all;
  - a profile without integrations;
  - an integration on the remote's host, over HTTPS, scp-like and SSH-over-443;
  - another host; an Enterprise instance against the public one;
  - an earlier integration, and another profile's integration;
  - a local path;
  - an email matched regardless of case; the first matching profile deciding;
  - no credential in a refusal.

**Modified**

- `src/Enigma.GitClient.Core/Identity/GitIdentityService.cs` — `GetEffectiveAsync`,
  `BuildEffectiveReadArguments`
- `src/Enigma.GitClient.App/Services/SyncOperations.cs` — the check before both pushes, and
  `DescribeRefusal`
- `src/Enigma.GitClient.App/DependencyInjection/ServiceCollectionExtensions.cs` — `IPushGuard`
- `src/Enigma.GitClient.App/Views/Pages/ProfilesPageView.axaml` — *Local only* on a profile without
  integrations
- Tests:
  - `tests/…/Infrastructure/FakeGitIdentityService.cs` — `GetEffectiveAsync`: each key from the
    repository's own identity, else the global one.
  - `tests/Enigma.GitClient.Core.UnitTests/Identity/GitIdentityArgumentsTests.cs` — the effective read's
    arguments, and that the command factory accepts them.
  - `tests/Enigma.GitClient.Core.IntegrationTests/Identity/GitIdentityServiceTests.cs` — against a real
    git, the effective identity:
    - is the global one until the repository sets its own;
    - takes each key from the most specific scope that sets it;
    - follows an `includeIf "gitdir:…"`.
  - `tests/Enigma.GitClient.App.UnitTests/RemotesAndSyncTests.cs` — six tests:
    - the toolbar push, and a branch's push, under a local-only profile run nothing and leave the remote
      untouched;
    - a repository whose identity matches no profile pushes as before;
    - the check allows a profile with an integration for the push URL's host (it does not before the
      account is connected);
    - an unreadable identity refuses the push;
    - without profiles, nothing but the profiles file is read.
  - `tests/Enigma.GitClient.App.UnitTests/ProfileIntegrationsTests.cs` — the *Local only* wording.
- `README.md` — docs sweep:
  - the Profiles bullet says a profile pushes only to the hosts it is connected to (and its wrapping is
    fixed: PHASE04 had left one long line);
  - *Connecting a host* explains the rule, and that the integration is the permission, not the
    credential.
- `docs/roadmap.md`, `docs/plan/FEATURE-1406.md` — statuses (the item's own row flips to `DONE` with
  this last phase)

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| Where the rule lives | A pure static function in Core; the App service only reads | Every branch can be tested without git, files or a window |
| A remote name that does not exist | Allowed through to git | Nothing can be pushed there anyway, and git's message names the problem |
| Separate wording for a profile with no integration at all | No: one message naming the host | It is accurate in both cases and says what to do |
| The effective read with nothing set anywhere | `GitIdentity.Empty` (git exits 1) | The same rule as the scoped reads |
| Logging a refusal | Information, by profile id and host | A refusal is expected behaviour, not a fault; names and emails stay out of logs |

## Deviations & follow-ups

- None from the plan.
- Follow-up idea, not done: the repository's section of the Profiles page could say which profile its
  pushes go out under. A refusal already names the profile.
- Documentation sweep: `README.md`, as listed above.

## Build / test evidence

- `dotnet build Enigma.GitClient.slnx`: 0 warnings, 0 errors.
- `dotnet test --solution Enigma.GitClient.slnx`: **2278 passed**, 0 failed, on the first run. That is
  2254 before, plus 24 new: 14 rule cases, 1 argument test, 3 integration tests and 6 App tests.
