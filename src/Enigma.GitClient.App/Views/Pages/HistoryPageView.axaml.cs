using System;
using System.ComponentModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Enigma.Avalonia.Desktop.Controls.ContentDialog;
using Enigma.GitClient.App.Controls;
using Enigma.GitClient.App.ViewModels.Pages;

namespace Enigma.GitClient.App.Views.Pages;

/// <summary>
/// The commit history page.
/// </summary>
public partial class HistoryPageView : UserControl
{
    private HistoryPageViewModel? _page;
    private ScrollViewer? _listScroll;

    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    public HistoryPageView()
    {
        InitializeComponent();

        // Every other way out of the dialog — Escape, the scrim, the Close button — ends here, so
        // this is what keeps the page's own state honest about what is on screen.
        DiffDialog.Closed += OnDiffDialogClosed;

        DiffDialogBody.AttachedToVisualTree += OnDialogBodyAttached;

        Resizes(RefsGrip, HistoryColumn.Refs);
        Resizes(AuthorGrip, HistoryColumn.Author);
        Resizes(DateGrip, HistoryColumn.Date);
        Resizes(ShaGrip, HistoryColumn.Sha);

        // The list's own viewport is the width the header has to match, and it is known only once
        // the list has a template to find a scroll viewer in.
        CommitList.TemplateApplied += OnCommitListTemplateApplied;
    }

    // ---------------------------------------------------------------- the columns

    /// <summary>
    /// Makes a header grip resize a column.
    /// </summary>
    /// <param name="grip">The grip.</param>
    /// <param name="column">The column it belongs to.</param>
    /// <remarks>
    /// A <see cref="Thumb"/> reports how far the pointer moved since the last report, which is
    /// exactly what the layout takes: it decides for itself which way that moves the column's edge,
    /// and how far it may go.
    /// </remarks>
    private void Resizes(Thumb grip, HistoryColumn column)
        => grip.DragDelta += (_, e) => (DataContext as HistoryPageViewModel)?.Columns.Resize(column, e.Vector.X);

    private void OnCommitListTemplateApplied(object? sender, TemplateAppliedEventArgs e)
    {
        if (_listScroll is not null)
        {
            _listScroll.ScrollChanged -= OnListScrollChanged;
            _listScroll.SizeChanged -= OnListResized;
        }

        _listScroll = e.NameScope.Find<ScrollViewer>("PART_ScrollViewer")
            ?? CommitList.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();

        if (_listScroll is null)
        {
            return;
        }

        // Both, because the two ways the viewport changes are not the same event: the window being
        // resized, and the vertical scrollbar appearing when one more row than fits is loaded.
        _listScroll.ScrollChanged += OnListScrollChanged;
        _listScroll.SizeChanged += OnListResized;

        ReportViewport();
    }

    private void OnListScrollChanged(object? sender, ScrollChangedEventArgs e) => ReportViewport();

    private void OnListResized(object? sender, SizeChangedEventArgs e) => ReportViewport();

    /// <summary>
    /// Tells the columns how wide the list's viewport is, which is what the header is drawn at.
    /// </summary>
    private void ReportViewport()
    {
        if (_listScroll is not null && DataContext is HistoryPageViewModel page)
        {
            page.Columns.Viewport = _listScroll.Viewport.Width;
        }
    }

    /// <summary>
    /// Stops the dialog's card scrolling this body, so the two panes inside it scroll themselves.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The card wraps its content in a <see cref="ScrollViewer"/> — that is what the control
    /// library's <c>DialogMaxHeight</c> is for, and it is right for a dialog whose content is a
    /// paragraph. It is wrong for this one: a scrolling <see cref="ScrollViewer"/> measures its
    /// child with infinite height, so the body was laid out at the full height of the patch (84 069
    /// px over a 4 000-line file), each inner list was handed exactly the height it asked for, and
    /// one bar moved the file list and the diff together.
    /// </para>
    /// <para>
    /// <see cref="ScrollBarVisibility.Disabled"/> is the one state in which a scroll presenter
    /// measures its child against the room it actually has. With it, the body is bounded by the
    /// card, the two lists get real viewports and their own bars, and the diff's
    /// <c>VirtualizingStackPanel</c> goes back to realising the rows on screen instead of all of
    /// them.
    /// </para>
    /// <para>
    /// Guarded rather than asserted: a future version of the control library that templates its
    /// card differently leaves the page exactly as it behaves today rather than throwing.
    /// </para>
    /// </remarks>
    private void OnDialogBodyAttached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (DiffDialogBody.GetVisualAncestors().OfType<ScrollViewer>().FirstOrDefault() is not { } card)
        {
            return;
        }

        card.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
        card.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
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

        // Another page's columns know nothing of this list's width.
        ReportViewport();
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
