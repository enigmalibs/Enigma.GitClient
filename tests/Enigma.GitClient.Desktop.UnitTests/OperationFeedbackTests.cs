using System;
using System.Linq;
using Enigma.Avalonia.Desktop.Controls.InfoBar;
using Enigma.Avalonia.Desktop.Services;
using Enigma.GitClient.Desktop.Services;
using Enigma.GitClient.Desktop.UnitTests.Infrastructure;
using Xunit;

namespace Enigma.GitClient.Desktop.UnitTests;

/// <summary>
/// What every operation tells and asks the reader through (CODE-REVIEW-6E56 SOLID-02): the info bar's
/// reports, the dialogs, the progress overlay, and git's first line, once.
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class OperationFeedbackTests
{
    private static readonly Type[] Operations =
    [
        typeof(SyncOperations), typeof(WorktreeOperations), typeof(SubmoduleOperations), typeof(CheckoutOperations),
        typeof(TagOperations), typeof(SquashOperations), typeof(ResetOperations), typeof(DiscardOperations),
        typeof(BranchOperations), typeof(StashOperations), typeof(RevertOperations), typeof(MergeOperations),
    ];

    private readonly HeadlessAvaloniaFixture _fixture;

    public OperationFeedbackTests(HeadlessAvaloniaFixture fixture) => _fixture = fixture;

    [Theory]
    [InlineData("fatal: not a branch\nhint: try again\n", "fatal: not a branch")]
    [InlineData("  error: refused\r\nmore\r\n", "error: refused")]
    [InlineData("only line", "only line")]
    [InlineData("   ", "git reported no reason.")]
    [InlineData("", "git reported no reason.")]
    public void FirstLine_IsTheFirstLineGitWrote_OrSaysThereWasNone(string written, string expected)
        => Assert.Equal(expected, OperationFeedback.FirstLine(written));

    [Fact]
    public void AReport_GoesToTheInfoBar_AsOneNoteOrThreeParts()
    {
        _fixture.Run(() =>
        {
            RecordingInfoBarService infoBar = new();
            OperationFeedback feedback = new(new ScriptedContentDialogService(), new RecordingOverlayService(), infoBar);

            feedback.Report("Tag deleted", "v1 is gone.", InfoBarSeverity.Success);
            feedback.Report(("Could not delete", "git refused.", InfoBarSeverity.Error));

            Assert.Equal(["Tag deleted", "Could not delete"], infoBar.Shown.Select(note => note.Title));
            Assert.Equal(InfoBarSeverity.Error, infoBar.Shown[1].Severity);
        });
    }

    [Fact]
    public void EveryOperation_TakesTheFeedback_InsteadOfTheDialogsTheInfoBarAndTheOverlay()
    {
        foreach (Type operation in Operations)
        {
            Type[] taken = [.. operation.GetConstructors().Single().GetParameters().Select(parameter => parameter.ParameterType)];

            Assert.Contains(typeof(IOperationFeedback), taken);
            Assert.DoesNotContain(typeof(IContentDialogService), taken);
            Assert.DoesNotContain(typeof(IInfoBarService), taken);
            Assert.DoesNotContain(typeof(IOverlayService), taken);
        }
    }
}
