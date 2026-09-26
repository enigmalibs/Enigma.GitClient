using System;

namespace Enigma.GitClient.Core.Identity;

/// <summary>
/// The name and email git records as the author and the committer of a commit — what
/// <c>user.name</c> and <c>user.email</c> hold in one configuration scope.
/// </summary>
/// <param name="Name">The person's name, empty when the scope does not set it.</param>
/// <param name="Email">The person's email, empty when the scope does not set it.</param>
public sealed record GitIdentity(string Name, string Email)
{
    /// <summary>An identity that sets neither value.</summary>
    public static readonly GitIdentity Empty = new(string.Empty, string.Empty);

    /// <summary>Gets a value indicating whether neither the name nor the email is set.</summary>
    public bool IsEmpty => string.IsNullOrWhiteSpace(Name) && string.IsNullOrWhiteSpace(Email);

    /// <summary>Gets a value indicating whether both the name and the email are set.</summary>
    public bool IsComplete => !string.IsNullOrWhiteSpace(Name) && !string.IsNullOrWhiteSpace(Email);

    /// <summary>
    /// Returns the identity with both values trimmed, which is what is compared and what is written.
    /// </summary>
    /// <returns>The trimmed identity.</returns>
    public GitIdentity Normalised() => new(Name?.Trim() ?? string.Empty, Email?.Trim() ?? string.Empty);

    /// <summary>
    /// Tells whether two identities name the same person the way git would write them: the names
    /// exactly, the emails regardless of case, both trimmed.
    /// </summary>
    /// <param name="other">The identity to compare with.</param>
    /// <returns><see langword="true"/> when they are the same.</returns>
    public bool IsSameAs(GitIdentity? other)
    {
        if (other is null)
        {
            return false;
        }

        GitIdentity left = Normalised();
        GitIdentity right = other.Normalised();

        return string.Equals(left.Name, right.Name, StringComparison.Ordinal)
            && string.Equals(left.Email, right.Email, StringComparison.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public override string ToString() => IsEmpty ? string.Empty : $"{Name} <{Email}>";
}

/// <summary>
/// What a name and an email must look like before they are handed to git.
/// </summary>
/// <remarks>
/// <para>
/// git refuses angle brackets and line breaks in an identity when it writes a commit, long after the
/// configuration accepted them; checking here turns that late, cryptic failure into a sentence next
/// to the field. The email rule only asks for text on both sides of an <c>@</c>: it catches the typo
/// that matters without policing what a real address may look like.
/// </para>
/// <para>
/// Every rule reads the value trimmed, because the trimmed value is what is written.
/// </para>
/// </remarks>
public static class GitIdentityRules
{
    /// <summary>The longest name or email accepted, in characters.</summary>
    public const int MaximumLength = 256;

    /// <summary>
    /// Checks a name.
    /// </summary>
    /// <param name="name">The name, as typed.</param>
    /// <returns>A sentence saying what is wrong, or <see langword="null"/> when the name is usable.</returns>
    public static string? ValidateName(string? name)
    {
        string value = name?.Trim() ?? string.Empty;

        if (value.Length == 0)
        {
            return "Enter a name.";
        }

        if (value.Length > MaximumLength)
        {
            return $"A name can be at most {MaximumLength} characters long.";
        }

        if (value.AsSpan().IndexOfAny('<', '>') >= 0)
        {
            return "A name cannot contain < or >.";
        }

        if (ContainsLineBreak(value))
        {
            return "A name must fit on one line.";
        }

        return null;
    }

    /// <summary>
    /// Checks an email.
    /// </summary>
    /// <param name="email">The email, as typed.</param>
    /// <returns>A sentence saying what is wrong, or <see langword="null"/> when the email is usable.</returns>
    public static string? ValidateEmail(string? email)
    {
        string value = email?.Trim() ?? string.Empty;

        if (value.Length == 0)
        {
            return "Enter an email.";
        }

        if (value.Length > MaximumLength)
        {
            return $"An email can be at most {MaximumLength} characters long.";
        }

        if (value.AsSpan().IndexOfAny('<', '>') >= 0)
        {
            return "An email cannot contain < or >.";
        }

        if (ContainsLineBreak(value))
        {
            return "An email must fit on one line.";
        }

        foreach (char character in value)
        {
            if (char.IsWhiteSpace(character))
            {
                return "An email cannot contain spaces.";
            }
        }

        int at = value.LastIndexOf('@');

        if (at <= 0 || at == value.Length - 1)
        {
            return "An email needs something on both sides of an @.";
        }

        return null;
    }

    /// <summary>
    /// Checks a whole identity: the name first, then the email.
    /// </summary>
    /// <param name="identity">The identity, as typed.</param>
    /// <returns>A sentence saying what is wrong, or <see langword="null"/> when both values are usable.</returns>
    public static string? Validate(GitIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);

        return ValidateName(identity.Name) ?? ValidateEmail(identity.Email);
    }

    private static bool ContainsLineBreak(string value)
        => value.AsSpan().IndexOfAny('\r', '\n') >= 0 || value.Contains('\0', StringComparison.Ordinal);
}
