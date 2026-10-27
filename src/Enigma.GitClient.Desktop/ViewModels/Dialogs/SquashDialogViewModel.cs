using System;
using System.Collections.Generic;
using System.Globalization;
using Enigma.GitClient.Core.Refs;
using Enigma.GitClient.Core.Squash;
using Enigma.GitClient.Desktop.Services;

namespace Enigma.GitClient.Desktop.ViewModels.Dialogs;

/// <summary>
/// The fields of the squash dialog: what is squashed, the new branch, whether to switch to it, the
/// message, and what the reader should know before confirming.
/// </summary>
/// <remarks>
/// The name and the title are judged on every keystroke, as the "create a branch" dialog judges a
/// name, so the reason the dialog cannot be confirmed appears while it is being typed. The squash
/// service judges both again before it writes anything.
/// </remarks>
public sealed class SquashDialogViewModel : ViewModelBase
{
    private readonly HashSet<string> _existingNames;

    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="candidate">The base and the tip, as the history showed them.</param>
    /// <param name="preview">What the squash would replace, read from git.</param>
    /// <param name="existingNames">The local branch names already taken.</param>
    public SquashDialogViewModel(
        SquashCandidate candidate,
        SquashPreview preview,
        IReadOnlyCollection<string> existingNames)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(preview);
        ArgumentNullException.ThrowIfNull(existingNames);

        _existingNames = new HashSet<string>(existingNames, StringComparer.Ordinal);

