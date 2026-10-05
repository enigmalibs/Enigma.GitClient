using System;
using System.IO;
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
        Assert.Equal("Enter a name.", IdentityProfileRules.Validate(new IdentityProfile("x", "Work", "", "ada@example.com")));
        Assert.Null(IdentityProfileRules.Validate(new IdentityProfile("x", "Work", "Ada", "ada@example.com")));
        Assert.Throws<ArgumentNullException>(() => IdentityProfileRules.Validate(null!));
    }

    [Fact]
    public void Validate_AcceptsAProfileWithNoNameAndNoEmail()
    {
        Assert.Null(IdentityProfileRules.Validate(new IdentityProfile("x", "Default", "", "")));
        Assert.Null(IdentityProfileRules.Validate(new IdentityProfile("x", "Default", "  ", " ")));

        // The label is still required.
        Assert.NotNull(IdentityProfileRules.Validate(new IdentityProfile("x", "", "", "")));
    }

    [Theory]
    [InlineData("Ada", "", "Enter an email.")]
    [InlineData("", "ada@example.com", "Enter a name.")]
    [InlineData("Ada", "nope", "An email needs something on both sides of an @.")]
    public void ValidateIdentity_RefusesHalfAnIdentity(string name, string email, string expected)
        => Assert.Equal(expected, IdentityProfileRules.ValidateIdentity(new GitIdentity(name, email)));

    [Fact]
    public void ValidateIdentity_AcceptsNoneOrAWholeOne()
    {
        Assert.Null(IdentityProfileRules.ValidateIdentity(GitIdentity.Empty));
        Assert.Null(IdentityProfileRules.ValidateIdentity(new GitIdentity(" Ada ", " ada@example.com ")));
        Assert.Throws<ArgumentNullException>(() => IdentityProfileRules.ValidateIdentity(null!));
    }

    [Fact]
    public void AProfileWithoutAnIdentity_IsNeverTheOneGitHas()
    {
        IdentityProfile none = new("default", "Default", string.Empty, string.Empty);
        IdentityProfile work = IdentityProfile.Create("Work", new GitIdentity("Ada", "ada@work.example"));

        Assert.False(none.HasIdentity);
        Assert.True(work.HasIdentity);

        Assert.False(none.Matches(new GitIdentity("Ada", "ada@work.example")));
        Assert.False(none.Matches(GitIdentity.Empty));
        Assert.Same(work, IdentityProfile.FirstMatching([none, work], new GitIdentity("Ada", "ada@work.example")));
        Assert.Null(IdentityProfile.FirstMatching([none], GitIdentity.Empty));
    }

    // ---------------------------------------------------------------- the base directory

    [Fact]
    public void ABaseDirectory_IsNoneUntilOneIsGivenAndIsTrimmed()
    {
        string directory = Path.GetTempPath();
        IdentityProfile work = IdentityProfile.Create("Work", new GitIdentity("Ada", "ada@work.example"));

        Assert.Equal(string.Empty, work.BaseDirectory);
        Assert.Equal(string.Empty, IdentityProfile.CreateDefault().BaseDirectory);

        IdentityProfile placed = work.WithBaseDirectory($"  {directory}  ");

        Assert.Equal(directory.Trim(), placed.BaseDirectory);
        Assert.Equal(work.Id, placed.Id);
        Assert.Equal(string.Empty, placed.WithBaseDirectory(null).BaseDirectory);

        // Changing the label and identity keeps it, trimmed as everything else is.
        IdentityProfile renamed = (placed with { BaseDirectory = $" {directory} " }).With("Office", work.Identity);
        Assert.Equal(directory.Trim(), renamed.BaseDirectory);
    }

    [Fact]
    public void ValidateBaseDirectory_AcceptsNoneOrAFullPath()
    {
        Assert.Null(IdentityProfileRules.ValidateBaseDirectory(null));
        Assert.Null(IdentityProfileRules.ValidateBaseDirectory(string.Empty));
        Assert.Null(IdentityProfileRules.ValidateBaseDirectory("   "));
        Assert.Null(IdentityProfileRules.ValidateBaseDirectory(Path.GetTempPath()));

        // It need not exist: a drive may be unplugged while the profile is edited.
        Assert.Null(IdentityProfileRules.ValidateBaseDirectory(Path.Combine(Path.GetTempPath(), "not-there-" + Guid.NewGuid().ToString("N"))));
    }

    [Theory]
    [InlineData("projects/work", "Give the base directory as a full path, from the root of the drive.")]
    [InlineData("./work", "Give the base directory as a full path, from the root of the drive.")]
    [InlineData("work\nmore", "A base directory must fit on one line.")]
    public void ValidateBaseDirectory_SaysWhatIsWrong(string directory, string expected)
        => Assert.Equal(expected, IdentityProfileRules.ValidateBaseDirectory(directory));

    [Fact]
    public void Validate_ChecksTheBaseDirectoryLast()
    {
        IdentityProfile work = IdentityProfile.Create("Work", new GitIdentity("Ada", "ada@work.example"));

        Assert.Null(IdentityProfileRules.Validate(work.WithBaseDirectory(Path.GetTempPath())));
        Assert.Equal(
            "Give the base directory as a full path, from the root of the drive.",
            IdentityProfileRules.Validate(work.WithBaseDirectory("relative")));
        Assert.Equal(
            "Give the profile a name, such as Work or Personal.",
            IdentityProfileRules.Validate((work with { Label = string.Empty }).WithBaseDirectory("relative")));
    }
}
