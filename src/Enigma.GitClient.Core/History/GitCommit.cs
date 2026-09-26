using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Enigma.GitClient.Core.History;

/// <summary>
/// One commit as read from the object database.
/// </summary>
public sealed class GitCommit : IEquatable<GitCommit>
{
    /// <summary>
    /// The number of characters used for the abbreviated SHA shown in the history view.
    /// </summary>
    public const int ShortShaLength = 7;

    private readonly ReadOnlyCollection<string> _parentShas;

    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="sha">The full 40-character SHA.</param>
    /// <param name="parentShas">The parents' full SHAs, in git's order.</param>
    /// <param name="author">Who wrote the change.</param>
    /// <param name="committer">Who committed it.</param>
    /// <param name="subject">The first line of the commit message.</param>
    /// <param name="body">Everything after the subject, without the separating blank line.</param>
    public GitCommit(
        string sha,
        IReadOnlyList<string> parentShas,
        GitSignature author,
        GitSignature committer,
        string subject,
        string body)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sha);
        ArgumentNullException.ThrowIfNull(parentShas);
        ArgumentNullException.ThrowIfNull(author);
        ArgumentNullException.ThrowIfNull(committer);
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(body);

        Sha = sha;
        _parentShas = new ReadOnlyCollection<string>([.. parentShas]);
        Author = author;
        Committer = committer;
        Subject = subject;
        Body = body;
    }

    /// <summary>
    /// Gets the full 40-character SHA.
    /// </summary>
    public string Sha { get; }

    /// <summary>
    /// Gets the abbreviated SHA shown in the history view.
    /// </summary>
    public string ShortSha => Sha.Length <= ShortShaLength ? Sha : Sha.Substring(0, ShortShaLength);

    /// <summary>
    /// Gets the parents' full SHAs, in git's order. The first parent is the branch the commit was
    /// made on; the rest are the branches it merged in.
    /// </summary>
    public IReadOnlyList<string> ParentShas => _parentShas;

    /// <summary>
    /// Gets who wrote the change.
    /// </summary>
    public GitSignature Author { get; }

    /// <summary>
    /// Gets who committed it. This differs from the author after a cherry-pick or an amend.
    /// </summary>
    public GitSignature Committer { get; }

    /// <summary>
    /// Gets the first line of the commit message.
    /// </summary>
    public string Subject { get; }

    /// <summary>
    /// Gets everything after the subject, without the blank line that separates them.
    /// </summary>
    public string Body { get; }

    /// <summary>
    /// Gets the commit message as subject, a blank line, then the body.
    /// </summary>
    public string Message => Body.Length == 0 ? Subject : $"{Subject}\n\n{Body}";

    /// <summary>
    /// Gets a value indicating whether the commit merges two or more histories.
    /// </summary>
    public bool IsMerge => _parentShas.Count > 1;

    /// <summary>
    /// Gets a value indicating whether the commit has no parents, and so starts a history.
    /// </summary>
    public bool IsRoot => _parentShas.Count == 0;

    /// <inheritdoc />
    public bool Equals(GitCommit? other)
        => other is not null && string.Equals(Sha, other.Sha, StringComparison.Ordinal);

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as GitCommit);

    /// <inheritdoc />
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Sha);

    /// <inheritdoc />
    public override string ToString() => $"{ShortSha} {Subject}";
}
