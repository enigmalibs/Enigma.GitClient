using System;
using System.Collections.Generic;
using System.Globalization;
using Enigma.GitClient.Core.History;

namespace Enigma.GitClient.Core.Refs;

/// <summary>
/// Turns the output of the client's <c>git for-each-ref</c> format template into references.
/// </summary>
/// <remarks>
/// One <c>for-each-ref</c> call returns every branch, tag and other ref with all of their metadata,
/// which is both far faster and far less racy than asking git one question per ref. Fields are
/// separated by NUL and records by a newline: neither can occur inside a ref name, a person name, a
/// date or a subject line, so the split is unambiguous.
/// </remarks>
public static class RefParser
{
    /// <summary>
    /// The field separator emitted by <see cref="FormatTemplate"/>.
    /// </summary>
    public const char FieldSeparator = '\u0000';

    /// <summary>
    /// The <c>--format</c> template the reader passes to git. Field order must match
    /// <see cref="ParseRecord"/>.
    /// </summary>
    public const string FormatTemplate =
        "%(refname)%00" +
        "%(objecttype)%00" +
        "%(objectname)%00" +
        "%(*objectname)%00" +
        "%(HEAD)%00" +
        "%(upstream:short)%00" +
        "%(upstream:track)%00" +
        "%(authorname)%00" +
        "%(authoremail)%00" +
        "%(committerdate:iso-strict)%00" +
        "%(*authorname)%00" +
        "%(*authoremail)%00" +
        "%(*committerdate:iso-strict)%00" +
        "%(taggername)%00" +
        "%(taggeremail)%00" +
        "%(taggerdate:iso-strict)%00" +
        "%(contents:subject)";

    private const int FieldCount = 17;

    /// <summary>
    /// Parses a whole <c>for-each-ref</c> payload.
    /// </summary>
    /// <param name="payload">Everything git wrote to standard output.</param>
    /// <returns>The references, in the order git returned them.</returns>
    public static IReadOnlyList<GitRef> Parse(string payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        List<GitRef> refs = [];

        foreach (string line in payload.Split('\n'))
        {
            string record = line.TrimEnd('\r');
            if (record.Length == 0)
            {
                continue;
            }

            refs.Add(ParseRecord(record));
        }

        return refs;
    }

    /// <summary>
    /// Parses a single record.
    /// </summary>
    /// <param name="record">One record, without its terminating newline.</param>
    /// <returns>The reference.</returns>
    /// <exception cref="FormatException">The record did not match the template.</exception>
    public static GitRef ParseRecord(string record)
    {
        ArgumentNullException.ThrowIfNull(record);

        string[] fields = record.Split(FieldSeparator);

        if (fields.Length < FieldCount)
        {
            throw new FormatException(
                $"A for-each-ref record had {fields.Length} fields; the template produces {FieldCount}.");
        }

        string refName = fields[0];
        string objectType = fields[1];
        string objectName = fields[2];
        string dereferenced = fields[3];
        bool isCurrent = fields[4].Trim() == "*";
        string upstream = fields[5];
        string track = fields[6];

        if (refName.StartsWith(GitBranch.LocalPrefix, StringComparison.Ordinal) ||
            refName.StartsWith(GitBranch.RemotePrefix, StringComparison.Ordinal))
        {
            return new GitBranch(
                refName,
                objectName,
                isCurrent,
                upstream.Length == 0 ? null : upstream,
                ParseTracking(track),
                new GitSignature(fields[7], NormaliseEmail(fields[8]), ParseDate(fields[9])),
                ParseDate(fields[9]),
                fields[16]);
        }

        if (refName.StartsWith(GitTag.Prefix, StringComparison.Ordinal))
        {
            bool isAnnotated = string.Equals(objectType, "tag", StringComparison.Ordinal);

            return new GitTag(
                refName,
                isAnnotated ? dereferenced : objectName,
                isAnnotated ? objectName : null,
                isAnnotated
                    ? new GitSignature(fields[13], NormaliseEmail(fields[14]), ParseDate(fields[15]))
                    : null,
                isAnnotated ? fields[16] : string.Empty,
                ParseDate(isAnnotated ? fields[12] : fields[9]));
        }

        return new GitOtherRef(refName, objectName);
    }

    /// <summary>
    /// Parses git's <c>upstream:track</c> atom, for example <c>[ahead 2, behind 1]</c>.
    /// </summary>
    /// <param name="track">The atom's value; empty when the branch has no upstream.</param>
    /// <returns>The parsed tracking state.</returns>
    public static BranchTracking ParseTracking(string? track)
    {
        if (string.IsNullOrWhiteSpace(track))
        {
            return BranchTracking.None;
        }

        string inner = track.Trim().Trim('[', ']');

        if (string.Equals(inner, "gone", StringComparison.OrdinalIgnoreCase))
        {
            return new BranchTracking(0, 0, IsUpstreamGone: true);
        }

        int ahead = 0;
        int behind = 0;

        foreach (string part in inner.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] words = part.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.Length != 2 ||
                !int.TryParse(words[1], NumberStyles.None, CultureInfo.InvariantCulture, out int count))
            {
                continue;
            }

            if (string.Equals(words[0], "ahead", StringComparison.OrdinalIgnoreCase))
            {
                ahead = count;
            }
            else if (string.Equals(words[0], "behind", StringComparison.OrdinalIgnoreCase))
            {
                behind = count;
            }
        }

        return new BranchTracking(ahead, behind, IsUpstreamGone: false);
    }

    /// <summary>
    /// Strips the angle brackets git wraps around an email address in its <c>*email</c> atoms.
    /// </summary>
    /// <param name="email">The raw atom value.</param>
    /// <returns>The bare address.</returns>
    private static string NormaliseEmail(string email)
        => email.Length >= 2 && email[0] == '<' && email[^1] == '>'
            ? email.Substring(1, email.Length - 2)
            : email;

    private static DateTimeOffset ParseDate(string field)
        => DateTimeOffset.TryParse(
            field,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind,
            out DateTimeOffset parsed)
            ? parsed
            : DateTimeOffset.MinValue;
}
