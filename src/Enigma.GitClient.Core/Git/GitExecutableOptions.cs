using System;
using System.Collections.Generic;

namespace Enigma.GitClient.Core.Git;

/// <summary>
/// Options controlling how the <c>git</c> executable is located and run.
/// </summary>
public sealed class GitExecutableOptions
{
    /// <summary>
    /// The configuration section these options bind to.
    /// </summary>
    public const string SectionName = "Git";

    /// <summary>
    /// Gets or sets an explicit path to the git executable. When set, it is used as-is and no
    /// probing happens; this is the escape hatch for a git installed somewhere unusual.
    /// </summary>
    public string? ExecutablePath { get; set; }

    /// <summary>
    /// Gets environment variables applied to every git child process, on top of the runner's own
    /// defaults and beneath any per-command override. Use it for things like a custom
    /// <c>GIT_SSH_COMMAND</c>, or to pin the configuration files a test run may see.
    /// </summary>
    public IDictionary<string, string> EnvironmentOverrides { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal);
}
