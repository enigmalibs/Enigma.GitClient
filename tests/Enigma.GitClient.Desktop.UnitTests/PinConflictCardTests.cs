using System;
using Enigma.GitClient.Core.Submodules;
using Enigma.GitClient.Desktop.ViewModels.Pages;
using Xunit;

namespace Enigma.GitClient.Desktop.UnitTests;

/// <summary>
/// A pin conflict's cards on the conflict page: the commit with its title, author and date, or the hash
/// alone when the submodule cannot say more.
/// </summary>
public sealed class PinConflictCardTests
{
    private const string Sha = "f00ba12345678901234567890123456789abcdef";

    [Fact]
    public void ACard_NamesTheCommit_ItsAuthorAndItsDate()
        => Assert.Equal(
            "Ours: f00ba12 — Fix X\nAda, 2026-11-01",
            ConflictResolutionPageViewModel.DescribePinSide(
                "Ours",
                Sha,
                new PinCommit("f00ba12", "Fix X", "Ada", new DateTimeOffset(2026, 11, 1, 9, 0, 0, TimeSpan.Zero))));

    [Fact]
    public void ACard_IsTheHashAlone_WhenTheSubmoduleCannotSayMore()
        => Assert.Equal("Theirs: f00ba12", ConflictResolutionPageViewModel.DescribePinSide("Theirs", Sha, commit: null));
}
