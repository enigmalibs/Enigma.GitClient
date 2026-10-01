using System;
using System.Threading.Tasks;
using Avalonia.Media;
using Enigma.Avalonia.Desktop.Services;
using Enigma.GitClient.Core.History;
using Enigma.GitClient.Desktop.ViewModels.Dialogs;
using Enigma.GitClient.Desktop.Views.Dialogs;
using Enigma.Icons.Avalonia;
using Enigma.Icons.Phosphor;

namespace Enigma.GitClient.Desktop.Services;

/// <summary>
/// Shows a commit's details in a dialog, so that no ViewModel has to know how it is built.
/// </summary>
public interface ICommitDetailsDialogService
{
    /// <summary>
    /// Shows the dialog and waits for it to be closed.
    /// </summary>
    /// <param name="commit">The commit to describe.</param>
    /// <returns>A task that completes when the reader has dismissed it.</returns>
    Task ShowAsync(GitCommit commit);
}

/// <summary>
/// Default <see cref="ICommitDetailsDialogService"/>: the commit details view in the window's content
/// dialog, as the About dialog is shown.
/// </summary>
public sealed class CommitDetailsDialogService : ICommitDetailsDialogService
{
    /// <summary>
    /// The heading glyph, resolved once: a page of text, which is what a commit's message is — the
    /// Info glyph already means About in the same window.
    /// </summary>
    private static readonly Geometry Icon = PhosphorIconSet.Instance
        .GetGlyph(PhosphorIcon.Article, PhosphorWeight.Regular)
        .ToGeometry();

    private readonly IContentDialogService _dialogs;
    private readonly TimeProvider _clock;

    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="dialogs">Hosts the dialog.</param>
    /// <param name="clock">Says how long ago the commit was written.</param>
    public CommitDetailsDialogService(IContentDialogService dialogs, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(dialogs);
        ArgumentNullException.ThrowIfNull(clock);

        _dialogs = dialogs;
        _clock = clock;
    }

    /// <inheritdoc />
    public async Task ShowAsync(GitCommit commit)
    {
        ArgumentNullException.ThrowIfNull(commit);

        CommitDetailsView view = new()
        {
            DataContext = new CommitDetailsViewModel(commit, _clock.GetLocalNow()),
        };

        await _dialogs.ShowAsync(dialog =>
        {
            dialog.Title = "Commit details";
            dialog.Content = view;
            dialog.IconData = Icon;

            // One button, and it only dismisses: the dialog describes and asks nothing.
            dialog.CloseButtonText = "Close";
        }).ConfigureAwait(true);
    }
}
