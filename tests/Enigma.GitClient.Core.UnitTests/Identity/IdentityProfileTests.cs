using System;
using Enigma.GitClient.Core.Identity;
using Xunit;

namespace Enigma.GitClient.Core.UnitTests.Identity;

/// <summary>
/// A profile: how one is made, changed, checked, and recognised as the identity git has.
/// </summary>
public sealed class IdentityProfileTests
{
    [Fact]
    public void Create_GivesAFreshIdentifierAndTrimsEverything()
    {
        IdentityProfile first = IdentityProfile.Create(" Work ", new GitIdentity(" Ada ", " ada@example.com "));
        IdentityProfile second = IdentityProfile.Create("Work", new GitIdentity("Ada", "ada@example.com"));

        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal(32, first.Id.Length);
        Assert.Equal("Work", first.Label);
        Assert.Equal(new GitIdentity("Ada", "ada@example.com"), first.Identity);
    }

    [Fact]
    public void With_KeepsTheIdentifier()
    {
        IdentityProfile profile = IdentityProfile.Create("Work", new GitIdentity("Ada", "ada@example.com"));

        IdentityProfile changed = profile.With("Office", new GitIdentity("Ada L.", "ada@office.example"));

        Assert.Equal(profile.Id, changed.Id);
        Assert.Equal("Office", changed.Label);
        Assert.Equal(new GitIdentity("Ada L.", "ada@office.example"), changed.Identity);
    }

    [Fact]
    public void Matches_TheIdentityGitHasRegardlessOfTheEmailsCase()
    {
        IdentityProfile profile = IdentityProfile.Create("Work", new GitIdentity("Ada Lovelace", "Ada@Work.example"));

        Assert.True(profile.Matches(new GitIdentity("Ada Lovelace", "ada@work.example")));
        Assert.False(profile.Matches(new GitIdentity("Ada", "ada@work.example")));
        Assert.False(profile.Matches(GitIdentity.Empty));
        Assert.False(profile.Matches(null));
    }

    [Fact]
    public void Matches_NeverAnIncompleteIdentity()
    {
        // A profile saved before the store checked it, with no email, must not claim an identity
        // git does not have either.
        IdentityProfile broken = new("x", "Broken", "Ada", string.Empty);

        Assert.False(broken.Matches(new GitIdentity("Ada", string.Empty)));
    }

    [Theory]
    [InlineData(null, "Give the profile a name, such as Work or Personal.")]
    [InlineData("  ", "Give the profile a name, such as Work or Personal.")]
    [InlineData("Work\nHome", "A profile's name must fit on one line.")]
    public void ValidateLabel_SaysWhatIsWrong(string? label, string expected)
        => Assert.Equal(expected, IdentityProfileRules.ValidateLabel(label));

    [Fact]
    public void ValidateLabel_AcceptsAnOrdinaryLabelUpToTheLimit()
    {
        Assert.Null(IdentityProfileRules.ValidateLabel("Work"));
        Assert.Null(IdentityProfileRules.ValidateLabel(new string('w', IdentityProfileRules.MaximumLabelLength)));
        Assert.NotNull(IdentityProfileRules.ValidateLabel(new string('w', IdentityProfileRules.MaximumLabelLength + 1)));
    }

    [Fact]
    public void Validate_ChecksTheLabelThenTheIdentity()
    {
        Assert.Equal(
            "Give the profile a name, such as Work or Personal.",
            IdentityProfileRules.Validate(new IdentityProfile("x", "", "", "")));
        Assert.Equal("Enter a name.", IdentityProfileRules.Validate(new IdentityProfile("x", "Work", "", "")));
        Assert.Null(IdentityProfileRules.Validate(new IdentityProfile("x", "Work", "Ada", "ada@example.com")));
        Assert.Throws<ArgumentNullException>(() => IdentityProfileRules.Validate(null!));
    }
}
