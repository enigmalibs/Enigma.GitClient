using System;
using Avalonia.Data.Converters;

namespace Enigma.GitClient.App.Controls;

/// <summary>
/// How large a dialog drawn over a page is.
/// </summary>
/// <remarks>
/// A dialog that shows a diff wants all the room there is, and it has to keep wanting it while the
/// window is resized — which rules out a fixed size, and rules out auto-sizing too, because a patch
/// has no natural width. What is left is a fraction of whatever the dialog is drawn over, and a
/// binding cannot multiply, so the fraction lives here.
/// </remarks>
public static class DialogSizing
{
    /// <summary>
    /// How much of the page a full-size dialog covers. Not all of it: the strip left around the
    /// card is what makes it read as a dialog over the page rather than as another panel.
    /// </summary>
    public const double Fraction = 0.94;

    /// <summary>
    /// Scales one of the page's dimensions to the dialog's.
    /// </summary>
    /// <remarks>
    /// Floored at zero: a control is measured before it is laid out, and a negative or unset bound
    /// would reach a size property that refuses one.
    /// </remarks>
    public static readonly FuncValueConverter<double, double> Fill =
        new(value => double.IsFinite(value) ? Math.Max(0, value * Fraction) : 0);
}
