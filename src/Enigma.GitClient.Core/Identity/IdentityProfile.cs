using System;
using System.Collections.Generic;
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
    /// Gets a value indicating whether the profile sets an identity at all.
    /// </summary>
    /// <remarks>
    /// A profile without one is legal: git never sees it. It never matches the identity git has, so it
    /// is never the current profile, never decides a push and never signs git in — which is what lets
    /// the client create one on the user's behalf without changing anything they had.
    /// </remarks>
    [JsonIgnore]
    public bool HasIdentity => !Identity.IsEmpty;

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

    /// <summary>
    /// Finds the profile an identity is: the first that matches it, as the Profiles page marks the first
    /// one current.
    /// </summary>
    /// <param name="profiles">Every profile, in the order they were added.</param>
    /// <param name="identity">The identity git reported.</param>
    /// <returns>The profile, or <see langword="null"/> when none matches.</returns>
    public static IdentityProfile? FirstMatching(IEnumerable<IdentityProfile> profiles, GitIdentity? identity)
    {
        ArgumentNullException.ThrowIfNull(profiles);

        foreach (IdentityProfile candidate in profiles)
        {
            if (candidate.Matches(identity))
            {
                return candidate;
            }
        }

        return null;
    }
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
    /// Checks the identity a profile sets: none at all, or a whole one.
    /// </summary>
    /// <param name="identity">The name and email, as typed.</param>
    /// <returns>A sentence saying what is wrong, or <see langword="null"/> when it is usable.</returns>
    /// <remarks>
    /// Neither value is a profile that git never sees (<see cref="IdentityProfile.HasIdentity"/>). One of
    /// them without the other is refused: git would write half an identity.
    /// </remarks>
    public static string? ValidateIdentity(GitIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);

        GitIdentity normalised = identity.Normalised();

        return normalised.IsEmpty ? null : GitIdentityRules.Validate(normalised);
    }

    /// <summary>
    /// Checks a whole profile: the label, then the name, then the email.
    /// </summary>
    /// <param name="profile">The profile.</param>
    /// <returns>A sentence saying what is wrong, or <see langword="null"/> when it is usable.</returns>
    public static string? Validate(IdentityProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        return ValidateLabel(profile.Label) ?? ValidateIdentity(profile.Identity);
    }
}
