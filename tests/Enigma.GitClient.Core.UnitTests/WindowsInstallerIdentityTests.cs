using System;
using System.IO;
using Xunit;

namespace Enigma.GitClient.Core.UnitTests;

/// <summary>
/// The Windows installer's upgrade code is the product line's identity in Windows Installer: an MSI that
/// carries the installed one's code replaces it, and one that does not installs beside it. Nothing in
/// the build notices a changed code, so this test pins the one written in
/// <c>packaging/windows/buildmsi.ps1</c>.
/// </summary>
public sealed class WindowsInstallerIdentityTests
{
    /// <summary>
    /// The upgrade code of the MSI that installed 6.13.0, read from its package: every later MSI must keep
    /// it.
    /// </summary>
    private const string UpgradeCode = "871622d1-a9c6-4c72-b6a3-e8d8104ad5c3";

    [Fact]
    public void TheInstallerKeepsTheUpgradeCodeOfTheInstalledReleases()
    {
        string[] script = File.ReadAllLines(Path.Combine(Root(), "packaging", "windows", "buildmsi.ps1"));

        Assert.Contains($"$UpgradeCode = '{UpgradeCode}'", script);
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
