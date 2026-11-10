using System;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace Enigma.GitClient.Core.IntegrationTests.Infrastructure;

/// <summary>
/// A repository with submodules, built once for a test class and copied into each test's workspace:
/// <c>super</c>, with four libraries under <c>libs/</c> — <c>alpha</c>, <c>beta lib</c>, <c>delta</c>
/// and <c>gamma</c>, each with two commits — and <c>alpha</c> with a library of its own at
/// <c>vendor/leaf</c>. Every submodule is downloaded and on the commit its repository expects.
/// </summary>
/// <remarks>
/// Building it takes some forty git starts, a dozen seconds on Windows; copying it takes none. The
/// libraries are copied too, beside <c>super</c>, so the relative addresses in <c>.gitmodules</c>
/// (<c>../alpha</c>) find the test's own copies when a test downloads a submodule again.
/// </remarks>
public sealed class SubmoduleTemplate : IAsyncLifetime
{
    /// <summary>The path of <c>alpha</c> in <c>super</c>.</summary>
    public const string Alpha = "libs/alpha";

    /// <summary>The path of the library nested in <c>alpha</c>, from <c>super</c>.</summary>
    public const string Leaf = "libs/alpha/vendor/leaf";

    /// <summary>The path of <c>beta lib</c> in <c>super</c> — a path with a space.</summary>
    public const string Beta = "libs/beta lib";

    /// <summary>The path of <c>delta</c> in <c>super</c>.</summary>
    public const string Delta = "libs/delta";

    /// <summary>The path of <c>gamma</c> in <c>super</c>.</summary>
    public const string Gamma = "libs/gamma";

    private GitWorkspace _workspace = null!;

    /// <summary>Gets the commit <c>super</c> expects <c>alpha</c> on.</summary>
    public string AlphaPin { get; private set; } = string.Empty;

    /// <summary>Gets the commit <c>super</c> expects <c>beta lib</c> on.</summary>
    public string BetaPin { get; private set; } = string.Empty;

    /// <summary>Gets the commit <c>super</c> expects <c>delta</c> on.</summary>
    public string DeltaPin { get; private set; } = string.Empty;

    /// <summary>Gets the commit <c>super</c> expects <c>gamma</c> on.</summary>
    public string GammaPin { get; private set; } = string.Empty;

    /// <summary>Gets the commit before <see cref="GammaPin"/>, on <c>gamma</c>'s branch.</summary>
    public string GammaOlder { get; private set; } = string.Empty;

    /// <summary>Gets the commit <c>alpha</c> expects <c>vendor/leaf</c> on.</summary>
    public string LeafPin { get; private set; } = string.Empty;

    /// <summary>Gets the commit <c>super</c>'s branch is on.</summary>
    public string SuperHead { get; private set; } = string.Empty;

    /// <inheritdoc />
    public async ValueTask InitializeAsync()
    {
        _workspace = GitWorkspace.Create();
        _workspace.AllowLocalSubmodules();

        LeafPin = await MakeLibraryAsync("leaf");

        TemporaryRepository alpha = await _workspace.InitRepositoryAsync("alpha");
        await alpha.CommitFileAsync("lib.txt", "alpha one\n", "alpha one");
        await alpha.GitAsync("submodule", "add", "--", "../leaf", "vendor/leaf");
        AlphaPin = await alpha.CommitAllAsync("Add the leaf");

        BetaPin = await MakeLibraryAsync("beta lib");
        DeltaPin = await MakeLibraryAsync("delta");
        GammaPin = await MakeLibraryAsync("gamma");
        GammaOlder = (await GitCli.RunAsync(Path.Combine(_workspace.RootPath, "gamma"), _workspace.Environment, ["rev-parse", "HEAD~1"])).Trim();

        TemporaryRepository super = await _workspace.InitRepositoryAsync("super");
        await super.CommitFileAsync("README.md", "# super\n", "Add the readme");

        foreach (string library in new[] { "alpha", "beta lib", "delta", "gamma" })
        {
            await super.GitAsync("submodule", "add", "--", "../" + library, "libs/" + library);
        }

        await super.GitAsync("submodule", "update", "--init", "--recursive");
        SuperHead = await super.CommitAllAsync("Add the libraries");
    }

    /// <summary>
    /// Copies <c>super</c> and its libraries into a test's workspace.
    /// </summary>
    /// <param name="workspace">The test's workspace; it must allow local submodules to download one again.</param>
    /// <returns>The test's own <c>super</c>.</returns>
    public TemporaryRepository CopyInto(GitWorkspace workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);

        foreach (string directory in Directory.EnumerateDirectories(_workspace.RootPath))
        {
            string name = Path.GetFileName(directory);

            if (name != "home")
            {
                HistoryFixture.CopyDirectory(directory, Path.Combine(workspace.RootPath, name));
            }
        }

        return new TemporaryRepository(workspace, Path.Combine(workspace.RootPath, "super"), isBare: false);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_workspace is not null)
        {
            await _workspace.DisposeAsync();
        }
    }

    private async Task<string> MakeLibraryAsync(string name)
    {
        TemporaryRepository library = await _workspace.InitRepositoryAsync(name);
        await library.CommitFileAsync("lib.txt", name + " one\n", name + " one");

        return await library.CommitFileAsync("lib.txt", name + " one\n" + name + " two\n", name + " two");
    }
}
