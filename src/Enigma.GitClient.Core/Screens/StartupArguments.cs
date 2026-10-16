using System;
using System.Collections.Generic;
using System.Globalization;

namespace Enigma.GitClient.Core.Screens;

/// <summary>
/// What the application is started with: a repository to open, and the screen to open on.
/// </summary>
/// <param name="RepositoryPath">The repository to open, or <see langword="null"/> for the start window.</param>
/// <param name="Screen">
/// The bounds of the screen to open on — the one the window that started this instance was on — or
/// <see langword="null"/> for the screen the application was last used on.
/// </param>
/// <remarks>
/// <para>
/// <c>[--screen=x,y,width,height] [--] [path]</c>. The first argument that is not an option is the path,
/// and <c>--</c> ends the options, so a path starting with a dash still opens. An option this build does
/// not know, or a screen it cannot read, is ignored: a command line typed by hand opens what it names
/// whatever else is on it.
/// </para>
/// <para>
/// The screen travels as four integers in the invariant culture, so an instance started on a machine
/// whose culture writes numbers differently reads them the same.
/// </para>
/// </remarks>
public sealed record StartupArguments(string? RepositoryPath, ScreenArea? Screen)
{
    /// <summary>What the screen option starts with.</summary>
    public const string ScreenOption = "--screen=";

    /// <summary>What ends the options.</summary>
    public const string EndOfOptions = "--";

    /// <summary>
    /// Reads a command line.
    /// </summary>
    /// <param name="args">The arguments, as the process was given them; <see langword="null"/> for none.</param>
    /// <returns>The repository path and the screen it names, each <see langword="null"/> when it names none.</returns>
    public static StartupArguments Parse(IReadOnlyList<string>? args)
    {
        if (args is null)
        {
            return new StartupArguments(null, null);
        }

        string? path = null;
        ScreenArea? screen = null;
        bool options = true;

        foreach (string argument in args)
        {
            if (string.IsNullOrWhiteSpace(argument))
            {
                continue;
            }

            if (options && argument == EndOfOptions)
            {
                options = false;
                continue;
            }

            if (options && argument.StartsWith('-'))
            {
                if (argument.StartsWith(ScreenOption, StringComparison.Ordinal)
                    && TryParseScreen(argument[ScreenOption.Length..]) is { } parsed)
                {
                    screen = parsed;
                }

                continue;
            }

            // The first argument that is not an option; the ones after it are left alone.
            path ??= argument.Trim();
        }

        return new StartupArguments(path, screen);
    }

    /// <summary>
    /// Writes the option that names a screen.
    /// </summary>
    /// <param name="screen">The screen's bounds.</param>
    /// <returns><c>--screen=x,y,width,height</c>, in the invariant culture.</returns>
    public static string FormatScreen(ScreenArea screen)
        => string.Create(
            CultureInfo.InvariantCulture,
            $"{ScreenOption}{screen.X},{screen.Y},{screen.Width},{screen.Height}");

    /// <summary>
    /// Reads <c>x,y,width,height</c>: four invariant-culture integers, the last two positive.
    /// </summary>
    private static ScreenArea? TryParseScreen(string value)
    {
        string[] parts = value.Split(',');

        if (parts.Length != 4)
        {
            return null;
        }

        int[] numbers = new int[4];

        for (int index = 0; index < parts.Length; index++)
        {
            if (!int.TryParse(parts[index].Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out numbers[index]))
            {
                return null;
            }
        }

        ScreenArea screen = new(numbers[0], numbers[1], numbers[2], numbers[3]);

        return screen.IsEmpty ? null : screen;
    }
}