        Candidate = candidate;
        Preview = preview;
        BranchName = DefaultBranchName(candidate.TipBranches, candidate.Tip.ShortSha, _existingNames);
        SwitchToNewBranch = candidate.HeadIsAtTip;
        Message = SquashService.ComposeDefaultMessage(preview.Subjects);
        Warnings = BuildWarnings(candidate, preview);
    }

    /// <summary>Gets the base and the tip, as the history showed them.</summary>
    public SquashCandidate Candidate { get; }

    /// <summary>Gets what the squash would replace.</summary>
    public SquashPreview Preview { get; }

    /// <summary>Gets the base's line: its short sha and subject.</summary>
    public string BaseText => $"{Candidate.Base.ShortSha}  {Candidate.Base.Subject}";

    /// <summary>Gets the tip's line: its short sha and subject.</summary>
    public string TipText => $"{Candidate.Tip.ShortSha}  {Candidate.Tip.Subject}";

    /// <summary>Gets the local branches that end at the tip, or an empty string when none does.</summary>
    public string TipBranchesText => Candidate.TipBranches.Count == 0
        ? string.Empty
        : $"On {string.Join(", ", Candidate.TipBranches)}";

    /// <summary>Gets a value indicating whether a local branch ends at the tip.</summary>
    public bool HasTipBranches => Candidate.TipBranches.Count > 0;

    /// <summary>
    /// Gets how much the squash replaces: the commits, the merges among them, and what came in from
    /// outside the range.
    /// </summary>
    public string RangeText
    {
        get
        {
            string text = CountCommits(Preview.CommitCount);

            if (Preview.MergeCount > 0)
            {
                text += Preview.MergeCount == 1
                    ? " (1 merge)"
                    : $" ({Preview.MergeCount.ToString(CultureInfo.CurrentCulture)} merges)";
            }

            if (Preview.OutsideCommitCount > 0)
            {
                text += $", plus {CountCommits(Preview.OutsideCommitCount)} merged in from outside the range";
            }

            return text;
        }
    }

    /// <summary>Gets what the reader should know before confirming; empty when there is nothing.</summary>
    public IReadOnlyList<string> Warnings { get; }

    /// <summary>Gets a value indicating whether there is anything to warn about.</summary>
    public bool HasWarnings => Warnings.Count > 0;

    /// <summary>
    /// Gets the sentence that says what the squash leaves alone: everything that exists.
    /// </summary>
    public string UntouchedText => Candidate.TipBranches.Count == 0
        ? "Nothing that exists changes: the original commits stay where they are."
        : $"Nothing that exists changes: \"{Candidate.TipBranches[0]}\" keeps its {CountCommits(Preview.CommitCount)}.";

    /// <summary>
    /// Gets or sets the name of the branch the squash commit is created on.
    /// </summary>
    public string BranchName
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                RaiseValidation();
            }
        }
    } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether HEAD moves to the new branch once it exists.
    /// </summary>
    public bool SwitchToNewBranch { get; set => SetProperty(ref field, value); }

    /// <summary>
    /// Gets or sets the squash commit's message; its first line is the title.
    /// </summary>
    public string Message
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                RaiseValidation();
            }
        }
    } = string.Empty;

    /// <summary>
    /// Gets the reason the dialog cannot be confirmed, empty when it can.
    /// </summary>
    public string ValidationMessage
    {
        get
        {
            RefNameValidation validation = RefNameValidator.ValidateBranch(BranchName);

            if (!validation.IsValid)
            {
                return validation.Message;
            }

            if (_existingNames.Contains(BranchName))
            {
                return $"A branch called \"{BranchName}\" already exists.";
            }

            return SquashService.TitleOf(Message ?? string.Empty).Length == 0
                ? "Write a title on the first line of the message."
                : string.Empty;
        }
    }

    /// <summary>Gets a value indicating whether there is something to show in the validation line.</summary>
    public bool HasValidationMessage => ValidationMessage.Length > 0;

    /// <summary>Gets a value indicating whether the dialog can be confirmed.</summary>
    public bool IsValid => ValidationMessage.Length == 0;

    /// <summary>
    /// Raised whenever the answer to <see cref="IsValid"/> may have changed.
    /// </summary>
    public event EventHandler? ValidationChanged;

    /// <summary>
    /// Works out the name the dialog proposes: the first local branch ending at the tip with
    /// <c>-squashed</c> after it, or <c>squash/</c> and the tip's short sha when none does — with
    /// <c>-2</c>, <c>-3</c>… after it while the name is taken.
    /// </summary>
    /// <param name="tipBranches">The local branches ending at the tip, the checked-out one first.</param>
    /// <param name="tipShortSha">The tip's short sha.</param>
    /// <param name="existingNames">The local branch names already taken.</param>
    /// <returns>A name no branch has.</returns>
    public static string DefaultBranchName(
        IReadOnlyList<string> tipBranches,
        string tipShortSha,
        IReadOnlyCollection<string> existingNames)
    {
        ArgumentNullException.ThrowIfNull(tipBranches);
        ArgumentNullException.ThrowIfNull(tipShortSha);
        ArgumentNullException.ThrowIfNull(existingNames);

        string name = tipBranches.Count > 0 ? $"{tipBranches[0]}-squashed" : $"squash/{tipShortSha}";

        if (!Contains(existingNames, name))
        {
            return name;
        }

        for (int suffix = 2; ; suffix++)
        {
            string candidate = $"{name}-{suffix.ToString(CultureInfo.InvariantCulture)}";

            if (!Contains(existingNames, candidate))
            {
                return candidate;
            }
        }
    }

    private static bool Contains(IReadOnlyCollection<string> names, string name)
    {
        foreach (string existing in names)
        {
            if (string.Equals(existing, name, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static List<string> BuildWarnings(SquashCandidate candidate, SquashPreview preview)
    {
        List<string> warnings = [];

        foreach (SquashOutsideMerge merge in preview.OutsideMerges)
        {
            string shortSha = merge.Sha[..Math.Min(7, merge.Sha.Length)];

            warnings.Add(
                $"{shortSha} \"{merge.Subject}\" brings in a branch from outside the range. The new branch keeps "
                + "its files but loses the link to it, so merging that branch again later may conflict again.");
        }

        if (preview.ChangesNothing)
        {
            warnings.Add("These commits change no file in total: the squash commit will be empty.");
        }

        if (candidate.TipBranches.Count == 0)
        {
            warnings.Add(
                $"No local branch ends at {candidate.Tip.ShortSha}: the new branch ends at the squash commit, "
                + $"and nothing above {candidate.Tip.ShortSha} is on it.");
        }

        return warnings;
    }

    private static string CountCommits(int commits)
        => commits == 1 ? "1 commit" : $"{commits.ToString(CultureInfo.CurrentCulture)} commits";

    private void RaiseValidation()
    {
        OnPropertyChanged(nameof(ValidationMessage));
        OnPropertyChanged(nameof(HasValidationMessage));
        OnPropertyChanged(nameof(IsValid));
        ValidationChanged?.Invoke(this, EventArgs.Empty);
    }
}
