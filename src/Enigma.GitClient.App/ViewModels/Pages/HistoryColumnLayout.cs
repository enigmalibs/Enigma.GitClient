using System;

namespace Enigma.GitClient.App.ViewModels.Pages;

/// <summary>
/// A column of the history list that the reader can resize.
/// </summary>
/// <remarks>
/// The graph and the message are not here: the graph's width is the number of lanes it has to draw,
/// and the message is the column that takes whatever the others leave.
/// </remarks>
public enum HistoryColumn
{
    /// <summary>The branch and tag badges.</summary>
    Refs,

    /// <summary>The author's avatar and name.</summary>
    Author,

    /// <summary>When the commit was written.</summary>
    Date,

    /// <summary>The abbreviated hash.</summary>
    Sha,
}

/// <summary>
/// The width of every column of the history list, shared by the header and by every row.
/// </summary>
/// <remarks>
/// <para>
/// One set of widths for the whole page, not one per row: an <c>Auto</c> column is measured against
/// the row it is in, so widths handed out per row put the same column at a different x on every
/// line. The rows therefore keep <c>Auto</c> columns and give each cell an explicit width from
/// here — the pattern the graph cell and the badge strip already used before there was a header.
/// </para>
/// <para>
/// The message column is the one that is never set: it is whatever is left over
/// (<see cref="MessageWidth"/>), which is why the list never has to scroll sideways and why a long
/// subject ellipsises instead of pushing the columns after it off screen. Every grip therefore
/// resizes the fixed column it touches and lets the message absorb the difference, which is what
/// makes the boundary follow the pointer whichever side of the message it is on.
/// </para>
/// </remarks>
public sealed class HistoryColumnLayout : ViewModelBase
{
    /// <summary>The space between two columns, which the header and the rows both use.</summary>
    public const double ColumnSpacing = 10;

    /// <summary>How narrow the message column may become before a grip stops moving.</summary>
    public const double MinimumMessageWidth = 120;

    /// <summary>The number of gaps between the six columns.</summary>
    private const int Gaps = 5;

