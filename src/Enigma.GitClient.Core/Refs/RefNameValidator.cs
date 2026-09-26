using System;
using System.Globalization;

namespace Enigma.GitClient.Core.Refs;

/// <summary>
/// The verdict on a proposed reference name.
/// </summary>
/// <param name="IsValid">Whether git would accept the name.</param>
/// <param name="Message">
/// What is wrong with it, naming the rule it breaks. Empty when the name is valid.
/// </param>
public readonly record struct RefNameValidation(bool IsValid, string Message)
{
    /// <summary>
    /// A name git would accept.
    /// </summary>
    public static readonly RefNameValidation Valid = new(true, string.Empty);

    /// <summary>
    /// Builds a rejection.
    /// </summary>
    /// <param name="message">What is wrong with the name.</param>
    /// <returns>The verdict.</returns>
    public static RefNameValidation Invalid(string message) => new(false, message);
}

/// <summary>
/// Checks a branch or tag name against git's own rules, before any command is run.
/// </summary>
/// <remarks>
/// <para>
/// git is the authority here — <c>git check-ref-format</c> is what the write commands will be
/// judged by — but it can only answer after a process has started, and it answers with a single
/// terse line. This check runs on every keystroke and says which rule the name breaks, which is the
/// difference between a dialog that teaches and one that just refuses.
/// </para>
/// <para>
/// The rules are <c>git-check-ref-format</c>'s, applied to each path component in turn, because a
/// branch name may legitimately contain <c>/</c>.
/// </para>
/// </remarks>
public static class RefNameValidator
{
    /// <summary>
    /// The characters git never allows anywhere in a reference name.
    /// </summary>
    public const string ForbiddenCharacters = "~^:?*[\\";

    /// <summary>
    /// Validates a branch name.
    /// </summary>
    /// <param name="name">The proposed name, without <c>refs/heads/</c>.</param>
    /// <returns>The verdict.</returns>
    public static RefNameValidation ValidateBranch(string? name) => Validate(name, "branch");

    /// <summary>
    /// Validates a tag name.
    /// </summary>
    /// <param name="name">The proposed name, without <c>refs/tags/</c>.</param>
    /// <returns>The verdict.</returns>
    public static RefNameValidation ValidateTag(string? name) => Validate(name, "tag");

    /// <summary>
    /// Validates a reference name of the given kind.
    /// </summary>
    /// <param name="name">The proposed name.</param>
    /// <param name="kind">What the name is for, used in the message.</param>
    /// <returns>The verdict.</returns>
    public static RefNameValidation Validate(string? name, string kind)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);

        if (string.IsNullOrEmpty(name))
        {
            return RefNameValidation.Invalid($"Enter a {kind} name.");
        }

        if (name.Trim().Length != name.Length)
        {
            return RefNameValidation.Invalid($"A {kind} name cannot begin or end with a space.");
        }

        if (name[0] == '-')
        {
            return RefNameValidation.Invalid($"A {kind} name cannot begin with a dash.");
        }

        if (string.Equals(name, "@", StringComparison.Ordinal))
        {
            return RefNameValidation.Invalid($"\"@\" on its own is not a valid {kind} name.");
        }

        if (name.Contains("..", StringComparison.Ordinal))
        {
            return RefNameValidation.Invalid($"A {kind} name cannot contain \"..\".");
        }

        if (name.Contains("@{", StringComparison.Ordinal))
        {
            return RefNameValidation.Invalid($"A {kind} name cannot contain \"@{{\".");
        }

        if (name.Contains("//", StringComparison.Ordinal))
        {
            return RefNameValidation.Invalid($"A {kind} name cannot contain an empty path component.");
        }

        if (name[0] == '/' || name[^1] == '/')
        {
            return RefNameValidation.Invalid($"A {kind} name cannot begin or end with \"/\".");
        }

        if (name[^1] == '.')
        {
            return RefNameValidation.Invalid($"A {kind} name cannot end with a dot.");
        }

        foreach (char character in name)
        {
            // char.IsControl covers both the C0 range and DEL, which are the bytes git rejects.
            if (char.IsControl(character))
            {
                return RefNameValidation.Invalid($"A {kind} name cannot contain control characters.");
            }

            if (character == ' ')
            {
                return RefNameValidation.Invalid($"A {kind} name cannot contain spaces.");
            }

            if (ForbiddenCharacters.Contains(character, StringComparison.Ordinal))
            {
                return RefNameValidation.Invalid(
                    string.Create(
                        CultureInfo.CurrentCulture,
                        $"A {kind} name cannot contain \"{character}\"."));
            }
        }

        foreach (string component in name.Split('/'))
        {
            if (component.Length == 0)
            {
                return RefNameValidation.Invalid($"A {kind} name cannot contain an empty path component.");
            }

            if (component[0] == '.')
            {
                return RefNameValidation.Invalid($"No part of a {kind} name may begin with a dot.");
            }

            if (component.EndsWith(".lock", StringComparison.Ordinal))
            {
                return RefNameValidation.Invalid($"No part of a {kind} name may end with \".lock\".");
            }
        }

        return RefNameValidation.Valid;
    }
}
