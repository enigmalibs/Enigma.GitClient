using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Media;
using Enigma.GitClient.Core.Configuration;

namespace Enigma.GitClient.App.Controls.Diff;

/// <summary>
/// Everything a diff needs to know about the face it is drawn in: the family and size the reader
/// chose, the width of one character in it, and the sizes and widths the rest of a diff row is
/// derived from.
/// </summary>
/// <param name="FontFamily">The face the diff text is drawn in.</param>
/// <param name="FontSize">How large that text is.</param>
/// <param name="CharacterWidth">
/// The advance width of one character of <paramref name="FontFamily"/> at
/// <paramref name="FontSize"/>. Exact for a monospace face, which is what the diff assumes
/// everywhere it expands a tab to a column grid.
/// </param>
/// <param name="LineNumberFontSize">How large the gutter's line numbers are.</param>
/// <param name="GutterWidth">How wide one line-number column is.</param>
/// <param name="MarkerWidth">How wide the <c>+</c>/<c>−</c> column is.</param>
public sealed record DiffMetrics(
    FontFamily FontFamily,
    double FontSize,
    double CharacterWidth,
    double LineNumberFontSize,
    double GutterWidth,
    double MarkerWidth);

/// <summary>
/// Turns the two font preferences into the metrics a diff row is drawn from, and puts them on the
/// application as resources.
/// </summary>
/// <remarks>
/// <para>
/// Resources rather than bindings, for the same reason <c>App.ApplyTheme</c> is an application-
/// level change: one preference then reaches every <see cref="DiffLineText"/> in the application —
/// the diff viewer's two renderings and the conflict-resolution page's three panes alike — without
/// a binding per row in six templates, and without the diff viewer owning typography that is not
/// its own.
/// </para>
/// <para>
/// The derived numbers are not decoration. A gutter fixed at 48 px is a gutter that clips a
/// six-digit line number the moment the face grows, so the column widths are computed from the
/// measured character width instead of guessed once.
/// </para>
/// </remarks>
public static class DiffTypography
{
    /// <summary>The resource key the diff text's face is published under.</summary>
    public const string FontFamilyKey = "DiffFontFamily";

    /// <summary>The resource key the diff text's size is published under.</summary>
    public const string FontSizeKey = "DiffFontSize";

    /// <summary>The resource key the gutter's line-number size is published under.</summary>
    public const string LineNumberFontSizeKey = "DiffLineNumberFontSize";

    /// <summary>The resource key one line-number column's width is published under.</summary>
    public const string GutterWidthKey = "DiffGutterWidth";

    /// <summary>The resource key the marker column's width is published under.</summary>
    public const string MarkerWidthKey = "DiffMarkerWidth";

    /// <summary>
    /// The resource key holding the application's own monospace stack, which is what an empty font
    /// preference means.
    /// </summary>
    public const string MonospaceFontFamilyKey = "MonospaceFontFamily";

    /// <summary>
    /// The monospace stack to fall back on when the application has no resources yet — the same
    /// list <c>App.axaml</c> publishes, so a diff measured before the application is up matches one
    /// measured after it.
    /// </summary>
    public const string FallbackFontFamily =
        "Cascadia Mono, JetBrains Mono, DejaVu Sans Mono, Consolas, Menlo, monospace";

    /// <summary>
    /// How many digits a gutter is made wide enough for, plus the padding its style already adds.
    /// Six digits is a million-line file, which is past anything a viewer opens.
    /// </summary>
    private const int GutterDigits = 6;

    private const double GutterPadding = 8;

    /// <summary>
    /// What a character's width is taken to be when the face cannot be measured at all: roughly
    /// what a monospace advance is as a fraction of the size. Only a face the font manager lists
    /// but cannot realise ever reaches it.
    /// </summary>
    private const double EstimatedAdvanceRatio = 0.6;

    private static readonly ConcurrentDictionary<(string Family, double Size), double> Widths = new();

    private static DiffMetrics? current;

    /// <summary>
    /// Gets the metrics the application is currently drawing diffs with.
    /// </summary>
    /// <remarks>
    /// Measured on first use rather than in a static initialiser: measuring needs a font manager,
    /// and a type initialiser can run long before there is an Avalonia platform to ask.
    /// </remarks>
    public static DiffMetrics Current
        => current ??= Measure(string.Empty, AppSettings.Defaults.DiffFontSize);

    /// <summary>
    /// Raised after <see cref="Current"/> changes, so a view holding a derived measurement — a
    /// viewport counted in characters, for instance — can take the new one.
    /// </summary>
    public static event EventHandler? Changed;

