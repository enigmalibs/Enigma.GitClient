using Avalonia;
using Avalonia.Controls;
using Enigma.Icons.Phosphor;

namespace Enigma.GitClient.App.Controls;

/// <summary>
/// The placeholder a page shows when it has nothing to display: an icon, a headline, an
/// explanation, and room for the action that resolves it.
/// </summary>
/// <remarks>
/// An empty pane is the most common way a desktop application feels broken. Giving every page one
/// shared, deliberate empty state — and the action that fills it — is cheaper than getting it wrong
/// six times.
/// </remarks>
public sealed class EmptyState : ContentControl
{
    /// <summary>
    /// Defines the <see cref="IconKind"/> property.
    /// </summary>
    public static readonly StyledProperty<PhosphorIcon> IconKindProperty =
        AvaloniaProperty.Register<EmptyState, PhosphorIcon>(nameof(IconKind), PhosphorIcon.Folder);

    /// <summary>
    /// Defines the <see cref="Title"/> property.
    /// </summary>
    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<EmptyState, string?>(nameof(Title));

    /// <summary>
    /// Defines the <see cref="Message"/> property.
    /// </summary>
    public static readonly StyledProperty<string?> MessageProperty =
        AvaloniaProperty.Register<EmptyState, string?>(nameof(Message));

    /// <summary>
    /// Gets or sets the icon shown above the headline.
    /// </summary>
    public PhosphorIcon IconKind
    {
        get => GetValue(IconKindProperty);
        set => SetValue(IconKindProperty, value);
    }

    /// <summary>
    /// Gets or sets the headline.
    /// </summary>
    public string? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    /// <summary>
    /// Gets or sets the sentence explaining why the pane is empty and what to do about it.
    /// </summary>
    public string? Message
    {
        get => GetValue(MessageProperty);
        set => SetValue(MessageProperty, value);
    }
}
