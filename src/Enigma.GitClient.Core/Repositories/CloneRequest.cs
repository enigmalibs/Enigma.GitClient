using System;
using System.IO;

namespace Enigma.GitClient.Core.Repositories;

/// <summary>
/// What to clone, and where.
/// </summary>
public sealed record CloneRequest
{
    /// <summary>
    /// Gets the URL or local path to clone from.
    /// </summary>
    public required string Url { get; init; }

    /// <summary>
    /// Gets the directory the clone is created inside.
    /// </summary>
    public required string ParentDirectory { get; init; }

    /// <summary>
    /// Gets the name of the directory to create. When empty, the name is derived from the URL.
    /// </summary>
    public string? DirectoryName { get; init; }

    /// <summary>
    /// Gets the branch to check out, or <see langword="null"/> for the remote's default.
    /// </summary>
    public string? Branch { get; init; }

    /// <summary>
    /// Gets the shallow-clone depth, or <see langword="null"/> for the full history.
    /// </summary>
    public int? Depth { get; init; }

    /// <summary>
    /// Gets a value indicating whether to clone submodules as well.
    /// </summary>
    public bool RecurseSubmodules { get; init; }

    /// <summary>
    /// Gets the name of the remote the clone creates.
    /// </summary>
    public string RemoteName { get; init; } = "origin";

    /// <summary>
    /// Gets the absolute path the clone will occupy.
    /// </summary>
    public string TargetPath
        => Path.Combine(ParentDirectory, DirectoryName is { Length: > 0 } name ? name : DeriveDirectoryName(Url));

    /// <summary>
    /// Derives the directory name git itself would pick for a URL.
    /// </summary>
    /// <param name="url">The clone URL.</param>
    /// <returns>The directory name, or <c>repository</c> when nothing usable can be derived.</returns>
    public static string DeriveDirectoryName(string url)
    {
        ArgumentNullException.ThrowIfNull(url);

        string trimmed = url.Trim().TrimEnd('/', '\\');

        if (trimmed.Length == 0)
        {
            return "repository";
        }

        // Works for https://host/owner/repo.git, ssh://host/owner/repo, git@host:owner/repo.git and
        // a local path, because every one of them ends in the repository's own name.
        int separator = trimmed.LastIndexOfAny(['/', '\\', ':']);
        string name = separator >= 0 ? trimmed.Substring(separator + 1) : trimmed;

        if (name.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
        {
            name = name.Substring(0, name.Length - 4);
        }

        return name.Length == 0 ? "repository" : name;
    }
}

/// <summary>
/// Which part of a clone git is working on.
/// </summary>
public enum CloneStage
{
    /// <summary>Nothing has been reported yet.</summary>
    Starting,

    /// <summary>The remote is enumerating what it will send.</summary>
    CountingObjects,

    /// <summary>The remote is compressing what it will send.</summary>
    CompressingObjects,

    /// <summary>Objects are arriving.</summary>
    ReceivingObjects,

    /// <summary>Deltas are being resolved locally.</summary>
    ResolvingDeltas,

    /// <summary>The work tree is being written.</summary>
    UpdatingFiles,

    /// <summary>The clone has finished.</summary>
    Done,
}

/// <summary>
/// How far a clone has got.
/// </summary>
/// <param name="Stage">Which part of the clone is running.</param>
/// <param name="Percentage">The stage's completion, or <see langword="null"/> when git reported none.</param>
/// <param name="Message">The raw line git wrote, for the detail label.</param>
public sealed record CloneProgress(CloneStage Stage, int? Percentage, string Message)
{
    /// <summary>
    /// The progress reported before git has said anything.
    /// </summary>
    public static readonly CloneProgress Starting = new(CloneStage.Starting, null, "Starting…");

    /// <summary>
    /// Gets a short label for the stage, suitable for a progress card's title.
    /// </summary>
    public string StageDescription
        => Stage switch
        {
            CloneStage.CountingObjects => "Counting objects",
            CloneStage.CompressingObjects => "Compressing objects",
            CloneStage.ReceivingObjects => "Receiving objects",
            CloneStage.ResolvingDeltas => "Resolving deltas",
            CloneStage.UpdatingFiles => "Updating files",
            CloneStage.Done => "Finishing",
            _ => "Starting",
        };
}
