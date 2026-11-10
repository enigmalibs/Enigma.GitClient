using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using Enigma.GitClient.Core.Git;
using Enigma.GitClient.Core.Submodules;
using Enigma.GitClient.Desktop.Services;
using Microsoft.Extensions.Logging;

namespace Enigma.GitClient.Desktop.ViewModels;

/// <summary>
/// The repository window's band that says when submodules are not on the commit the repository
/// expects — not downloaded, or behind — whatever the cause: the automatic update turned off, a pull
/// made in a terminal, an update that left one where it was.
/// </summary>
/// <remarks>
/// <para>
/// A submodule with new commits, or on a commit on another line, does not raise it: that is usually
/// work in progress, and the Submodules dialog says so.
/// </para>
/// <para>
/// Read after every refresh, and only for a repository whose work tree declares submodules: one
/// without any costs nothing.
/// </para>
/// </remarks>
public sealed class SubmoduleNoticeViewModel : ViewModelBase
{
    private readonly IRepositoryContext _context;
    private readonly ISubmoduleService _submodules;
    private readonly ISubmoduleOperations _operations;
    private readonly IToolDialogService _tools;
    private readonly ILogger<SubmoduleNoticeViewModel> _logger;

    private IReadOnlyList<string> _paths = [];
    private bool _reading;
    private bool _readAgain;

    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="context">The repository the window is looking at.</param>
    /// <param name="submodules">Reads where each submodule stands.</param>
    /// <param name="operations">Updates the submodules the band names.</param>
    /// <param name="tools">Opens the Submodules dialog.</param>
    /// <param name="logger">Receives a list that could not be read.</param>
    public SubmoduleNoticeViewModel(
        IRepositoryContext context,
        ISubmoduleService submodules,
        ISubmoduleOperations operations,
        IToolDialogService tools,
        ILogger<SubmoduleNoticeViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(submodules);
        ArgumentNullException.ThrowIfNull(operations);
        ArgumentNullException.ThrowIfNull(tools);
        ArgumentNullException.ThrowIfNull(logger);

        _context = context;
        _submodules = submodules;
        _operations = operations;
        _tools = tools;
        _logger = logger;

        UpdateCommand = new AsyncRelayCommand(() => _operations.UpdateAsync([.. _paths]), () => _paths.Count > 0);
        OpenSubmodulesCommand = new AsyncRelayCommand(() => _tools.ShowAsync(ToolDialog.Submodules));

        _context.StateRefreshed += (_, _) => ReadAsync().Forget();
        _context.RepositoryChanged += (_, _) => Show([]);
    }

    /// <summary>Gets a value indicating whether the band is on screen.</summary>
    public bool IsVisible { get; private set => SetProperty(ref field, value); }

    /// <summary>Gets what the band says.</summary>
    public string Message { get; private set => SetProperty(ref field, value); } = string.Empty;

    /// <summary>Gets the band's tooltip: each submodule it is about, and where it stands.</summary>
    public string Details { get; private set => SetProperty(ref field, value); } = string.Empty;

    /// <summary>
    /// Gets the command that puts the submodules the band names on their expected commits — never one
    /// with uncommitted changes or on a commit no branch contains.
    /// </summary>
    public AsyncRelayCommand UpdateCommand { get; }

    /// <summary>Gets the command that opens the Submodules dialog.</summary>
    public AsyncRelayCommand OpenSubmodulesCommand { get; }

    /// <summary>
    /// Reads where the submodules stand and shows or hides the band; a read asked for while one runs
    /// runs again once it ends.
    /// </summary>
    /// <returns>A task that completes once the band is up to date.</returns>
    public async Task ReadAsync()
    {
        if (_reading)
        {
            _readAgain = true;
            return;
        }

        _reading = true;

        try
        {
            do
            {
                _readAgain = false;
                await ReadOnceAsync().ConfigureAwait(true);
            }
            while (_readAgain);
        }
        finally
        {
            _reading = false;
        }
    }

    /// <summary>
    /// Says what the band says for so many submodules.
    /// </summary>
    /// <param name="count">How many are not on the expected commit.</param>
    /// <returns>The sentence.</returns>
    public static string Describe(int count)
        => count == 1
            ? "1 submodule is not on the commit this repository expects."
            : $"{count} submodules are not on the commit this repository expects.";

    private async Task ReadOnceAsync()
    {
        if (_context.Repository is not { } repository || !_context.HasSubmodules)
        {
            Show([]);
            return;
        }

        try
        {
            IReadOnlyList<GitSubmodule> listed = await _submodules
                .ListAsync(repository, _context.RepositoryLifetime)
                .ConfigureAwait(true);

            if (ReferenceEquals(repository, _context.Repository))
            {
                Show([.. listed.Where(submodule => submodule.State is SubmoduleCheckoutState.Behind or SubmoduleCheckoutState.NotDownloaded)]);
            }
        }
        catch (OperationCanceledException)
        {
            // The repository was swapped or closed; its successor reads its own.
        }
        catch (GitCommandException exception)
        {
            _logger.LogWarning(exception, "The submodules of {Repository} could not be read for the band", repository);
            Show([]);
        }
    }

    private void Show(IReadOnlyList<GitSubmodule> stale)
    {
        _paths = [.. stale.Select(submodule => submodule.Path)];

        IsVisible = stale.Count > 0;
        Message = stale.Count > 0 ? Describe(stale.Count) : string.Empty;
        Details = string.Join(
            Environment.NewLine,
            stale.Select(submodule => submodule.State == SubmoduleCheckoutState.NotDownloaded
                ? $"{submodule.Path}: not downloaded"
                : $"{submodule.Path}: behind"));

        UpdateCommand.NotifyCanExecuteChanged();
    }
}
