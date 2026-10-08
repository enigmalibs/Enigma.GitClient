# Security Policy

Enigma.GitClient is a desktop git client that drives the real `git` against your repositories and
keeps personal access tokens for GitHub, GitLab and Azure DevOps, so a defect in it can reach your
source code, your hosting accounts, or the credentials that open them. Vulnerability reports are taken
seriously and handled with priority.

## Supported versions

Security fixes are provided for the latest released version. Enigma.GitClient follows
[Semantic Versioning](https://semver.org/), and users are encouraged to stay current with the newest
release.

| Version | Supported          |
|---------|--------------------|
| 5.9.x   | :white_check_mark: |
| 5.8.x   | :x:                |
| 5.7.x   | :x:                |
| 5.6.x   | :x:                |
| 5.5.x   | :x:                |
| 5.4.x   | :x:                |
| 5.3.x   | :x:                |
| 5.2.x   | :x:                |
| 5.1.x   | :x:                |
| 5.0.x   | :x:                |
| 4.x     | :x:                |
| 3.x     | :x:                |
| 2.x     | :x:                |
| 1.x     | :x:                |

## Reporting a vulnerability

**Please do not report security vulnerabilities through public GitHub issues, discussions, or pull
requests.** Public disclosure before a fix is available puts every user at risk.

Instead, use **GitHub's private vulnerability reporting**:

1. Go to the repository's **Security** tab.
2. Select **Report a vulnerability** to open a private advisory.
3. Include as much detail as you can — the affected version, the component involved, a description of
   the issue, and, where possible, a minimal reproduction and its impact.

This keeps the report private between you and the maintainers while it is triaged and fixed.

## What to expect

- Your report will be acknowledged and triaged as promptly as possible.
- The issue will be investigated and, once confirmed, a fix prepared and released.
- Coordinated disclosure is preferred: please allow a reasonable period for a fix to ship before any
  public discussion of the vulnerability.
- Your contribution will be credited in the resulting advisory unless you ask to remain anonymous.

## Scope

Reports concerning the way Enigma.GitClient builds and runs `git` commands, stores and uses access
tokens (encrypted at rest, redacted from logs), talks to the hosting providers, and reads and writes
the files in its configuration directory are in scope, as is the Linux installer in
`packaging/linux/`. Because Enigma.GitClient drives the system's `git`, issues rooted in git itself
should also be reported to the [git project](https://git-scm.com/community).
