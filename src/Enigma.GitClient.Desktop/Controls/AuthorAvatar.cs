using System.Globalization;
using Avalonia;
using Avalonia.Controls.Primitives;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Enigma.GitClient.Desktop.Controls.Graph;

namespace Enigma.GitClient.Desktop.Controls;

/// <summary>
/// A small monogram standing in for an author's picture.
/// </summary>
/// <remarks>
/// Deliberately generated rather than fetched. A commit carries an email address, and turning that
/// into an avatar means sending a hash of it to a third-party service for every author in the
/// history — a privacy cost and a network dependency that a local git client has no business
/// incurring. A stable colour derived from the name gives the same "scan the column for one person"
/// benefit for nothing.
/// </remarks>
public sealed class AuthorAvatar : TemplatedControl
{
    /// <summary>
    /// The graph lanes monograms are tinted from. The same seed always lands on the same lane. The
    /// colours are the theme's (<c>Themes/Graph.axaml</c>), so an author's colour sits in the history's
    /// own palette and follows the theme with it. The yellow and the grey lanes are left out: neither
    /// reads as a person's colour.
    /// </summary>
    private static readonly int[] Lanes = [0, 1, 2, 3, 4, 5, 7, 8];

    /// <summary>
    /// The tint while the theme cannot be reached: a monogram drawn outside the application's resources.
    /// </summary>
    private static readonly IBrush NoTheme = Brushes.Gray;

    /// <summary>
    /// Defines the <see cref="Initials"/> property.
    /// </summary>
    public static readonly StyledProperty<string?> InitialsProperty =
        AvaloniaProperty.Register<AuthorAvatar, string?>(nameof(Initials));

    /// <summary>
    /// Defines the <see cref="Seed"/> property.
    /// </summary>
    public static readonly StyledProperty<string?> SeedProperty =
        AvaloniaProperty.Register<AuthorAvatar, string?>(nameof(Seed));

    /// <summary>
    /// Defines the read-only <see cref="Tint"/> property.
    /// </summary>
    public static readonly DirectProperty<AuthorAvatar, IBrush> TintProperty =
        AvaloniaProperty.RegisterDirect<AuthorAvatar, IBrush>(nameof(Tint), avatar => avatar.Tint);

    private IBrush _tint = NoTheme;

    static AuthorAvatar()
        => SeedProperty.Changed.AddClassHandler<AuthorAvatar>((avatar, _) => avatar.UpdateTint());

    /// <summary>
    /// Gets or sets the letters shown inside the monogram.
    /// </summary>
    public string? Initials
    {
        get => GetValue(InitialsProperty);
        set => SetValue(InitialsProperty, value);
    }

    /// <summary>
    /// Gets or sets the text the colour is derived from, normally the author's name.
    /// </summary>
    public string? Seed
    {
        get => GetValue(SeedProperty);
        set => SetValue(SeedProperty, value);
    }

    /// <summary>
    /// Gets the brush the monogram is filled with.
    /// </summary>
    public IBrush Tint
    {
        get => _tint;
        private set => SetAndRaise(TintProperty, ref _tint, value);
    }

    /// <summary>
    /// Picks the palette entry a seed maps to.
    /// </summary>
    /// <param name="seed">The text to derive a colour from.</param>
    /// <returns>The palette index.</returns>
    public static int IndexFor(string? seed)
    {
        if (string.IsNullOrEmpty(seed))
        {
            return 0;
        }

        // A small stable hash, computed here rather than taken from string.GetHashCode: that one is
        // randomised per process, so the same author would change colour on every restart.
        uint hash = 2166136261;

        foreach (char character in seed)
        {
            hash = (hash ^ character) * 16777619;
        }

        return (int)(hash % (uint)Lanes.Length);
    }

    /// <inheritdoc />
    /// <remarks>Where the theme's resources first become reachable.</remarks>
    protected override void OnAttachedToLogicalTree(LogicalTreeAttachmentEventArgs e)
    {
        base.OnAttachedToLogicalTree(e);
        UpdateTint();
    }

    /// <summary>
    /// Takes the lane brush itself, whose colour follows the theme variant, so a theme switch recolours
    /// the monogram with nothing to look up again.
    /// </summary>
    private void UpdateTint()
    {
        string key = "GraphLane" + Lanes[IndexFor(Seed)].ToString(CultureInfo.InvariantCulture) + "Brush";
        Tint = GraphPalette.Find(this, key) ?? NoTheme;
    }
}
