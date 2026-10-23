using System;

namespace Enigma.GitClient.Core.History;

/// <summary>
/// Who did something, and when — the author or the committer of a commit.
/// </summary>
/// <param name="Name">The person's name as recorded in the commit.</param>
/// <param name="Email">The person's email address as recorded in the commit.</param>
/// <param name="When">The recorded timestamp, with the original UTC offset preserved.</param>
public sealed record GitSignature(string Name, string Email, DateTimeOffset When)
{
    /// <summary>
    /// An empty signature, used where git reported no identity at all.
    /// </summary>
    public static readonly GitSignature Empty = new(string.Empty, string.Empty, DateTimeOffset.MinValue);

    /// <summary>
    /// Gets the initials used for the generated avatar in the history view. Falls back to the
    /// email's local part, then to a question mark, so there is always something to draw.
    /// </summary>
    public string Initials => InitialsOf(Name.Length > 0 ? Name : Email.Split('@')[0]);

    /// <summary>
    /// Writes the initials a generated avatar shows for a text: the first letters of its first and last
    /// words, in upper case.
    /// </summary>
    /// <param name="text">A name, or anything else a monogram stands for — a profile's label.</param>
    /// <returns>One or two letters, or a question mark when the text has no word to take one from.</returns>
    /// <remarks>
    /// Words are separated by spaces, dots, hyphens and underscores, so an email's local part
    /// (<c>ada.lovelace</c>) gives the same letters as the name it spells.
    /// </remarks>
    public static string InitialsOf(string? text)
    {
        string[] words = (text ?? string.Empty).Split([' ', '.', '-', '_'], StringSplitOptions.RemoveEmptyEntries);

        return words.Length switch
        {
            0 => "?",
            1 => words[0].Substring(0, 1).ToUpperInvariant(),
            _ => string.Concat(
                words[0].Substring(0, 1).ToUpperInvariant(),
                words[^1].Substring(0, 1).ToUpperInvariant()),
        };
    }

    /// <inheritdoc />
    public override string ToString() => Email.Length > 0 ? $"{Name} <{Email}>" : Name;
}
