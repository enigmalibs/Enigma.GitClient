using System;
using System.Collections.Generic;

namespace Enigma.GitClient.Core.Git;

/// <summary>
/// Helpers for reasoning about a git argument vector — in particular, telling git's own global
/// options apart from the sub-command and its arguments.
/// </summary>
public static class GitArgumentVector
{
    /// <summary>
    /// git's global options that take their value as the following, separate argument.
    /// </summary>
    private static readonly HashSet<string> OptionsWithSeparateValue = new(StringComparer.Ordinal)
    {
        "-C",
        "-c",
        "--git-dir",
        "--work-tree",
        "--namespace",
        "--exec-path",
        "--super-prefix",
        "--config-env",
    };

    /// <summary>
    /// git's global flags that take no value.
    /// </summary>
    private static readonly HashSet<string> StandaloneOptions = new(StringComparer.Ordinal)
    {
        "-p",
        "--paginate",
        "-P",
        "--no-pager",
        "--no-replace-objects",
        "--bare",
        "--literal-pathspecs",
        "--glob-pathspecs",
        "--noglob-pathspecs",
        "--icase-pathspecs",
        "--no-optional-locks",
        "--no-lazy-fetch",
    };

    /// <summary>
    /// Finds the sub-command in an argument vector, skipping git's global options.
    /// </summary>
    /// <param name="arguments">The argument vector.</param>
    /// <returns>The sub-command, or <see langword="null"/> when the vector holds only options.</returns>
    public static string? FindVerb(IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        for (int index = 0; index < arguments.Count; index++)
        {
            string argument = arguments[index];

            if (argument.Length == 0)
            {
                continue;
            }

            if (argument[0] != '-')
            {
                return argument;
            }

            if (OptionsWithSeparateValue.Contains(argument))
            {
                // Skip the option's value as well.
                index++;
                continue;
            }

            // Everything else is either "--option=value" or a standalone flag; both are one
            // argument, so the loop simply moves on.
            _ = StandaloneOptions.Contains(argument);
        }

        return null;
    }

    /// <summary>
    /// Enumerates the <c>-c name=value</c> configuration overrides present in a vector.
    /// </summary>
    /// <param name="arguments">The argument vector.</param>
    /// <returns>The <c>name=value</c> pairs, in order.</returns>
    public static IEnumerable<string> EnumerateConfigOverrides(IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        for (int index = 0; index < arguments.Count - 1; index++)
        {
            if (string.Equals(arguments[index], "-c", StringComparison.Ordinal))
            {
                yield return arguments[index + 1];
                index++;
            }
        }
    }
}
