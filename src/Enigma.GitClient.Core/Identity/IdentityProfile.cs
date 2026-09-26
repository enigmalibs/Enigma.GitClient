using System;
using System.Text.Json.Serialization;

namespace Enigma.GitClient.Core.Identity;

/// <summary>
/// A name and an email kept under a label ("Work", "Personal"), so the global identity can be switched
/// to it in one step.
/// </summary>
/// <remarks>
/// A profile is the client's convenience, not git's: git never sees one, only the identity it is
/// used to write.
/// </remarks>
/// <param name="Id">A stable identifier, so a profile can be edited without losing its place.</param>
/// <param name="Label">What the profile is called.</param>
/// <param name="Name">The name it sets.</param>
/// <param name="Email">The email it sets.</param>
public sealed record IdentityProfile(string Id, string Label, string Name, string Email)
{
    /// <summary>Gets the identity the profile sets.</summary>
    [JsonIgnore]
    public GitIdentity Identity => new(Name, Email);

    /// <summary>
    /// Builds a new profile with a fresh identifier and trimmed values.
    /// </summary>
    /// <param name="label">What the profile is called.</param>
    /// <param name="identity">The identity it sets.</param>
    /// <returns>The profile.</returns>
    public static IdentityProfile Create(string label, GitIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);

        return new IdentityProfile(Guid.NewGuid().ToString("N"), string.Empty, string.Empty, string.Empty)
            .With(label, identity);
    }

    /// <summary>
    /// Returns this profile with new values and the same identifier.
    /// </summary>
    /// <param name="label">What the profile is called.</param>
    /// <param name="identity">The identity it sets.</param>
    /// <returns>The changed profile, trimmed.</returns>
    public IdentityProfile With(string label, GitIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);

        GitIdentity trimmed = identity.Normalised();

        return this with { Label = label?.Trim() ?? string.Empty, Name = trimmed.Name, Email = trimmed.Email };
    }

    /// <summary>
    /// Tells whether this profile is the identity git has — which is what makes it the current one.
    /// </summary>
    /// <param name="identity">The identity git reported.</param>
    /// <returns><see langword="true"/> when the names are equal and the emails equal regardless of case.</returns>
    public bool Matches(GitIdentity? identity) => identity is { IsComplete: true } && Identity.IsSameAs(identity);
}

/// <summary>
/// What a profile's label must look like.
/// </summary>
public static class IdentityProfileRules
{
    /// <summary>The longest label accepted, in characters.</summary>
    public const int MaximumLabelLength = 100;

    /// <summary>
    /// Checks a label.
    /// </summary>
    /// <param name="label">The label, as typed.</param>
    /// <returns>A sentence saying what is wrong, or <see langword="null"/> when the label is usable.</returns>
    public static string? ValidateLabel(string? label)
    {
        string value = label?.Trim() ?? string.Empty;

        if (value.Length == 0)
        {
            return "Give the profile a name, such as Work or Personal.";
        }

        if (value.Length > MaximumLabelLength)
        {
            return $"A profile's name can be at most {MaximumLabelLength} characters long.";
        }

        if (value.AsSpan().IndexOfAny('\r', '\n') >= 0)
        {
            return "A profile's name must fit on one line.";
        }

        return null;
    }

    /// <summary>
    /// Checks a whole profile: the label, then the name, then the email.
    /// </summary>
    /// <param name="profile">The profile.</param>
    /// <returns>A sentence saying what is wrong, or <see langword="null"/> when it is usable.</returns>
    public static string? Validate(IdentityProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        return ValidateLabel(profile.Label) ?? GitIdentityRules.Validate(profile.Identity.Normalised());
    }
}
