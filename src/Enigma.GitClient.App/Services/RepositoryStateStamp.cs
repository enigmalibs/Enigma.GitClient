using System;
using System.Collections.Generic;
using System.Linq;
using Enigma.GitClient.Core.Refs;

namespace Enigma.GitClient.App.Services;

/// <summary>
/// What the history is drawn from, reduced to a value two moments can be compared by: where HEAD is,
/// what is in progress, and where every reference points.
/// </summary>
/// <remarks>
/// A refresh publishes new objects every time, whether or not anything moved, so comparing the
/// objects says nothing. Comparing stamps says whether re-reading the history would draw anything
/// different — which is the only question a page that wants to keep the reader's place has to ask.
/// </remarks>
/// <param name="Head">Where HEAD was, and what was in progress.</param>
/// <param name="References">Every reference as <c>full name → target</c>, in ordinal order.</param>
public sealed record RepositoryStateStamp(HeadState? Head, string References)
{
    /// <summary>
    /// Takes the stamp of what a repository context last read.
    /// </summary>
    /// <param name="context">The context.</param>
    /// <returns>The stamp.</returns>
    public static RepositoryStateStamp Of(IRepositoryContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        IEnumerable<string> references = context.Refs.All()
            .Select(reference => reference.FullName + "=" + reference.TargetSha)
            .Order(StringComparer.Ordinal);

        return new RepositoryStateStamp(context.Head, string.Join('\n', references));
    }
}
