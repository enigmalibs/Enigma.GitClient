using System;
using Enigma.GitClient.Core.History;

namespace Enigma.GitClient.Core.Refs;

/// <summary>
/// A tag, either lightweight (a bare pointer to a commit) or annotated (its own object carrying a
/// tagger and a message).
/// </summary>
public sealed class GitTag : GitRef
{
    /// <summary>
    /// The ref namespace prefix of a tag.
    /// </summary>
    public const string Prefix = "refs/tags/";

    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="fullName">The full ref name.</param>
    /// <param name="targetSha">The SHA of the commit the tag points at.</param>
    /// <param name="tagObjectSha">
    /// The SHA of the tag object for an annotated tag, or <see langword="null"/> for a lightweight
    /// tag.
    /// </param>
    /// <param name="tagger">Who created an annotated tag, or <see langword="null"/>.</param>
    /// <param name="message">The tag message subject, empty for a lightweight tag.</param>
    /// <param name="targetDate">The commit date of the tagged commit.</param>
    public GitTag(
        string fullName,
        string targetSha,
        string? tagObjectSha,
        GitSignature? tagger,
        string message,
        DateTimeOffset targetDate)
        : base(fullName, targetSha)
    {
        TagObjectSha = tagObjectSha;
        Tagger = tagger;
        Message = message ?? string.Empty;
        TargetDate = targetDate;
    }

    /// <inheritdoc />
    public override GitRefKind Kind => GitRefKind.Tag;

    /// <inheritdoc />
    public override string ShortName
        => FullName.StartsWith(Prefix, StringComparison.Ordinal)
            ? FullName.Substring(Prefix.Length)
            : FullName;

    /// <summary>
    /// Gets a value indicating whether the tag has its own object, and so a tagger and a message.
    /// </summary>
    public bool IsAnnotated => TagObjectSha is not null;

    /// <summary>
    /// Gets the SHA of the tag object, or <see langword="null"/> for a lightweight tag.
    /// </summary>
    public string? TagObjectSha { get; }

    /// <summary>
    /// Gets who created an annotated tag, or <see langword="null"/> for a lightweight tag.
    /// </summary>
    public GitSignature? Tagger { get; }

    /// <summary>
    /// Gets the tag message subject, empty for a lightweight tag.
    /// </summary>
    public string Message { get; }

    /// <summary>
    /// Gets the commit date of the tagged commit, which is what the tags list sorts on so
    /// lightweight and annotated tags order consistently.
    /// </summary>
    public DateTimeOffset TargetDate { get; }
}

/// <summary>
/// Any reference that is not a branch or a tag — the stash, a note ref, or something under a
/// namespace the client does not model.
/// </summary>
public sealed class GitOtherRef : GitRef
{
    /// <summary>
    /// The full name of the stash reference.
    /// </summary>
    public const string StashRefName = "refs/stash";

    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="fullName">The full ref name.</param>
    /// <param name="targetSha">The SHA the ref points at.</param>
    public GitOtherRef(string fullName, string targetSha)
        : base(fullName, targetSha)
    {
    }

    /// <inheritdoc />
    public override GitRefKind Kind
        => FullName switch
        {
            StashRefName => GitRefKind.Stash,
            _ when FullName.StartsWith("refs/notes/", StringComparison.Ordinal) => GitRefKind.Note,
            _ => GitRefKind.Other,
        };

    /// <inheritdoc />
    public override string ShortName
        => FullName.StartsWith("refs/", StringComparison.Ordinal) ? FullName.Substring(5) : FullName;
}
