using Enigma.GitClient.Desktop.Services;
using Xunit;

namespace Enigma.GitClient.Desktop.UnitTests;

/// <summary>
/// Which files a click on "Open" would run, and so are shown in the file manager instead (SEC-03).
/// </summary>
public sealed class RunnableFilesTests
{
    [Theory]
    [InlineData("build.cmd")]
    [InlineData("tools/setup.exe")]
    [InlineData("install.BAT")]
    [InlineData("deploy.ps1")]
    [InlineData("readme.lnk")]
    [InlineData("docs.url")]
    [InlineData("explorer.scf")]
    [InlineData("helper.vbs")]
    [InlineData("app.js")]
    [InlineData("package.msi")]
    [InlineData("settings.reg")]
    [InlineData("tool.jar")]
    public void OnWindows_ProgramsScriptsAndShortcuts_AreRunnable(string path)
        => Assert.True(RunnableFiles.IsRunnable(path, isWindows: true, pathExtensions: null));

    [Theory]
    [InlineData("README.md")]
    [InlineData("src/Program.cs")]
    [InlineData("logo.png")]
    [InlineData("notes.txt")]
    [InlineData("Makefile")]
    [InlineData("archive.zip")]
    public void OnWindows_DocumentsAndSources_AreNot(string path)
        => Assert.False(RunnableFiles.IsRunnable(path, isWindows: true, pathExtensions: null));

    [Fact]
    public void OnWindows_TrailingDotsAndSpaces_DoNotHideTheExtension()
    {
        // Windows opens "setup.exe. " as setup.exe.
        Assert.True(RunnableFiles.IsRunnable("setup.exe.", isWindows: true, pathExtensions: null));
        Assert.True(RunnableFiles.IsRunnable("setup.exe ", isWindows: true, pathExtensions: null));
    }

    [Fact]
    public void OnWindows_WhatPathExtSaysRuns_IsRunnable()
    {
        Assert.False(RunnableFiles.IsRunnable("script.py", isWindows: true, pathExtensions: null));
        Assert.True(RunnableFiles.IsRunnable("script.py", isWindows: true, pathExtensions: ".COM;.EXE;.PY"));
    }

    [Theory]
    [InlineData("launch.desktop")]
    [InlineData("build.sh")]
    [InlineData("Run.command")]
    [InlineData("tool.AppImage")]
    [InlineData("installer.run")]
    public void Elsewhere_LaunchersAndScripts_AreRunnable(string path)
        => Assert.True(RunnableFiles.IsRunnable(path, isWindows: false, pathExtensions: null));

    [Theory]
    [InlineData("app.js")]
    [InlineData("build.cmd")]
    [InlineData("README.md")]
    public void Elsewhere_WhatOnlyWindowsRuns_OpensAsUsual(string path)
        => Assert.False(RunnableFiles.IsRunnable(path, isWindows: false, pathExtensions: null));
}
