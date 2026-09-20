using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.VisualTree;
using Enigma.GitClient.App.ViewModels.Pages;

namespace Enigma.GitClient.App.Views.Pages;

/// <summary>
/// The commit history page.
/// </summary>
public partial class HistoryPageView : UserControl
{
    private ScrollViewer? _listScroll;

    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    public HistoryPageView()
    {
        InitializeComponent();

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
    /// Follows the page, which is what the columns are measured against.
    /// </summary>
    /// <param name="e">The event.</param>
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        // Another page's columns know nothing of this list's width.
        ReportViewport();
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
