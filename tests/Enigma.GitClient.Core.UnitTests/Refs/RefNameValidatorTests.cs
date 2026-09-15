using System;
using Enigma.GitClient.Core.Branches;
using Enigma.GitClient.Core.Refs;
using Xunit;

namespace Enigma.GitClient.Core.UnitTests.Refs;

/// <summary>
/// Covers the branch- and tag-name rules the dialogs check on every keystroke.
/// </summary>
/// <remarks>
/// git itself is the authority, and the write commands still go through it. What is pinned here is
/// that the client agrees with git on every rule and, when it refuses, says which rule was broken —
/// a validator that rejects a good name is as bad as one that accepts a bad one.
/// </remarks>
public sealed class RefNameValidatorTests
{
    [Theory]
    [InlineData("main")]
    [InlineData("feature/login")]
    [InlineData("release/2026.09")]
    [InlineData("bugfix/issue-1234")]
    [InlineData("a")]
    [InlineData("user/josue/work")]
    [InlineData("v1.2.3")]
    [InlineData("fix.something")]
    [InlineData("WIP_things")]
    [InlineData("branch-with-dash")]
    public void Validate_AcceptsANameGitWouldTake(string name)
    {
        RefNameValidation validation = RefNameValidator.ValidateBranch(name);

        Assert.True(validation.IsValid, $"\"{name}\" should be valid but was rejected: {validation.Message}");
        Assert.Equal(string.Empty, validation.Message);
    }

    [Theory]
    [InlineData("", "Enter a branch name")]
    [InlineData("a..b", "\"..\"")]
    [InlineData("feature/", "\"/\"")]
    [InlineData("/feature", "\"/\"")]
    [InlineData("-lead", "dash")]
    [InlineData("has space", "spaces")]
    [InlineData(" leading", "space")]
    [InlineData("trailing ", "space")]
    [InlineData("tilde~here", "\"~\"")]
    [InlineData("caret^here", "\"^\"")]
    [InlineData("colon:here", "\":\"")]
    [InlineData("question?here", "\"?\"")]
    [InlineData("star*here", "\"*\"")]
    [InlineData("bracket[here", "\"[\"")]
    [InlineData("back\\slash", "\"\\\"")]
    [InlineData("at@{brace", "\"@{\"")]
    [InlineData("@", "\"@\"")]
    [InlineData("ends.", "dot")]
    [InlineData(".hidden", "dot")]
    [InlineData("feature/.hidden", "dot")]
    [InlineData("name.lock", "\".lock\"")]
    [InlineData("feature/name.lock", "\".lock\"")]
    [InlineData("double//slash", "empty path component")]
    public void Validate_RejectsANameGitWouldNot(string name, string expectedFragment)
    {
        RefNameValidation validation = RefNameValidator.ValidateBranch(name);

        Assert.False(validation.IsValid, $"\"{name}\" should have been rejected");

        // The message has to name the rule, not just say no: a dialog that only says "invalid"
        // leaves the user guessing which of a dozen rules they broke.
        Assert.Contains(expectedFragment, validation.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_RejectsAControlCharacter()
    {
        RefNameValidation validation = RefNameValidator.ValidateBranch("badname");

        Assert.False(validation.IsValid);
        Assert.Contains("control characters", validation.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_RejectsDelete()
    {
        // DEL is a control character to git too, even though it sits above the C0 range.
        Assert.False(RefNameValidator.ValidateBranch("badname").IsValid);
    }

    [Fact]
    public void Validate_NamesTheKindItWasAskedAbout()
    {
        Assert.Contains("branch", RefNameValidator.ValidateBranch("a..b").Message, StringComparison.Ordinal);
        Assert.Contains("tag", RefNameValidator.ValidateTag("a..b").Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_TreatsNullAsEmpty()
        => Assert.False(RefNameValidator.ValidateBranch(null).IsValid);

    [Fact]
    public void Validate_RequiresAKind()
        => Assert.Throws<ArgumentException>(() => RefNameValidator.Validate("main", " "));

    [Theory]
    [InlineData("origin/main", "main")]
    [InlineData("origin/feature/login", "feature/login")]
    [InlineData("upstream/release/2026.09", "release/2026.09")]
    [InlineData("main", "main")]
    [InlineData("origin/", "origin/")]
    public void LocalNameFor_DropsTheRemoteAndKeepsTheRest(string remoteBranch, string expected)
        => Assert.Equal(expected, BranchService.LocalNameFor(remoteBranch));

    [Fact]
    public void LocalNameFor_RequiresAName()
        => Assert.Throws<ArgumentException>(() => BranchService.LocalNameFor(" "));
}
