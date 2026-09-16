using System;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Enigma.GitClient.App.ViewModels.Pages;

namespace Enigma.GitClient.App.Views.Pages;

/// <summary>
/// The commit history page.
/// </summary>
public partial class HistoryPageView : UserControl
{
    /// <summary>
    /// How tall the detail panel opens, before anyone has dragged the splitter.
    /// </summary>
    public const double DefaultDetailsHeight = 300;

    /// <summary>
    /// The least the detail panel may be dragged to. Below this it shows a header and nothing else,
    /// which is worse than not opening it at all.
    /// </summary>
    public const double MinimumDetailsHeight = 140;

    /// <summary>
    /// The workspace row the detail panel occupies.
    /// </summary>
    private const int DetailsRowIndex = 2;

    private readonly RowDefinition _detailsRow;
    private double _detailsHeight = DefaultDetailsHeight;
    private HistoryPageViewModel? _page;

    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    public HistoryPageView()
    {
        InitializeComponent();

        _detailsRow = Workspace.RowDefinitions[DetailsRowIndex];
    }

    /// <summary>
    /// Gets the height the detail panel will open at — the default until the splitter has been
    /// dragged, and whatever it was left at afterwards.
    /// </summary>
    public double DetailsHeight => _detailsHeight;

    /// <summary>
    /// Follows the page's selection, which is what decides whether the detail panel has a height at
    /// all.
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

        ApplyDetailsHeight(_page?.HasSelection ?? false);
    }

    private void OnPagePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(HistoryPageViewModel.HasSelection) or null)
        {
            ApplyDetailsHeight(_page?.HasSelection ?? false);
        }
    }

    /// <summary>
    /// Opens the detail row to the remembered height, or collapses it.
    /// </summary>
    /// <param name="hasSelection">Whether a commit is selected.</param>
    /// <remarks>
    /// The height lives on the row rather than on the panel because the row is what the splitter
    /// writes to. Collapsing therefore has to read back whatever the splitter left behind first, or
    /// the next selection would throw the reader's own size away.
    /// </remarks>
    private void ApplyDetailsHeight(bool hasSelection)
    {
        if (hasSelection)
        {
            _detailsRow.MinHeight = MinimumDetailsHeight;
            _detailsRow.Height = new GridLength(_detailsHeight, GridUnitType.Pixel);

            return;
        }

        if (_detailsRow.Height is { IsAbsolute: true, Value: > 0 } dragged)
        {
            _detailsHeight = dragged.Value;
        }

        // The minimum goes with it: a row that must be 140 tall is not collapsed.
        _detailsRow.MinHeight = 0;
        _detailsRow.Height = new GridLength(0, GridUnitType.Pixel);
    }

    /// <summary>
    /// Checks out whatever the double-clicked row stands for.
    /// </summary>
    /// <remarks>
    /// A double-click is a gesture, not state, so it has nowhere to live but here. The handler does
    /// no work of its own: it finds the row and runs the command the ViewModel already exposes, so
    /// the behaviour stays testable without a pointer.
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
