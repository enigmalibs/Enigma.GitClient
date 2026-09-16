using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Enigma.GitClient.Core.Diff;
using Enigma.GitClient.Core.Files;
using Enigma.GitClient.Core.Repositories;

namespace Enigma.GitClient.App.UnitTests.Infrastructure;

/// <summary>
/// An <see cref="IDiffService"/> that hands back a patch the test chose and records the options it
/// was asked for.
/// </summary>
/// <remarks>
/// The viewer's job is partly to ask git the right question — a wider <c>-U</c>, whitespace
/// ignored, the size limit lifted — and the answer to "what did it ask for?" is not visible in the
/// rendered result. Recording the request is the only way to assert it.
/// </remarks>
public sealed class RecordingDiffService : IDiffService
{
    /// <summary>Gets the options every patch read was made with, oldest first.</summary>
    public List<DiffOptions> PatchRequests { get; } = [];

    /// <summary>Gets the files every patch read named.</summary>
    public List<string> PatchPaths { get; } = [];

    /// <summary>Gets or sets what a patch read returns.</summary>
    public FilePatch? Patch { get; set; }

    /// <summary>Gets or sets what a changed-files read returns.</summary>
    public IReadOnlyList<ChangedFile> Files { get; set; } = [];

    /// <inheritdoc />
    public Task<IReadOnlyList<ChangedFile>> GetChangedFilesAsync(
        RepositoryHandle repository,
        DiffTarget target,
        CancellationToken cancellationToken = default)
        => Task.FromResult(Files);

    /// <inheritdoc />
    public Task<FilePatch?> GetPatchAsync(
        RepositoryHandle repository,
        DiffTarget target,
        string path,
        DiffOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        PatchRequests.Add(options ?? DiffOptions.Default);
        PatchPaths.Add(path);

        return Task.FromResult(Patch);
    }

    /// <inheritdoc />
    public Task<FilePatch?> GetPatchAsync(
        RepositoryHandle repository,
        DiffTarget target,
        ChangedFile file,
        DiffOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(file);

        PatchRequests.Add(options ?? DiffOptions.Default);
        PatchPaths.Add(file.Path);

        return Task.FromResult(Patch);
    }
}
