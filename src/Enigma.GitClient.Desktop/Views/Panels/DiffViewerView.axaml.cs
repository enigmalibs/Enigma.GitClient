using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Enigma.GitClient.Desktop.Controls.Diff;
using Enigma.GitClient.Desktop.ViewModels.Panels;

namespace Enigma.GitClient.Desktop.Views.Panels;

/// <summary>
/// The colour-coded diff viewer, unified or side by side.
/// </summary>
/// <remarks>
/// <para>
/// Both renderings are read-only editors (<see cref="DiffTextEditor"/>): one for the unified patch, two
/// for the side-by-side one. Selecting, copying, searching and scrolling sideways are the editors' own,
/// and the selection is mirrored into the ViewModel's (<see cref="DiffRenderOptions.Selection"/>).
/// Nothing here ever changes the text.
/// </para>
/// <para>
/// The code behind this view is for what the bindings cannot do: telling each minimap which part of
/// its patch is on screen and scrolling the patch when the map is pressed, putting a newly opened
/// file at its first change, and keeping the two sides of the side-by-side rendering scrolled
/// together.
/// </para>
/// </remarks>
public partial class DiffViewerView : UserControl
{
    /// <summary>
    /// How many rows are kept above the first change when a file opens. Enough that the change is
    /// not on the very first pixel, little enough that it is still where the eye lands.
    /// </summary>
    private const int ContextRows = 2;

    /// <summary>
    /// The scroll each map drives, once its editor has a template to find one in.
    /// </summary>
    private readonly Dictionary<DiffMinimap, ScrollViewer> _scrolls = [];

    // Held for the view's lifetime: the link is what the two editors' scroll handlers call into.
    private readonly DiffScrollLink _sides;

    private DiffViewerViewModel? _viewer;

    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    public DiffViewerView()
    {
        InitializeComponent();

        Maps(UnifiedEditor, UnifiedMinimap);

        // The map follows the old side and drives it; the link takes the new side along.
        Maps(LeftEditor, SideBySideMinimap);
        _sides = new DiffScrollLink(LeftEditor, RightEditor);
    }

    // ---------------------------------------------------------------- the minimap

    /// <summary>
    /// Ties a rendering's minimap to an editor's scroll, in both directions.
    /// </summary>
    /// <param name="editor">The editor showing the patch.</param>
    /// <param name="map">The map beside it.</param>
    /// <remarks>
    /// <para>
    /// Here rather than in the control, and here rather than in the ViewModel: what part of a patch is
    /// on screen is a fact about a laid-out editor, which is a thing only the view has. The map is told
    /// it in fractions and knows nothing about where they came from.
    /// </para>
    /// <para>
    /// The editor's text area is a logical scrollable that counts in pixels, so its scroll viewer's
    /// offset, extent and viewport are the patch's own.
    /// </para>
    /// </remarks>
    private void Maps(DiffTextEditor editor, DiffMinimap map)
    {
        editor.TemplateApplied += (_, _) =>
        {
            if (editor.ScrollHost is not { } scroll)
            {
                return;
            }

            _scrolls[map] = scroll;
            scroll.ScrollChanged += (_, _) => Report(scroll, map);

            Report(scroll, map);
        };

        map.ScrollRequested += (_, start) =>
        {
            if (_scrolls.TryGetValue(map, out ScrollViewer? scroll))
            {
                ScrollTo(scroll, start);
            }
        };
    }

    /// <summary>
    /// Tells a map which part of its patch is on screen.
    /// </summary>
    private static void Report(ScrollViewer scroll, DiffMinimap map)
    {
        double extent = scroll.Extent.Height;

        if (extent <= 0)
        {
            // Nothing to scroll: the whole of it is on screen, which is what a full window says.
            map.ViewportStart = 0;
            map.ViewportEnd = 1;
            return;
        }

        map.ViewportStart = Math.Clamp(scroll.Offset.Y / extent, 0, 1);
        map.ViewportEnd = Math.Clamp((scroll.Offset.Y + scroll.Viewport.Height) / extent, 0, 1);
    }

    /// <summary>
    /// Scrolls a rendering so its view starts at a fraction of the patch.
    /// </summary>
    private static void ScrollTo(ScrollViewer scroll, double start)
    {
        double furthest = Math.Max(0, scroll.Extent.Height - scroll.Viewport.Height);

        scroll.Offset = new Vector(
            scroll.Offset.X,
            Math.Clamp(start * scroll.Extent.Height, 0, furthest));
    }

    /// <inheritdoc />
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (_viewer is not null)
        {
            _viewer.PatchChanged -= OnPatchChanged;
        }

        _viewer = DataContext as DiffViewerViewModel;

        if (_viewer is not null)
        {
            _viewer.PatchChanged += OnPatchChanged;
        }

        ShowFirstChange();
    }

    // ---------------------------------------------------------------- where a file opens

    private void OnPatchChanged(object? sender, EventArgs e) => ShowFirstChange();

    /// <summary>
    /// Puts both renderings where their first change is.
    /// </summary>
    /// <remarks>
    /// <para>
    /// An editor keeps the offset it had, so without this a newly opened file starts wherever the
    /// last one was left — a third of the way down a long patch is a plausible-looking place to land
    /// in a short one, which is what made the position look random.
    /// </para>
    /// <para>
    /// Posted, and laid out first: the documents have only just been built. Both renderings are moved,
    /// not only the one on screen, so switching between them after the file opens does not land
    /// somewhere else — and both sides of the side-by-side one, so they open level without waiting
    /// for the link to bring the second along.
    /// </para>
    /// </remarks>
    private void ShowFirstChange()
        => Dispatcher.UIThread.Post(
            () =>
            {
                if (DataContext is not DiffViewerViewModel viewer)
                {
                    return;
                }

                Align(UnifiedEditor, viewer.UnifiedFirstChangeRow);
                Align(LeftEditor, viewer.SideBySideFirstChangeRow);
                Align(RightEditor, viewer.SideBySideFirstChangeRow);
            },
            DispatcherPriority.Background);

    /// <summary>
    /// Scrolls an editor to the row its first change begins at.
    /// </summary>
    /// <param name="editor">The editor.</param>
    /// <param name="firstChangeRow">The row that change begins at.</param>
    /// <remarks>
    /// One move, and exact: an editor knows where every line is, where a virtualising list only knew
    /// the rows it had realised. A row is a document line, so the line to put at the top is the row
    /// <see cref="ContextRows"/> above the change.
    /// </remarks>
    private static void Align(DiffTextEditor editor, int firstChangeRow)
    {
        editor.UpdateLayout();
        editor.ScrollToRow(Math.Max(0, firstChangeRow - ContextRows));
    }
}
