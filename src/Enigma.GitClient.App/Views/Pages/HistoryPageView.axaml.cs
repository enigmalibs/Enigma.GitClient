using System;
using System.ComponentModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Enigma.GitClient.App.ViewModels.Pages;

namespace Enigma.GitClient.App.Views.Pages;

/// <summary>
/// The commit history page.
/// </summary>
public partial class HistoryPageView : UserControl
{
    private HistoryPageViewModel? _page;
    private ScrollViewer? _listScroll;
    private Vector? _offsetBeforeReplace;

    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    public HistoryPageView()
    {
        InitializeComponent();

        // Tunnelling, and on the page rather than on the panel: Escape has to leave the diffs
        // whatever inside them has the key — the file filter box, the patch, a list — and before
        // any of them can handle it first.
        AddHandler(KeyDownEvent, OnPageKeyDown, RoutingStrategies.Tunnel);

        // Tunnelling too, so the line's menu is rebuilt before it opens: what it offers depends on
        // things that change while the line is on screen — the host's name, read after the rows were.
        AddHandler(ContextRequestedEvent, OnContextRequested, RoutingStrategies.Tunnel);

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
    /// Follows the page: the columns are measured against it, and the diffs opening is what moves
    /// the focus.
    /// </summary>
    /// <param name="e">The event.</param>
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (_page is not null)
        {
            _page.PropertyChanged -= OnPagePropertyChanged;
            _page.RowsReplacing -= OnRowsReplacing;
            _page.RowsReplaced -= OnRowsReplaced;
        }

        _page = DataContext as HistoryPageViewModel;

        if (_page is not null)
        {
            _page.PropertyChanged += OnPagePropertyChanged;
            _page.RowsReplacing += OnRowsReplacing;
            _page.RowsReplaced += OnRowsReplaced;
        }

        // Another page's columns know nothing of this list's width.
        ReportViewport();
    }

    // ---------------------------------------------------------------- keeping the reader's place

    /// <summary>
    /// Remembers where the list was scrolled to before a refresh replaces its rows.
    /// </summary>
    private void OnRowsReplacing(object? sender, EventArgs e) => _offsetBeforeReplace = _listScroll?.Offset;

    /// <summary>
    /// Puts the list back where it was once the new rows are in. Posted, because the rows are measured
    /// in the layout pass that follows, and an offset past what has been measured is clamped away.
    /// </summary>
    private void OnRowsReplaced(object? sender, EventArgs e)
    {
        if (_offsetBeforeReplace is not { } offset)
        {
            return;
        }

        _offsetBeforeReplace = null;

        Dispatcher.UIThread.Post(
            () =>
            {
                if (_listScroll is not null)
                {
                    _listScroll.Offset = offset;
                }
            },
            DispatcherPriority.Loaded);
    }

    // ---------------------------------------------------------------- leaving the diffs

    private void OnPagePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(HistoryPageViewModel.IsDiffViewOpen) or null)
        {
            MoveFocus(_page?.IsDiffViewOpen ?? false);
        }
    }

    /// <summary>
    /// Puts the focus where the keys should go: into the diffs while they are up, and back on the
    /// graph when they are not.
    /// </summary>
    /// <param name="isOpen">Whether the diffs are on screen.</param>
    /// <remarks>
    /// This is what makes Escape work on the first press. A key event is routed to whatever has
    /// focus; with the focus still on the list — or nowhere at all, which is where a freshly shown
    /// window leaves it — the route never passes through this page, and the key reached nothing
    /// until the reader happened to click inside the diffs first.
    ///
    /// Posted rather than called: the panel is collapsed until the layout pass that follows this
    /// notification, and a control that is not visible cannot take the focus.
    /// </remarks>
    private void MoveFocus(bool isOpen)
        => Dispatcher.UIThread.Post(() =>
        {
            if (isOpen && DiffPage.IsVisible)
            {
                DiffPage.Focus();
                return;
            }

            if (!isOpen && !DiffPage.IsVisible && CommitList.IsVisible)
            {
                CommitList.Focus();
            }
        });

    /// <summary>
    /// Leaves the diffs on Escape.
    /// </summary>
    /// <param name="sender">The page.</param>
    /// <param name="e">The key.</param>
    /// <remarks>
    /// Handled here so that nothing below can claim the key first, and only while the diffs are on
    /// screen: Escape on the graph itself belongs to whatever the reader is using — a context menu,
    /// a tooltip, the shell.
    /// </remarks>
    private void OnPageKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || _page is not { IsDiffViewOpen: true } page)
        {
            return;
        }

        page.IsDiffViewOpen = false;
        e.Handled = true;
    }

    /// <summary>
    /// Asks the line under a right-click to rebuild its menu before the menu opens.
    /// </summary>
    /// <param name="sender">The page.</param>
    /// <param name="e">The request, which is left for the menu to handle.</param>
    private void OnContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (e.Source is Visual source
            && source.GetSelfAndVisualAncestors().OfType<Control>().FirstOrDefault(control => control.DataContext is CommitRowViewModel)
                is { DataContext: CommitRowViewModel row })
        {
            row.RefreshMenu();
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
