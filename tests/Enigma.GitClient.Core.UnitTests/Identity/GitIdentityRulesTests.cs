using System;
using Enigma.GitClient.Core.Identity;
using Xunit;

namespace Enigma.GitClient.Core.UnitTests.Identity;

/// <summary>
/// Covers what a name and an email must look like before git is asked to store them, and how two
/// identities are compared.
/// </summary>
public sealed class GitIdentityRulesTests
{
    [Theory]
    [InlineData("Ada Lovelace")]
    [InlineData("  Ada Lovelace  ")]
    [InlineData("José Ñúñez")]
    [InlineData("-dash first")]
    public void ValidateName_AcceptsAnOrdinaryName(string name)
        => Assert.Null(GitIdentityRules.ValidateName(name));

    [Theory]
    [InlineData(null, "Enter a name.")]
    [InlineData("", "Enter a name.")]
    [InlineData("   ", "Enter a name.")]
    [InlineData("Ada <Lovelace>", "A name cannot contain < or >.")]
    [InlineData("Ada\nLovelace", "A name must fit on one line.")]
    [InlineData("Ada\rLovelace", "A name must fit on one line.")]
    [InlineData("Ada\0Lovelace", "A name must fit on one line.")]
    public void ValidateName_SaysWhatIsWrong(string? name, string expected)
        => Assert.Equal(expected, GitIdentityRules.ValidateName(name));

    [Fact]
    public void ValidateName_RefusesANameLongerThanTheLimit()
    {
        Assert.Null(GitIdentityRules.ValidateName(new string('a', GitIdentityRules.MaximumLength)));
        Assert.NotNull(GitIdentityRules.ValidateName(new string('a', GitIdentityRules.MaximumLength + 1)));
    }

    [Theory]
    [InlineData("ada@example.com")]
    [InlineData("  ada@example.com  ")]
    [InlineData("ada+git@localhost")]
    [InlineData("12345+ada@users.noreply.github.com")]
    [InlineData("\"odd@quoted\"@example.com")]
    public void ValidateEmail_AcceptsAnOrdinaryAddress(string email)
        => Assert.Null(GitIdentityRules.ValidateEmail(email));

    [Theory]
    [InlineData(null, "Enter an email.")]
    [InlineData("", "Enter an email.")]
    [InlineData("  ", "Enter an email.")]
    [InlineData("<ada@example.com>", "An email cannot contain < or >.")]
    [InlineData("ada@exa\nmple.com", "An email must fit on one line.")]
    [InlineData("ada lovelace@example.com", "An email cannot contain spaces.")]
    [InlineData("ada\t@example.com", "An email cannot contain spaces.")]
    [InlineData("ada.example.com", "An email needs something on both sides of an @.")]
    [InlineData("@example.com", "An email needs something on both sides of an @.")]
    [InlineData("ada@", "An email needs something on both sides of an @.")]
    public void ValidateEmail_SaysWhatIsWrong(string? email, string expected)
        => Assert.Equal(expected, GitIdentityRules.ValidateEmail(email));

    [Fact]
    public void ValidateEmail_RefusesAnAddressLongerThanTheLimit()
    {
        string local = new('a', GitIdentityRules.MaximumLength - "@x".Length);

        Assert.Null(GitIdentityRules.ValidateEmail(local + "@x"));
        Assert.NotNull(GitIdentityRules.ValidateEmail(local + "a@x"));
    }

    [Fact]
    public void Validate_ChecksTheNameBeforeTheEmail()
    {
        Assert.Equal("Enter a name.", GitIdentityRules.Validate(new GitIdentity("", "")));
        Assert.Equal("Enter an email.", GitIdentityRules.Validate(new GitIdentity("Ada", "")));
        Assert.Null(GitIdentityRules.Validate(new GitIdentity("Ada", "ada@example.com")));
    }

    [Fact]
    public void Validate_RefusesNothing()
        => Assert.Throws<ArgumentNullException>(() => GitIdentityRules.Validate(null!));

    [Fact]
    public void Identity_SaysWhetherItIsEmptyOrComplete()
    {
        Assert.True(GitIdentity.Empty.IsEmpty);
        Assert.False(GitIdentity.Empty.IsComplete);

        GitIdentity half = new("Ada", " ");
        Assert.False(half.IsEmpty);
        Assert.False(half.IsComplete);

        GitIdentity whole = new("Ada", "ada@example.com");
        Assert.False(whole.IsEmpty);
        Assert.True(whole.IsComplete);
    }

    [Fact]
    public void Normalised_TrimsBothValues()
        => Assert.Equal(
            new GitIdentity("Ada Lovelace", "ada@example.com"),
            new GitIdentity("  Ada Lovelace ", " ada@example.com  ").Normalised());

    [Fact]
    public void IsSameAs_ComparesTheNameExactlyAndTheEmailRegardlessOfCase()
    {
        GitIdentity ada = new("Ada Lovelace", "Ada@Example.com");

        Assert.True(ada.IsSameAs(new GitIdentity(" Ada Lovelace ", "ada@example.com")));
        Assert.False(ada.IsSameAs(new GitIdentity("ada lovelace", "ada@example.com")));
        Assert.False(ada.IsSameAs(new GitIdentity("Ada Lovelace", "ada@example.org")));
        Assert.False(ada.IsSameAs(null));
    }

    [Fact]
    public void ToString_WritesTheIdentityTheWayACommitDoes()
    {
        Assert.Equal("Ada Lovelace <ada@example.com>", new GitIdentity("Ada Lovelace", "ada@example.com").ToString());
        Assert.Equal(string.Empty, GitIdentity.Empty.ToString());
    }
}
