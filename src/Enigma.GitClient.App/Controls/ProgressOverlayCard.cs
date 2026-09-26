using System;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls.Primitives;

namespace Enigma.GitClient.App.Controls;

/// <summary>
/// The card shown on the modal overlay while a long git operation runs: what is happening, how far
/// it has got, and a way out.
/// </summary>
/// <remarks>
/// Enigma.Avalonia.Desktop's overlay service takes any control as its content; this is the one this
/// application puts there, so every long operation looks and behaves the same.
/// </remarks>
public sealed class ProgressOverlayCard : TemplatedControl
{
    /// <summary>
    /// Defines the <see cref="Title"/> property.
    /// </summary>
    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<ProgressOverlayCard, string?>(nameof(Title));

    /// <summary>
    /// Defines the <see cref="Message"/> property.
    /// </summary>
    public static readonly StyledProperty<string?> MessageProperty =
        AvaloniaProperty.Register<ProgressOverlayCard, string?>(nameof(Message));

    /// <summary>
    /// Defines the <see cref="Progress"/> property.
    /// </summary>
    public static readonly StyledProperty<double> ProgressProperty =
        AvaloniaProperty.Register<ProgressOverlayCard, double>(nameof(Progress));

    /// <summary>
    /// Defines the <see cref="IsIndeterminate"/> property.
    /// </summary>
    public static readonly StyledProperty<bool> IsIndeterminateProperty =
        AvaloniaProperty.Register<ProgressOverlayCard, bool>(nameof(IsIndeterminate), true);

    /// <summary>
    /// Defines the <see cref="CancelCommand"/> property.
    /// </summary>
    public static readonly StyledProperty<ICommand?> CancelCommandProperty =
        AvaloniaProperty.Register<ProgressOverlayCard, ICommand?>(nameof(CancelCommand));

    /// <summary>
    /// Defines the <see cref="IsCancelVisible"/> property.
    /// </summary>
    public static readonly StyledProperty<bool> IsCancelVisibleProperty =
        AvaloniaProperty.Register<ProgressOverlayCard, bool>(nameof(IsCancelVisible), true);

    /// <summary>
    /// Gets or sets the headline, for example the operation's name.
    /// </summary>
    public string? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    /// <summary>
    /// Gets or sets the detail line, which follows what git is currently reporting.
    /// </summary>
    public string? Message
    {
        get => GetValue(MessageProperty);
        set => SetValue(MessageProperty, value);
    }

    /// <summary>
    /// Gets or sets the completion, from 0 to 100.
    /// </summary>
    public double Progress
    {
        get => GetValue(ProgressProperty);
        set => SetValue(ProgressProperty, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether the bar is indeterminate, which it is until git
    /// reports a percentage.
    /// </summary>
    public bool IsIndeterminate
    {
        get => GetValue(IsIndeterminateProperty);
        set => SetValue(IsIndeterminateProperty, value);
    }

    /// <summary>
    /// Gets or sets the command the cancel button runs.
    /// </summary>
    public ICommand? CancelCommand
    {
        get => GetValue(CancelCommandProperty);
        set => SetValue(CancelCommandProperty, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether the operation can be cancelled.
    /// </summary>
    public bool IsCancelVisible
    {
        get => GetValue(IsCancelVisibleProperty);
        set => SetValue(IsCancelVisibleProperty, value);
    }

    /// <summary>
    /// Applies a clone progress report to the card.
    /// </summary>
    /// <param name="progress">What git reported.</param>
    public void Apply(Core.Repositories.CloneProgress progress)
    {
        ArgumentNullException.ThrowIfNull(progress);

        Message = progress.Message;

        if (progress.Percentage is { } percentage)
        {
            IsIndeterminate = false;
            Progress = percentage;
        }
        else
        {
            IsIndeterminate = true;
        }
    }
}
