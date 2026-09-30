using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Enigma.GitClient.Core.Diagnostics;
using Xunit;

namespace Enigma.GitClient.Core.UnitTests;

/// <summary>
/// The product's name in words is written in more places than the code can reach — the Linux launcher
/// entry, the installer and the uninstaller, the views' labels — and nothing but this test ties them to
/// <see cref="ProductInformation.DisplayName"/>.
/// </summary>
public sealed class DisplayNameConsistencyTests
{
    /// <summary>What the product was called up to 4.1.1.</summary>
    private const string FormerName = "Enigma git client";

    [Fact]
    public void TheLauncherEntryIsNamedAfterTheProduct()
    {
        string[] entry = File.ReadAllLines(Path.Combine(Root(), "packaging", "linux", "enigma-git-client.desktop.in"));

        Assert.Contains($"Name={ProductInformation.DisplayName}", entry);
        Assert.Contains($"GenericName={ProductInformation.DisplayName}", entry);
    }

    [Theory]
    [InlineData("install.sh")]
    [InlineData("uninstall.sh")]
    public void TheInstallerAndTheUninstallerSayTheProductsName(string script)
    {
        string text = File.ReadAllText(Path.Combine(Root(), "packaging", "linux", script));

        Assert.Contains(ProductInformation.DisplayName, text, StringComparison.Ordinal);
    }

    [Fact]
    public void NothingShippedStillSaysTheFormerName()
    {
        string root = Root();

        IEnumerable<string> files = Directory
            .EnumerateFiles(Path.Combine(root, "src"), "*.*", SearchOption.AllDirectories)
            .Where(path => path.EndsWith(".cs", StringComparison.Ordinal) || path.EndsWith(".axaml", StringComparison.Ordinal))
            .Where(path => !IsBuildOutput(path))
            .Concat(Directory.EnumerateFiles(Path.Combine(root, "packaging"), "*", SearchOption.AllDirectories)
                .Where(path => !path.EndsWith(".png", StringComparison.Ordinal)))
            .Append(Path.Combine(root, "README.md"));

        string[] stale = [.. files.Where(path => File.ReadAllText(path).Contains(FormerName, StringComparison.Ordinal))
            .Select(path => Path.GetRelativePath(root, path))];

        Assert.Empty(stale);
    }

    private static bool IsBuildOutput(string path)
    {
        string separator = Path.DirectorySeparatorChar.ToString();

        return path.Contains($"{separator}bin{separator}", StringComparison.Ordinal)
            || path.Contains($"{separator}obj{separator}", StringComparison.Ordinal);
    }

    /// <summary>
    /// The repository's root: the first directory above the test's output that holds the solution.
    /// </summary>
    private static string Root()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Enigma.GitClient.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("The repository root was not found above " + AppContext.BaseDirectory);
    }
}
