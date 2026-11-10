using System;
using System.Collections.Generic;

namespace Enigma.GitClient.Core.Git;

/// <summary>
/// The operations Enigma.GitClient refuses to perform, enforced on every argument vector before a
/// process is started.
/// </summary>
/// <remarks>
/// Rebase is a product-level exclusion, not a preference: it is rejected here so no future caller
/// can reintroduce it by accident, and so a repository configured with <c>pull.rebase = true</c>
/// cannot make an ordinary pull rewrite history behind the user's back.
/// </remarks>
public static class ForbiddenGitOperations
{
    /// <summary>
    /// The git sub-commands that are never run.
    /// </summary>
    public static readonly IReadOnlySet<string> ForbiddenVerbs =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "rebase" };

    /// <summary>
    /// Throws when an argument vector would perform a forbidden operation.
    /// </summary>
    /// <param name="arguments">The complete argument vector.</param>
    /// <exception cref="NotSupportedException">The vector would rebase.</exception>
    public static void EnsureAllowed(IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        string? verb = GitArgumentVector.FindVerb(arguments);

        if (verb is not null && ForbiddenVerbs.Contains(verb))
        {
            throw new NotSupportedException(
                $"Enigma.GitClient never runs 'git {verb}'. Rebase is permanently out of scope for this client.");
        }

        foreach (string configuration in GitArgumentVector.EnumerateConfigOverrides(arguments))
        {
            if (IsRebaseEnablingConfiguration(configuration))
            {
                throw new NotSupportedException(
                    $"Enigma.GitClient never enables rebase through configuration ('-c {configuration}').");
            }
        }

        if (string.Equals(verb, "pull", StringComparison.OrdinalIgnoreCase) && ContainsRebaseFlag(arguments))
        {
            throw new NotSupportedException(
                "Enigma.GitClient never runs 'git pull --rebase'. Pull always merges.");
        }

        if (string.Equals(verb, "submodule", StringComparison.OrdinalIgnoreCase) && IsRebasingSubmoduleUpdate(arguments))
        {
            throw new NotSupportedException(
                "Enigma.GitClient never runs 'git submodule update --rebase'. A submodule is always checked out.");
        }
    }

    private static bool IsRebaseEnablingConfiguration(string configuration)
    {
        int separator = configuration.IndexOf('=');
        string name = separator < 0 ? configuration : configuration.Substring(0, separator);

        // submodule.<name>.update=rebase makes "git submodule update" rebase the submodule's branch
        // onto the recorded commit.
        if (name.StartsWith("submodule.", StringComparison.OrdinalIgnoreCase) &&
            name.EndsWith(".update", StringComparison.OrdinalIgnoreCase))
        {
            return separator >= 0 &&
                   string.Equals(configuration.Substring(separator + 1).Trim(), "rebase", StringComparison.OrdinalIgnoreCase);
        }

        if (!string.Equals(name, "pull.rebase", StringComparison.OrdinalIgnoreCase) &&
            !name.EndsWith(".rebase", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // "-c pull.rebase" with no value means "true" to git; only an explicit false is allowed.
        string value = separator < 0 ? "true" : configuration.Substring(separator + 1);
        return !IsFalse(value);
    }

    private static bool IsFalse(string value)
        => value.Trim() is "false" or "0" or "no" or "off" or "";

    /// <summary>
    /// Tells whether a <c>git submodule</c> vector is an update in rebase mode.
    /// </summary>
    /// <remarks>
    /// The submodule's own sub-command is the first argument after <c>submodule</c> that is not an
    /// option (<c>git submodule --quiet update</c>); its options run up to <c>--</c>, after which
    /// everything is a path. <c>-r</c> is <c>--rebase</c>'s short form.
    /// </remarks>
    private static bool IsRebasingSubmoduleUpdate(IReadOnlyList<string> arguments)
    {
        int index = GitArgumentVector.FindVerbIndex(arguments) + 1;

        while (index < arguments.Count && arguments[index].StartsWith('-'))
        {
            index++;
        }

        if (index >= arguments.Count || !string.Equals(arguments[index], "update", StringComparison.Ordinal))
        {
            return false;
        }

        for (index++; index < arguments.Count; index++)
        {
            string argument = arguments[index];

            if (argument == "--")
            {
                return false;
            }

            if (string.Equals(argument, "--rebase", StringComparison.Ordinal) ||
                string.Equals(argument, "-r", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsRebaseFlag(IReadOnlyList<string> arguments)
    {
        foreach (string argument in arguments)
        {
            if (string.Equals(argument, "--rebase", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(argument, "-r", StringComparison.Ordinal))
            {
                return true;
            }

            if (argument.StartsWith("--rebase=", StringComparison.OrdinalIgnoreCase) &&
                !IsFalse(argument.Substring("--rebase=".Length)))
            {
                return true;
            }
        }

        return false;
    }
}
