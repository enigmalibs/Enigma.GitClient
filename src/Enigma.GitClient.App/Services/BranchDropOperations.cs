using System;
using System.Threading.Tasks;
using Enigma.Avalonia.Desktop.Controls.InfoBar;
using Enigma.Avalonia.Desktop.Services;
using Enigma.GitClient.Core.Merging;

namespace Enigma.GitClient.App.Services;

/// <summary>
/// One branch dropped onto another in the branches list.
/// </summary>
/// <param name="Source">The branch being dragged — the one whose work is brought over.</param>
/// <param name="SourceIsRemote">Whether the dragged branch lives on a remote.</param>
/// <param name="Target">The branch it was dropped on — the one that is written to.</param>
/// <param name="TargetIsRemote">Whether the target lives on a remote.</param>
/// <param name="TargetIsCurrent">Whether the target is the branch HEAD is already on.</param>
public sealed record BranchDropRequest(
    string Source,
    bool SourceIsRemote,
    string Target,
    bool TargetIsRemote,
    bool TargetIsCurrent);

/// <summary>
/// What dropping one branch onto another in the branches list does.
/// </summary>
/// <remarks>
/// A composition, not a new git verb: <c>git merge</c> merges into <c>HEAD</c>, so "merge A into B"
/// means being on B first. The flow therefore moves onto the target when it is not already checked
/// out, and then merges — each step through the operations service that already owns its questions
/// and its reporting.
/// </remarks>
public interface IBranchDropOperations
{
    /// <summary>
    /// Carries out a drop the reader has already chosen an action for.
    /// </summary>
    /// <param name="request">Which branch was dropped on which.</param>
    /// <param name="fastForward">How to treat a target that is simply behind.</param>
    /// <returns><see langword="true"/> when the repository changed.</returns>
    /// <remarks>
    /// The mode is the caller's because the question is: the drop opens a menu naming both branches
    /// and the actions they allow, so asking again in a dialog would be asking twice.
    /// </remarks>
    Task<bool> DropAsync(BranchDropRequest request, FastForwardMode fastForward = FastForwardMode.WhenPossible);
}

/// <summary>
/// Default <see cref="IBranchDropOperations"/>.
/// </summary>
public sealed class BranchDropOperations : IBranchDropOperations
{
    private readonly IBranchOperations _branches;
    private readonly IMergeOperations _merges;
    private readonly IInfoBarService _infoBar;

    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="branches">Moves onto the target branch when it is not the current one.</param>
    /// <param name="merges">Performs the merge and reports what it did.</param>
    /// <param name="infoBar">Explains a drop that means nothing.</param>
    public BranchDropOperations(
        IBranchOperations branches,
        IMergeOperations merges,
        IInfoBarService infoBar)
    {
        ArgumentNullException.ThrowIfNull(branches);
        ArgumentNullException.ThrowIfNull(merges);
        ArgumentNullException.ThrowIfNull(infoBar);

        _branches = branches;
        _merges = merges;
        _infoBar = infoBar;
    }

    /// <summary>
    /// Answers whether a drop is one this service would carry out at all, before anything is asked
    /// or run.
    /// </summary>
    /// <param name="request">The drop.</param>
    /// <returns><see langword="true"/> when the pair can be merged.</returns>
    /// <remarks>
    /// Static and side-effect-free on purpose: the drag needs the same answer on every pointer move
    /// to decide whether the pointer is over something it may land on, and that question must not
    /// cost a dialog or a git call.
    /// </remarks>
    public static bool CanDrop(BranchDropRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Source.Length == 0 || request.Target.Length == 0)
        {
            return false;
        }

        // Nothing local writes to a remote-tracking ref: that is what a push is for, and a push is
        // not something to arrive at by dragging.
        if (request.TargetIsRemote)
        {
            return false;
        }

        return !string.Equals(request.Source, request.Target, StringComparison.Ordinal);
    }

    /// <inheritdoc />
    public async Task<bool> DropAsync(
        BranchDropRequest request,
        FastForwardMode fastForward = FastForwardMode.WhenPossible)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!CanDrop(request))
        {
            await ExplainAsync(request).ConfigureAwait(true);
            return false;
        }

        // Being on the target is what makes it the side that is written to.
        if (!request.TargetIsCurrent
            && !await _branches.CheckoutAsync(request.Target, isRemote: false).ConfigureAwait(true))
        {
            return false;
        }

        MergeOutcome outcome = await _merges.MergeAsync(request.Source, fastForward).ConfigureAwait(true);

        // The checkout alone moved HEAD, so a merge that changed nothing still leaves the graph to
        // re-read.
        return outcome.ChangedAnything || !request.TargetIsCurrent;
    }

    private Task ExplainAsync(BranchDropRequest request)
    {
        string message = request.TargetIsRemote
            ? $"\"{request.Target}\" is on a remote. A remote branch is changed by pushing to it, not by merging into it here."
            : "A branch cannot be merged into itself.";

        return _infoBar.ShowAsync(bar =>
        {
            bar.Title = "Nothing to merge";
            bar.Message = message;
            bar.Severity = InfoBarSeverity.Info;
        });
    }
}
