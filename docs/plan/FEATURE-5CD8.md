# FEATURE-5CD8 — Release 3.0.0

**Status:** TODO
**Type:** FEATURE
**Branch:** `feature/feature-5cd8-release-3-0-0`
**Run:** feature/2026-09-27-release-3-0-0

## Objective

Cut the next version now that `FEATURE-6C81` is in, numbered by Semantic Versioning. That means:

- the version stated once for the whole solution;
- a dated section in the release notes, with an upgrade note;
- the README's what's-new callout;
- the supported version in `SECURITY.md`;
- a dependency refresh;
- the merge/tag/push runbook, printed for the user.

## Context & constraints

- **An app, not a package** (no `PackageId`). `docs/RELEASE.md` is the runbook, parameterised by
  `X.Y.Z`. 2.0.0 (`FEATURE-4AC8`) is the template for this item.
- **Since 2.0.0**, tagged and on `origin/main`, one item has landed: `FEATURE-6C81`, *Git signs in with
  the profile's token*. Every fetch, pull, push, tag or remote-branch push and clone to an HTTPS
  remote that one of the profile's integrations covers now signs in with that integration's token.
- **The version, by SemVer:**
  - The feature is new functionality.
  - It is not backward compatible. For each origin it signs in to, the credential helper
    `-c credential.<origin>.helper=` empties git's helper list (`GitCredentials`), so the user's own
    helper is no longer asked for that host.
  - 2.0's scope hints and README asked for read-only tokens: a fine-grained GitHub token with
    *Contents: read*, GitLab `read_repository`, Azure DevOps *Code: Read*.
  - 2.0's README stated *"The integration is the permission, not the credential: git still signs in
    with its own credential helper or SSH key"*, and its release notes stated that *no token is ever
    handed to git*.

  So a 2.0 user who pushes over HTTPS through their own credential helper, with a read-only token,
  has pushes refused after the upgrade until they replace the token. The same goes for a fine-grained
  GitHub token limited to selected repositories: another private repository on that host no longer
  fetches with the user's own credentials. An upgrade that can stop an existing workflow until the
  user acts is a **MAJOR** release, as `FEATURE-4AC8` ruled for 2.0.0: **3.0.0**.
- No stored format changed since 2.0.0: `host-accounts.json`, `identity-profiles.json`,
  `settings.json` and the tokens are as 2.0 wrote them. That goes in the notes' upgrade section, not
  the version.
- The Avalonia set stays coupled with Enigma.Avalonia.Desktop.
- A run never tags, pushes or merges into `develop`/`main`: those commands are printed.

## Steps

1. `Directory.Build.props` — `<Version>3.0.0</Version>`.
2. Dependency refresh:
   - run `dotnet list package --outdated`;
   - apply the non-coupled patch/minor bumps;
   - hold the Avalonia set (and anything major) back;
   - log every change and every hold-back.
3. `RELEASENOTES.md` — `## 3.0.0 — <date>` on top, in the file's themed shape:
   - *Signing in* — which operations sign in, with which integration, what SSH and uncovered hosts
     keep, and how the token reaches git;
   - *Refused tokens* — the two messages, and 401/403 as authentication failures;
   - *Upgrading from 2.0* — replace read-only tokens you push with, fine-grained tokens and their
     repository selection, and the file compatibility both ways;
   - the *Dependencies* and *Version* sub-sections.
4. `README.md` — the callout becomes *What's new in 3.0*.
5. `SECURITY.md` — 3.0.x supported, 2.x and 1.x not.
6. `docs/RELEASE.md` — the MSI paragraph records 3.0.0 too.
7. Pre-flight in Release: build clean, suite green, and the Core assembly carries `3.0.0+<sha>`.
8. Print the runbook.

## Acceptance criteria

- The Release build is clean with zero warnings, and the Release suite is green.
- The built assemblies carry version 3.0.0.
- `RELEASENOTES.md` has one dated `3.0.0` section covering `FEATURE-6C81`, including the upgrade note
  and the dependency changes. The README callout and `SECURITY.md` name 3.0.
- Nothing in the dev runs `git tag`, `git push`, `dotnet pack` or `dotnet nuget push`.

## Out of scope

- NuGet packaging; a Windows MSI profile.
- Bumping the coupled Avalonia set, or any major dependency version.
- Changing `FEATURE-6C81`'s behaviour to keep 2.0 compatibility (for example, falling back to the
  user's helper when a token is refused). The release numbers what shipped; it does not reshape it.
- Tagging, merging, pushing.

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| Version number | 3.0.0 | SemVer MAJOR. The token now replaces the user's own helper on the hosts a profile covers, so pushes 2.0 ran with a read-only token (which 2.0 asked for) are refused until the token is replaced; a documented 2.0 guarantee is reversed | 2.1.0 (it hides a behaviour break behind a minor, which `FEATURE-4AC8` rejected for the same reason); 2.0.1 (not only fixes) |
| Two majors on the same day | Accepted | SemVer numbers compatibility, not time or size; a smaller number would mislead the users it breaks | Holding the feature back for a later major |
| An upgrade note | *Upgrading from 2.0* in the 3.0.0 section | A major release says what to do | Only the feature list |
| Dependency refresh | Non-coupled patch/minor bumps, logged; the Avalonia set and majors held | `dotnet-release`'s rule, as in 1.1.0 and 2.0.0 | No refresh |
| `SECURITY.md` rows | `3.0.x` supported; `2.x` and `1.x` not | The file's policy: fixes go to the latest release | Keeping 2.0.x supported |
| MSI profile, tag | None; a bare `3.0.0`, printed | The conventions 1.0.0 to 2.0.0 set | Generating an MSI; `v3.0.0` |
| Phasing | One phase | A routine release, per `dotnet-release`: one reviewable commit | Splitting notes from the version |
