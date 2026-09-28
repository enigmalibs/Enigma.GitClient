using System;
using Enigma.GitClient.App.Formatting;
using Enigma.GitClient.Core.History;

namespace Enigma.GitClient.App.ViewModels.Dialogs;

/// <summary>
/// What the commit details dialog says about a commit: its title and description, who wrote it and
/// when, and its hash.
/// </summary>
/// <remarks>
/// Read-only, and formatted once: the dialog is shown, read, copied from and closed. The date is the
/// author's, as the history's date column shows it, so the dialog and the line it was opened from
/// agree.
/// </remarks>
public sealed class CommitDetailsViewModel : ViewModelBase
{
    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="commit">The commit to describe.</param>
    /// <param name="now">The moment how long ago it was written is measured from.</param>
    public CommitDetailsViewModel(GitCommit commit, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(commit);

        Subject = commit.Subject;
        Body = commit.Body.TrimEnd('\n', '\r');
        Author = commit.Author.ToString();
        Date = RelativeTime.FormatAbsolute(commit.Author.When);
        Age = RelativeTime.Format(commit.Author.When, now);
        Sha = commit.Sha;
    }

    /// <summary>Gets the commit's title: the first line of its message.</summary>
    public string Subject { get; }

    /// <summary>Gets the commit's description: everything after the title, empty when it has none.</summary>
    public string Body { get; }

    /// <summary>Gets a value indicating whether there is a description to show.</summary>
    public bool HasBody => Body.Length > 0;

    /// <summary>Gets who wrote the commit, as <c>Name &lt;email&gt;</c>.</summary>
    public string Author { get; }

    /// <summary>Gets when the commit was written, in full.</summary>
    public string Date { get; }

    /// <summary>Gets how long ago that was, such as <c>3 hours ago</c>.</summary>
    public string Age { get; }

    /// <summary>Gets the date followed by how long ago it was, in parentheses: what the dialog shows.</summary>
    public string DateWithAge => Age.Length > 0 ? $"{Date} ({Age})" : Date;

    /// <summary>Gets the commit's full hash.</summary>
    public string Sha { get; }
}
