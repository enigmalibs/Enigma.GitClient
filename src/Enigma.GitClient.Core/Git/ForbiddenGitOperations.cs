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
    }

    private static bool IsRebaseEnablingConfiguration(string configuration)
    {
        int separator = configuration.IndexOf('=');
        string name = separator < 0 ? configuration : configuration.Substring(0, separator);

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
