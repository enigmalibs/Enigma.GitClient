using System;
using System.Collections.Generic;

namespace Enigma.GitClient.Core.Git;

/// <summary>
/// Default <see cref="IGitCommandFactory"/>. Every invocation the client makes goes through here,
/// which is what makes the standard global options and the forbidden-operation check unavoidable.
/// </summary>
public sealed class GitCommandFactory : IGitCommandFactory
{
    /// <summary>
    /// The global options prepended to every invocation.
    /// </summary>
    /// <remarks>
    /// <c>--no-pager</c> removes any chance of git waiting on a pager; <c>core.quotePath=false</c>
    /// makes git emit paths as raw UTF-8 instead of C-style escapes, which is what the parsers
    /// expect; <c>color.ui=false</c> keeps ANSI sequences out of parseable output even when the
    /// user's configuration forces colour.
    /// </remarks>
    public static readonly IReadOnlyList<string> GlobalOptions =
    [
        "--no-pager",
        "-c",
        "core.quotePath=false",
        "-c",
        "color.ui=false",
    ];

    /// <summary>
    /// The sub-commands that move HEAD or the work tree, which run with <c>submodule.recurse</c> off.
    /// </summary>
    /// <remarks>
    /// A user's <c>submodule.recurse=true</c> makes these move — and, configured so, rebase — the
    /// submodules as well. The application's own follow step is the only thing that moves a submodule
    /// (<c>ISubmoduleService.FollowAsync</c>), under the rule that it never moves one with uncommitted
    /// work, so git's recursion is switched off for these, whatever the configuration says.
    /// </remarks>
    public static readonly IReadOnlySet<string> SubmoduleRecursionOffVerbs = new HashSet<string>(StringComparer.Ordinal)
    {
        "checkout",
        "switch",
        "pull",
        "merge",
        "reset",
        "revert",
        "cherry-pick",
        "restore",
        "read-tree",
    };

    /// <summary>The override that switches git's own submodule recursion off.</summary>
    public const string SubmoduleRecursionOff = "submodule.recurse=false";

    /// <inheritdoc />
    public GitCommand Create(string workingDirectory, params string[] arguments)
        => Create(workingDirectory, (IEnumerable<string>)arguments);

    /// <inheritdoc />
    public GitCommand Create(string workingDirectory, IEnumerable<string> arguments)
        => Build(workingDirectory, arguments, standardInput: null);

    /// <inheritdoc />
    public GitCommand CreateWithInput(string workingDirectory, string standardInput, IEnumerable<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(standardInput);
        return Build(workingDirectory, arguments, standardInput);
    }

    private static GitCommand Build(string workingDirectory, IEnumerable<string> arguments, string? standardInput)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        List<string> vector = [.. GlobalOptions];
        int start = vector.Count;
        vector.AddRange(arguments);

        if (GitArgumentVector.FindVerb(vector) is { } verb && SubmoduleRecursionOffVerbs.Contains(verb))
        {
            vector.InsertRange(start, ["-c", SubmoduleRecursionOff]);
        }

        if (vector.Count == GlobalOptions.Count)
        {
            throw new ArgumentException("A git command needs a sub-command.", nameof(arguments));
        }

        ForbiddenGitOperations.EnsureAllowed(vector);

        return new GitCommand(workingDirectory, vector, environment: null, standardInput);
    }
}
