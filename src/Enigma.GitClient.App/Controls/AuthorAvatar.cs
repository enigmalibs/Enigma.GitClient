using System;
using Avalonia;
using Avalonia.Controls.Primitives;
using Avalonia.Media;

namespace Enigma.GitClient.App.Controls;

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
    /// The palette monograms are tinted from. The same seed always lands on the same colour.
    /// </summary>
    private static readonly Color[] Palette =
    [
        Color.Parse("#4E9BF5"),
        Color.Parse("#57C77E"),
        Color.Parse("#E8A33D"),
        Color.Parse("#C77DE0"),
        Color.Parse("#F2725B"),
        Color.Parse("#45C2C2"),
        Color.Parse("#8A9BF5"),
        Color.Parse("#EF87B5"),
    ];

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

    private IBrush _tint = new SolidColorBrush(Palette[0]);

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

        return (int)(hash % (uint)Palette.Length);
    }

    private void UpdateTint() => Tint = new SolidColorBrush(Palette[IndexFor(Seed)]);
}
