using System;
using System.IO;
using System.Xml.Linq;
using Xunit;

namespace Enigma.GitClient.Desktop.UnitTests;

/// <summary>
/// The application's assembly name is written in places the compiler cannot reach — the project's
/// directory, the Linux installer and uninstaller, which look for the launcher it becomes, the desktop
/// entry, which matches the window class Avalonia takes from it, and the Windows manifest — and nothing
/// but this test ties them to the assembly the build really produces.
/// </summary>
public sealed class AssemblyNameConsistencyTests
{
    private static readonly string AssemblyName =
        typeof(App).Assembly.GetName().Name ?? string.Empty;

    [Fact]
    public void TheApplicationIsNamedLikeTheOtherEnigmaDesktopApplications()
        => Assert.Equal("Enigma.GitClient.Desktop", AssemblyName);

    [Theory]
    [InlineData("install.sh")]
    [InlineData("uninstall.sh")]
    public void TheInstallerAndTheUninstallerLookForTheLauncherTheBuildProduces(string script)
    {
        string[] lines = File.ReadAllLines(Path.Combine(Root(), "packaging", "linux", script));

        Assert.Contains($"readonly APP_EXE='{AssemblyName}'", lines);
    }

    [Fact]
    public void TheLauncherEntryExpectsTheWindowClassOfTheAssembly()
    {
        string[] entry = File.ReadAllLines(Path.Combine(Root(), "packaging", "linux", "enigma-git-client.desktop.in"));

        Assert.Contains($"StartupWMClass={AssemblyName}", entry);
    }

    [Fact]
    public void TheProjectDirectoryAndTheWindowsManifestAreNamedAfterTheAssembly()
    {
        XNamespace assembly = "urn:schemas-microsoft-com:asm.v1";
        XDocument manifest = XDocument.Load(Path.Combine(Root(), "src", AssemblyName, "app.manifest"));

        Assert.Equal(AssemblyName, manifest.Root?.Element(assembly + "assemblyIdentity")?.Attribute("name")?.Value);
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