    /// <summary>
    /// Gets the width the graph column needs, which is the lane count rather than a preference.
    /// </summary>
    public double GraphWidth
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                NotifyMessageWidth();
            }
        }
    }

    /// <summary>
    /// Gets the badge column's width.
    /// </summary>
    /// <remarks>
    /// Measured from the badges until the reader drags its grip; from then on it is theirs, because
    /// a column that snapped back to the widest branch name on the next refresh would undo the drag
    /// in front of them.
    /// </remarks>
    public double RefsWidth
    {
        get;
        private set
        {
            if (SetProperty(ref field, value))
            {
                NotifyMessageWidth();
            }
        }
    }

    /// <summary>Gets the author column's width.</summary>
    public double AuthorWidth
    {
        get;
        private set
        {
            if (SetProperty(ref field, value))
            {
                NotifyMessageWidth();
            }
        }
    } = 190;

    /// <summary>Gets the date column's width.</summary>
    public double DateWidth
    {
        get;
        private set
        {
            if (SetProperty(ref field, value))
            {
                NotifyMessageWidth();
            }
        }
    } = 110;

    /// <summary>Gets the hash column's width.</summary>
    public double ShaWidth
    {
        get;
        private set
        {
            if (SetProperty(ref field, value))
            {
                NotifyMessageWidth();
            }
        }
    } = 70;

    /// <summary>
    /// Gets or sets how wide the list's own viewport is — its width less its vertical scrollbar.
    /// </summary>
    /// <remarks>
    /// Reported by the view, because only the view knows whether the scrollbar is there. It is what
    /// the header is drawn at, so the header cannot be a scrollbar's width out of step with the
    /// rows below it.
    /// </remarks>
    public double Viewport
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                OnPropertyChanged(nameof(HeaderWidth));
                NotifyMessageWidth();
            }
        }
    }

    /// <summary>
    /// Gets the width to draw the header at: the list's viewport, or <see cref="double.NaN"/> —
    /// which is <c>Auto</c> — before the list has been laid out and has one to report.
    /// </summary>
    public double HeaderWidth => Viewport > 0 ? Viewport : double.NaN;

    /// <summary>
    /// Gets what the message column is left with, never less than its minimum.
    /// </summary>
    public double MessageWidth => Math.Max(MinimumMessageWidth, Slack + MinimumMessageWidth);

    /// <summary>
    /// Gets how much the message column could give up before it reaches its minimum. Negative when
    /// the window is already too narrow for every column's minimum, and zero before the list has
    /// reported a viewport.
    /// </summary>
    public double Slack
        => Viewport <= 0
            ? 0
            : Viewport
                - GraphWidth
                - RefsWidth
                - AuthorWidth
                - DateWidth
                - ShaWidth
                - (ColumnSpacing * Gaps)
                - MinimumMessageWidth;

    /// <summary>
    /// The narrowest a column may be dragged.
    /// </summary>
    /// <param name="column">The column.</param>
    /// <returns>Its minimum width.</returns>
    /// <remarks>
    /// The badge column's is zero: a history with nothing decorated asks for no column at all, and
    /// a floor would spend width on a column with nothing in it.
    /// </remarks>
    public static double MinimumWidth(HistoryColumn column)
        => column switch
        {
            HistoryColumn.Refs => 0,
            HistoryColumn.Author => 60,
            HistoryColumn.Date => 60,
            HistoryColumn.Sha => 48,
            _ => 0,
        };

    /// <summary>
    /// Offers a measured width for the badge column, which is taken only while the reader has not
    /// resized it themselves.
    /// </summary>
    /// <param name="measured">The width the badges on the loaded rows ask for.</param>
    public void SeedRefsWidth(double measured)
    {
        if (!IsRefsWidthOwnedByReader)
        {
            RefsWidth = Math.Max(0, measured);
        }
    }

    /// <summary>
    /// Moves a column's edge by what the pointer moved.
    /// </summary>
    /// <param name="column">The column the grip belongs to.</param>
    /// <param name="delta">How far the pointer moved, positive to the right.</param>
    /// <remarks>
    /// The badge column sits before the message and grows to the right; the author, the date and
    /// the hash sit after it and grow to the left. Either way the message absorbs the difference,
    /// so the edge under the pointer is the edge that moves.
    /// </remarks>
    public void Resize(HistoryColumn column, double delta)
    {
        double current = WidthOf(column);
        double target = current + (column == HistoryColumn.Refs ? delta : -delta);

        target = Math.Max(target, MinimumWidth(column));

        // Growing is only possible with room the message can spare; shrinking always is.
        target = Math.Min(target, current + Math.Max(Slack, 0));

        SetWidth(column, target);

        if (column == HistoryColumn.Refs)
        {
            IsRefsWidthOwnedByReader = true;
        }
    }

    /// <summary>
    /// Gets a value indicating whether the reader has resized the badge column, after which the
    /// measured width no longer overrides it.
    /// </summary>
    public bool IsRefsWidthOwnedByReader { get; private set; }

    /// <summary>
    /// The width a column is drawn at.
    /// </summary>
    /// <param name="column">The column.</param>
    /// <returns>Its width.</returns>
    public double WidthOf(HistoryColumn column)
        => column switch
        {
            HistoryColumn.Refs => RefsWidth,
            HistoryColumn.Author => AuthorWidth,
            HistoryColumn.Date => DateWidth,
            HistoryColumn.Sha => ShaWidth,
            _ => 0,
        };

    private void SetWidth(HistoryColumn column, double width)
    {
        switch (column)
        {
            case HistoryColumn.Refs:
                RefsWidth = width;
                break;

            case HistoryColumn.Author:
                AuthorWidth = width;
                break;

            case HistoryColumn.Date:
                DateWidth = width;
                break;

            case HistoryColumn.Sha:
                ShaWidth = width;
                break;

            default:
                break;
        }
    }

    private void NotifyMessageWidth()
    {
        OnPropertyChanged(nameof(Slack));
        OnPropertyChanged(nameof(MessageWidth));
    }
}
