using System;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Enigma.Avalonia.Desktop.Controls.ContentDialog;
using Enigma.GitClient.App.ViewModels.Pages;

namespace Enigma.GitClient.App.Views.Pages;

/// <summary>
/// The commit history page.
/// </summary>
public partial class HistoryPageView : UserControl
{
    private HistoryPageViewModel? _page;

    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    public HistoryPageView()
    {
        InitializeComponent();

        // Every other way out of the dialog — Escape, the scrim, the Close button — ends here, so
        // this is what keeps the page's own state honest about what is on screen.
        DiffDialog.Closed += OnDiffDialogClosed;
    }

    /// <summary>
    /// Follows the page's dialog state, which is what decides whether the diffs are on screen.
    /// </summary>
    /// <param name="e">The event.</param>
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (_page is not null)
        {
            _page.PropertyChanged -= OnPagePropertyChanged;
        }

        _page = DataContext as HistoryPageViewModel;

        if (_page is not null)
        {
            _page.PropertyChanged += OnPagePropertyChanged;
        }

        ApplyDialogState(_page?.IsDiffDialogOpen ?? false);
    }

    private void OnPagePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(HistoryPageViewModel.IsDiffDialogOpen) or null)
        {
            ApplyDialogState(_page?.IsDiffDialogOpen ?? false);
        }
    }

    /// <summary>
    /// Opens or closes the dialog showing what the selected commit changed.
    /// </summary>
    /// <param name="isOpen">Whether the page wants it on screen.</param>
    /// <remarks>
    /// Through the control's own methods rather than its <see cref="ContentDialog.IsOpen"/>
    /// property: each <c>ShowAsync</c> hands out a completion source that closing resolves, so an
    /// open or a close that the dialog is already in would resolve one twice. Hence the guard, and
    /// hence the discarded tasks — the page is told the dialog closed by the event, not by awaiting
    /// a result nobody reads.
    /// </remarks>
    private void ApplyDialogState(bool isOpen)
    {
        if (isOpen == DiffDialog.IsOpen)
        {
            return;
        }

        if (isOpen)
        {
            _ = DiffDialog.ShowAsync();
            return;
        }

        _ = DiffDialog.HideAsync();
    }

    private void OnDiffDialogClosed(object? sender, DialogResult result)
    {
        if (_page is not null)
        {
            _page.IsDiffDialogOpen = false;
        }
    }

    /// <summary>
    /// Shows what the double-clicked row changed.
    /// </summary>
    /// <remarks>
    /// A double-click is a gesture, not state, so it has nowhere to live but here. The handler does
    /// no work of its own: it finds the row and runs the command the ViewModel already exposes, so
    /// the behaviour stays testable without a pointer. The first click of the pair has already put
    /// the selection on the row, which is why the page's own selection is what it acts on.
    /// </remarks>
    /// <param name="sender">The list.</param>
    /// <param name="e">The gesture.</param>
    private void OnCommitDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is not HistoryPageViewModel page || page.SelectedRow is not { } row)
        {
            return;
        }

        if (row.Commands?.Activate is { } activate && activate.CanExecute(row))
        {
            activate.Execute(row);
        }
    }
}
