using System;
using System.Collections.Generic;
using System.Linq;
using Enigma.GitClient.Core.Refs;
using Microsoft.Extensions.Logging;

namespace Enigma.GitClient.App.Services;

/// <summary>
/// The branches of the open repository the user hid from the history.
/// </summary>
/// <remarks>
/// Shared by the two places that care: the branches dialog, where a branch is hidden or shown, and the
/// history, which leaves the hidden ones out of its walk and off its badges.
/// </remarks>
public interface IHiddenBranches
{
    /// <summary>
    /// Gets the full names of the refs hidden in the open repository — empty when none is open.
    /// </summary>
    /// <remarks>
    /// Read from the store the first time it is asked for after a repository opens, so whoever asks
    /// first — the history building its first query — already gets the repository's own set.
    /// </remarks>
    IReadOnlySet<string> Hidden { get; }

    /// <summary>
    /// Answers whether a ref is hidden in the open repository.
    /// </summary>
    /// <param name="fullName">The ref's full name — <c>refs/heads/topic</c>.</param>
    /// <returns><see langword="true"/> when it is hidden.</returns>
    bool IsHidden(string fullName);

    /// <summary>
    /// Hides a ref of the open repository, or shows it again, and remembers it.
    /// </summary>
    /// <param name="fullName">The ref's full name.</param>
    /// <param name="hidden">Whether it is hidden from now on.</param>
    void SetHidden(string fullName, bool hidden);

    /// <summary>
    /// Shows every ref of the open repository again.
    /// </summary>
    void ShowAll();

    /// <summary>
    /// Raised when the open repository's set changed because of <see cref="SetHidden"/> or
    /// <see cref="ShowAll"/> — not when another repository opens, which every page follows already.
    /// </summary>
    event EventHandler? Changed;
}

/// <summary>
/// Default <see cref="IHiddenBranches"/>, over <see cref="IHiddenBranchStore"/>.
/// </summary>
public sealed class HiddenBranches : IHiddenBranches
{
    private static readonly IReadOnlySet<string> Nothing = new HashSet<string>(StringComparer.Ordinal);

    private readonly IRepositoryContext _context;
    private readonly IHiddenBranchStore _store;
    private readonly ILogger<HiddenBranches> _logger;

    private string? _loadedFor;
    private IReadOnlySet<string> _hidden = Nothing;

    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="context">Says which repository is open, and which refs it has.</param>
    /// <param name="store">Keeps the sets between sessions.</param>
    /// <param name="logger">Receives a store that failed, which is read as "nothing hidden".</param>
    public HiddenBranches(IRepositoryContext context, IHiddenBranchStore store, ILogger<HiddenBranches> logger)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(logger);

        _context = context;
        _store = store;
        _logger = logger;
    }

    /// <inheritdoc />
    public event EventHandler? Changed;

    /// <inheritdoc />
    public IReadOnlySet<string> Hidden
    {
        get
        {
            EnsureLoaded();
            return _hidden;
        }
    }

    /// <inheritdoc />
    public bool IsHidden(string fullName) => fullName is not null && Hidden.Contains(fullName);

    /// <inheritdoc />
    public void SetHidden(string fullName, bool hidden)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fullName);

        if (_context.Repository is not { } repository || IsHidden(fullName) == hidden)
        {
            return;
        }

        IReadOnlySet<string> updated;

        try
        {
            updated = _store.SetHidden(repository.WorkTreePath, fullName, hidden, ExistingRefs());
        }
        catch (Exception exception) when (exception is not ArgumentException)
        {
            // The session still does what was asked; only remembering it failed.
            _logger.LogWarning(exception, "Whether {Ref} is hidden could not be remembered", fullName);

            HashSet<string> local = [.. _hidden];

            if (hidden)
            {
                local.Add(fullName);
            }
            else
            {
                local.Remove(fullName);
            }

            updated = local;
        }

        _hidden = updated;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc />
    public void ShowAll()
    {
        if (_context.Repository is not { } repository || Hidden.Count == 0)
        {
            return;
        }

        try
        {
            _store.ShowAll(repository.WorkTreePath);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Showing every branch again could not be remembered");
        }

        _hidden = Nothing;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Reads the open repository's set, once per repository.
    /// </summary>
    private void EnsureLoaded()
    {
        string? path = _context.Repository?.WorkTreePath;

        if (string.Equals(path, _loadedFor, StringComparison.Ordinal))
        {
            return;
        }

        _loadedFor = path;
        _hidden = Nothing;

        if (path is null)
        {
            return;
        }

        try
        {
            _hidden = _store.Get(path);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "The hidden branches of {Repository} could not be read; every branch is shown", path);
        }
    }

    /// <summary>
    /// Every branch the open repository has, or <see langword="null"/> while its references have not
    /// been read — pruning against an empty list would forget every hidden branch.
    /// </summary>
    private List<string>? ExistingRefs()
    {
        RefCollection refs = _context.Refs;

        List<string> names = [.. refs.LocalBranches.Select(branch => branch.FullName), .. refs.RemoteBranches.Select(branch => branch.FullName)];

        return names.Count == 0 ? null : names;
    }
}
