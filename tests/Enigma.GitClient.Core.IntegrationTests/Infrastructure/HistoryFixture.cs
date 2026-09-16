using System;
using System.Threading.Tasks;

namespace Enigma.GitClient.Core.IntegrationTests.Infrastructure;

/// <summary>
/// A repository with a known, deliberately non-trivial history, used by every test that needs a
/// real graph to read.
/// </summary>
/// <remarks>
/// <code>
///   main:    A --- B --------- D(merge) --- E
///                   \         /
///   topic:           C ------/
///
///   feature: A --- F                 (never merged; only reachable through --all)
///   tag v1.0 -> B (annotated), tag v0.9 -> A (lightweight)
/// </code>
/// Every commit gets an explicit, increasing timestamp so date ordering is deterministic whatever
/// the machine's speed.
/// </remarks>
public sealed class HistoryFixture
{
    private HistoryFixture(TemporaryRepository repository) => Repository = repository;

    /// <summary>Gets the repository.</summary>
    public TemporaryRepository Repository { get; }

    /// <summary>Gets the SHA of the root commit on <c>main</c>.</summary>
    public string ShaA { get; private set; } = string.Empty;

    /// <summary>Gets the SHA of the second commit on <c>main</c>.</summary>
    public string ShaB { get; private set; } = string.Empty;

    /// <summary>Gets the SHA of the only commit on <c>topic</c>.</summary>
    public string ShaC { get; private set; } = string.Empty;

    /// <summary>Gets the SHA of the merge commit.</summary>
    public string ShaD { get; private set; } = string.Empty;

    /// <summary>Gets the SHA of the commit after the merge.</summary>
    public string ShaE { get; private set; } = string.Empty;

    /// <summary>Gets the SHA of the commit on the unmerged <c>feature</c> branch.</summary>
    public string ShaF { get; private set; } = string.Empty;

    /// <summary>
    /// Builds the fixture inside a workspace.
    /// </summary>
    /// <param name="workspace">The workspace to create the repository in.</param>
    /// <param name="name">The repository directory name.</param>
    /// <returns>The built fixture.</returns>
    public static async Task<HistoryFixture> CreateAsync(GitWorkspace workspace, string name = "history")
    {
        ArgumentNullException.ThrowIfNull(workspace);

        TemporaryRepository repository = await workspace.InitRepositoryAsync(name);
        HistoryFixture fixture = new(repository);

        DateTimeOffset start = new(2026, 1, 1, 8, 0, 0, TimeSpan.Zero);

        fixture.ShaA = await repository.CommitFileAtAsync(
            "README.md",
            "# fixture\n",
            "Add the readme",
            start);

        fixture.ShaB = await repository.CommitFileAtAsync(
            "src/app.txt",
            "one\ntwo\nthree\n",
            "Add the application file",
            start.AddMinutes(10));

        await repository.GitAsync("tag", "v0.9", fixture.ShaA);
        await repository.GitAsync("tag", "-a", "v1.0", "-m", "First release\n\nWith release notes.", fixture.ShaB);

        await repository.GitAsync("checkout", "-b", "topic", fixture.ShaB);
        fixture.ShaC = await repository.CommitFileAtAsync(
            "src/topic.txt",
            "topic\n",
            "Work on the topic branch",
            start.AddMinutes(20));

        await repository.GitAsync("checkout", "main");
        await repository.GitWithEnvironmentAsync(
            new System.Collections.Generic.Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["GIT_AUTHOR_DATE"] = start.AddMinutes(30).ToString("o", System.Globalization.CultureInfo.InvariantCulture),
                ["GIT_COMMITTER_DATE"] = start.AddMinutes(30).ToString("o", System.Globalization.CultureInfo.InvariantCulture),
            },
            "merge",
            "--no-ff",
            "topic",
            "-m",
            "Merge the topic branch\n\nIt brings in the topic file.");
        fixture.ShaD = await repository.ResolveAsync("HEAD");

        fixture.ShaE = await repository.CommitFileAtAsync(
            "src/app.txt",
            "one\ntwo modified\nthree\nfour\n",
            "Extend the application file",
            start.AddMinutes(40));

        await repository.GitAsync("checkout", "-b", "feature", fixture.ShaA);
        fixture.ShaF = await repository.CommitFileAtAsync(
            "src/feature.txt",
            "feature\n",
            "Start the feature branch",
            start.AddMinutes(50));

        await repository.GitAsync("checkout", "main");

        return fixture;
    }
}
