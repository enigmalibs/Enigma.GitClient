using System;
using Enigma.GitClient.Core.Diff;
using Xunit;

namespace Enigma.GitClient.Core.UnitTests.Diff;

public sealed class GitPathQuotingTests
{
    [Theory]
    [InlineData("plain.txt", "plain.txt")]
    [InlineData("a file with spaces.txt", "a file with spaces.txt")]
    [InlineData("\"quoted.txt\"", "quoted.txt")]
    [InlineData("\"weird\\\"name.txt\"", "weird\"name.txt")]
    [InlineData("\"with\\\\backslash.txt\"", "with\\backslash.txt")]
    [InlineData("\"tab\\there.txt\"", "tab\there.txt")]
    [InlineData("\"newline\\nhere.txt\"", "newline\nhere.txt")]
    public void Unquote_RemovesGitsQuoting(string value, string expected)
        => Assert.Equal(expected, GitPathQuoting.Unquote(value));

    [Fact]
    public void Unquote_DecodesOctalEscapesAsUtf8Bytes()
    {
        // git writes "rapport-écrit.txt" as octal byte escapes when core.quotePath is on. The two
        // escapes below are the UTF-8 encoding of a single 'é', so they must be decoded together.
        Assert.Equal("rapport-écrit.txt", GitPathQuoting.Unquote("\"rapport-\\303\\251crit.txt\""));
    }

    [Fact]
    public void Unquote_DecodesAMultiCharacterNonAsciiPath()
        => Assert.Equal(
            "dossier/fonctionnalité.txt",
            GitPathQuoting.Unquote("\"dossier/fonctionnalit\\303\\251.txt\""));

    [Fact]
    public void Unquote_LeavesAnUnknownEscapeIntact()
        => Assert.Equal("weird\\qescape", GitPathQuoting.Unquote("\"weird\\qescape\""));

    [Fact]
    public void Unquote_LeavesAnUnterminatedQuoteAlone()
        => Assert.Equal("\"unterminated", GitPathQuoting.Unquote("\"unterminated"));

    [Theory]
    [InlineData("a/src/file.cs", "src/file.cs")]
    [InlineData("b/src/file.cs", "src/file.cs")]
    [InlineData("/dev/null", null)]
    [InlineData("\"a/weird\\\"name.txt\"", "weird\"name.txt")]
    [InlineData("src/file.cs", "src/file.cs")]
    public void UnquoteAndStripPrefix_HandlesEveryPatchPathShape(string value, string? expected)
        => Assert.Equal(expected, GitPathQuoting.UnquoteAndStripPrefix(value));

    [Fact]
    public void UnquoteAndStripPrefix_TrimsSurroundingWhitespace()
        => Assert.Equal("src/file.cs", GitPathQuoting.UnquoteAndStripPrefix("  a/src/file.cs  "));

    [Fact]
    public void Unquote_RejectsNull()
        => Assert.Throws<ArgumentNullException>(() => GitPathQuoting.Unquote(null!));
}
