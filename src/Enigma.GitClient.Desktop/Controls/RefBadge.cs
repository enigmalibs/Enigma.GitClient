using System;
using Avalonia;
using Avalonia.Controls.Primitives;
using Enigma.GitClient.Core.Refs;
using Enigma.Icons.Phosphor;

namespace Enigma.GitClient.Desktop.Controls;

/// <summary>
/// The pill a graph row shows for a branch, a tag or the stash pointing at its commit.
/// </summary>
/// <remarks>
/// A local branch whose upstream is on the same commit is one pill, as GitKraken draws it: the local
/// branch's, with the remote's cloud beside the branch icon — <see cref="Upstream"/> names it.
/// </remarks>
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

    /// <summary>
    /// Defines the <see cref="Upstream"/> property.
    /// </summary>
    public static readonly StyledProperty<string?> UpstreamProperty =
        AvaloniaProperty.Register<RefBadge, string?>(nameof(Upstream));

    /// <summary>
    /// Defines the <see cref="IsWorktree"/> property.
    /// </summary>
    public static readonly StyledProperty<bool> IsWorktreeProperty =
        AvaloniaProperty.Register<RefBadge, bool>(nameof(IsWorktree));

    /// <summary>
    /// Defines the <see cref="WorktreeTip"/> property.
    /// </summary>
    public static readonly StyledProperty<string?> WorktreeTipProperty =
        AvaloniaProperty.Register<RefBadge, string?>(nameof(WorktreeTip));

    /// <summary>
    /// Defines the read-only <see cref="HasUpstream"/> property, which the control template binds.
    /// </summary>
    public static readonly DirectProperty<RefBadge, bool> HasUpstreamProperty =
        AvaloniaProperty.RegisterDirect<RefBadge, bool>(nameof(HasUpstream), badge => badge.HasUpstream);

    /// <summary>
    /// Defines the read-only <see cref="Description"/> property, which the control template binds.
    /// </summary>
    public static readonly DirectProperty<RefBadge, string?> DescriptionProperty =
        AvaloniaProperty.RegisterDirect<RefBadge, string?>(nameof(Description), badge => badge.Description);

    private bool _hasUpstream;
    private string? _description;

    static RefBadge()
    {
        KindProperty.Changed.AddClassHandler<RefBadge>((badge, _) => badge.UpdateClasses());
        IsCurrentProperty.Changed.AddClassHandler<RefBadge>((badge, _) => badge.UpdateClasses());
        TextProperty.Changed.AddClassHandler<RefBadge>((badge, _) => badge.UpdateDescription());
        UpstreamProperty.Changed.AddClassHandler<RefBadge>((badge, _) => badge.UpdateDescription());
        WorktreeTipProperty.Changed.AddClassHandler<RefBadge>((badge, _) => badge.UpdateDescription());
    }

    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    public RefBadge()
    {
        UpdateClasses();
        UpdateDescription();
    }

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
    /// Gets or sets the upstream drawn in the same pill — <c>origin/main</c> beside <c>main</c>, both on
    /// this commit — or <see langword="null"/> for a badge of its own.
    /// </summary>
    public string? Upstream
    {
        get => GetValue(UpstreamProperty);
        set => SetValue(UpstreamProperty, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether the branch is checked out in another working tree, which
    /// the pill marks with a tree after the check and before the branch icon.
    /// </summary>
    public bool IsWorktree
    {
        get => GetValue(IsWorktreeProperty);
        set => SetValue(IsWorktreeProperty, value);
    }

    /// <summary>
    /// Gets or sets where the branch is checked out when it is in another working tree, which the
    /// tooltip adds on a line of its own; <see langword="null"/> otherwise.
    /// </summary>
    public string? WorktreeTip
    {
        get => GetValue(WorktreeTipProperty);
        set => SetValue(WorktreeTipProperty, value);
    }

    /// <summary>
    /// Gets a value indicating whether the pill draws an upstream too, and so its cloud icon.
    /// </summary>
    public bool HasUpstream
    {
        get => _hasUpstream;
        private set => SetAndRaise(HasUpstreamProperty, ref _hasUpstream, value);
    }

    /// <summary>
    /// Gets what the pill stands for, in words — its tooltip and its accessible name: the label, the
    /// upstream when it draws one, and where the branch is checked out when another working tree has it.
    /// </summary>
    public string? Description
    {
        get => _description;
        private set => SetAndRaise(DescriptionProperty, ref _description, value);
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

    private void UpdateDescription()
    {
        HasUpstream = Upstream is { Length: > 0 };

        string? name = HasUpstream ? $"{Text} and {Upstream}" : Text;
        Description = WorktreeTip is { Length: > 0 } worktree ? $"{name}\n{worktree}" : name;
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
