using System;
using Avalonia;
using Avalonia.Controls.Primitives;
using Enigma.GitClient.Core.Refs;
using Enigma.Icons.Phosphor;

namespace Enigma.GitClient.App.Controls;

/// <summary>
/// The pill a graph row shows for a branch, a tag or the stash pointing at its commit.
/// </summary>
public sealed class RefBadge : TemplatedControl
{
    /// <summary>
    /// Defines the <see cref="Kind"/> property.
    /// </summary>
    public static readonly StyledProperty<GitRefKind> KindProperty =
        AvaloniaProperty.Register<RefBadge, GitRefKind>(nameof(Kind), GitRefKind.LocalBranch);

    /// <summary>
    /// Defines the <see cref="Text"/> property.
    /// </summary>
    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<RefBadge, string?>(nameof(Text));

    /// <summary>
    /// Defines the <see cref="IsCurrent"/> property.
    /// </summary>
    public static readonly StyledProperty<bool> IsCurrentProperty =
        AvaloniaProperty.Register<RefBadge, bool>(nameof(IsCurrent));

    static RefBadge()
    {
        KindProperty.Changed.AddClassHandler<RefBadge>((badge, _) => badge.UpdateClasses());
        IsCurrentProperty.Changed.AddClassHandler<RefBadge>((badge, _) => badge.UpdateClasses());
    }

    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    public RefBadge() => UpdateClasses();

    /// <summary>
    /// Gets or sets what kind of reference the badge stands for, which decides its colour and icon.
    /// </summary>
    public GitRefKind Kind
    {
        get => GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    /// <summary>
    /// Gets or sets the label — the ref's short name.
    /// </summary>
    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether this is the branch HEAD points at.
    /// </summary>
    public bool IsCurrent
    {
        get => GetValue(IsCurrentProperty);
        set => SetValue(IsCurrentProperty, value);
    }

    /// <summary>
    /// Defines the read-only <see cref="IconKind"/> property, which the control template binds.
    /// </summary>
    public static readonly DirectProperty<RefBadge, PhosphorIcon> IconKindProperty =
        AvaloniaProperty.RegisterDirect<RefBadge, PhosphorIcon>(nameof(IconKind), badge => badge.IconKind);

    private PhosphorIcon _iconKind = PhosphorIcon.GitBranch;

    /// <summary>
    /// Gets the icon shown inside the badge, derived from <see cref="Kind"/>.
    /// </summary>
    public PhosphorIcon IconKind
    {
        get => _iconKind;
        private set => SetAndRaise(IconKindProperty, ref _iconKind, value);
    }

    /// <summary>
    /// Builds a badge from a reference.
    /// </summary>
    /// <param name="reference">The reference to show.</param>
    /// <returns>The badge.</returns>
    public static RefBadge For(GitRef reference)
    {
        ArgumentNullException.ThrowIfNull(reference);

        return new RefBadge
        {
            Kind = reference.Kind,
            Text = reference.ShortName,
            IsCurrent = reference is GitBranch { IsCurrent: true },
        };
    }

    /// <summary>
    /// Keeps the style classes in step with the properties, so the theme can select on them.
    /// </summary>
    private void UpdateClasses()
    {
        Classes.Set("head", IsCurrent);
        Classes.Set("local", !IsCurrent && Kind == GitRefKind.LocalBranch);
        Classes.Set("remote", Kind == GitRefKind.RemoteBranch);
        Classes.Set("tag", Kind == GitRefKind.Tag);
        Classes.Set("stash", Kind is GitRefKind.Stash or GitRefKind.Note or GitRefKind.Other);

        IconKind = Kind switch
        {
            GitRefKind.RemoteBranch => PhosphorIcon.CloudArrowDown,
            GitRefKind.Tag => PhosphorIcon.Tag,
            GitRefKind.Stash => PhosphorIcon.Archive,
            GitRefKind.Note => PhosphorIcon.Note,
            _ => PhosphorIcon.GitBranch,
        };
    }
}