    /// <summary>
    /// Takes the stored font preferences on and publishes them as application resources.
    /// </summary>
    /// <param name="settings">The preferences.</param>
    public static void Apply(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (Application.Current is not { } application)
        {
            // No application, no font manager and nothing to publish to: a test that never stood
            // one up keeps the metrics it already has.
            return;
        }

        DiffMetrics metrics = Measure(settings.DiffFontFamily, settings.DiffFontSize);

        application.Resources[FontFamilyKey] = metrics.FontFamily;
        application.Resources[FontSizeKey] = metrics.FontSize;
        application.Resources[LineNumberFontSizeKey] = metrics.LineNumberFontSize;
        application.Resources[GutterWidthKey] = metrics.GutterWidth;
        application.Resources[MarkerWidthKey] = metrics.MarkerWidth;

        current = metrics;

        Changed?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>
    /// Works out the metrics for a preference, without publishing them.
    /// </summary>
    /// <param name="family">The chosen family name, empty for the application's monospace stack.</param>
    /// <param name="size">The chosen size.</param>
    /// <returns>The metrics.</returns>
    public static DiffMetrics Measure(string? family, double size)
    {
        FontFamily face = Resolve(family);
        double fontSize = Math.Clamp(size, 8, 32);
        double lineNumberSize = Math.Max(9, fontSize - 2);
        double characterWidth = MeasureCharacterWidth(face, fontSize);
        double digitWidth = MeasureCharacterWidth(face, lineNumberSize);

        return new DiffMetrics(
            face,
            fontSize,
            characterWidth,
            lineNumberSize,
            Math.Ceiling((digitWidth * GutterDigits) + GutterPadding),
            Math.Ceiling(characterWidth + GutterPadding));
    }

    /// <summary>
    /// Measures the advance width of one character of a face at a size.
    /// </summary>
    /// <param name="family">The face.</param>
    /// <param name="size">The size.</param>
    /// <returns>The width, never zero.</returns>
    /// <remarks>
    /// Cached per face and size: a diff asks this question for every pane of every patch, and the
    /// answer only changes when the preference does.
    /// </remarks>
    public static double MeasureCharacterWidth(FontFamily family, double size)
    {
        ArgumentNullException.ThrowIfNull(family);

        return Widths.GetOrAdd(
            (family.ToString(), size),
            key => TryMeasure("0", family, key.Size, out double width)
                ? Math.Max(width, 1)
                : Math.Max(key.Size * EstimatedAdvanceRatio, 1));
    }

    /// <summary>
    /// Answers whether a face draws every character at the same width, which is what the diff's
    /// column grid — expanded tabs, aligned indentation, a scroll offset counted in characters —
    /// assumes of the face it is given.
    /// </summary>
    /// <param name="family">The face.</param>
    /// <returns><see langword="true"/> when the face is monospace.</returns>
    public static bool IsMonospace(FontFamily family)
    {
        ArgumentNullException.ThrowIfNull(family);

        const double probeSize = 16;

        // A family the font manager lists but cannot realise is not a face anyone can be offered,
        // whatever its metrics would have been.
        if (!TryMeasure("i", family, probeSize, out double narrow)
            || !TryMeasure("W", family, probeSize, out double wide))
        {
            return false;
        }

        return narrow > 0 && Math.Abs(narrow - wide) < 0.01;
    }

    /// <summary>
    /// Lists the monospace faces installed on this machine, in name order.
    /// </summary>
    /// <returns>The families, which may be empty on a machine with no monospace face at all.</returns>
    public static IReadOnlyList<FontFamily> MonospaceFamilies()
        => [.. FontManager.Current.SystemFonts.Where(IsMonospace).OrderBy(family => family.Name, StringComparer.CurrentCulture)];

    private static FontFamily Resolve(string? family)
    {
        if (!string.IsNullOrWhiteSpace(family))
        {
            // Avalonia falls back through its own stack for a name no font manager knows, so a
            // preference naming a face this machine lost still draws.
            return new FontFamily(family);
        }

        if (Application.Current?.Resources.TryGetResource(MonospaceFontFamilyKey, null, out object? stack) == true
            && stack is FontFamily monospace)
        {
            return monospace;
        }

        return new FontFamily(FallbackFontFamily);
    }

    /// <summary>
    /// Measures a string in a face, reporting failure rather than throwing.
    /// </summary>
    /// <remarks>
    /// The system font collection lists families it cannot always turn into glyphs — a name the
    /// platform enumerates but whose file is missing or unreadable — and asking one of those for a
    /// typeface throws. A font list is not worth taking a settings page down for, so the failure is
    /// an answer here rather than an exception.
    /// </remarks>
    private static bool TryMeasure(string text, FontFamily family, double size, out double width)
    {
        try
        {
            width = new FormattedText(
                text,
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                new Typeface(family),
                size,
                Brushes.Black).Width;

            return true;
        }
        catch (InvalidOperationException)
        {
            width = 0;
            return false;
        }
    }
}
