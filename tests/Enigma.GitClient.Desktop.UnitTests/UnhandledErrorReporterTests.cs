using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using Enigma.Avalonia.Desktop.Controls.ContentDialog;
using Enigma.Avalonia.Desktop.Services;
using Enigma.GitClient.Desktop.Services;
using Enigma.GitClient.Desktop.UnitTests.Infrastructure;
using Enigma.GitClient.Desktop.ViewModels.Dialogs;
using Enigma.GitClient.Desktop.Views;
using Enigma.GitClient.Desktop.Views.Dialogs;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Enigma.GitClient.Desktop.UnitTests;

/// <summary>
/// The safety net's reporter (BUG-9DE3): it logs every exception, shows one dialog on the current
/// window's host when that host is free, waits for a window under the splash, and never loops on a
/// dialog that cannot be shown.
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class UnhandledErrorReporterTests
{
    private readonly HeadlessAvaloniaFixture _fixture;

    public UnhandledErrorReporterTests(HeadlessAvaloniaFixture fixture) => _fixture = fixture;

    private sealed class Harness
    {
        public RecordingAppWindows Windows { get; } = new();

        public ScriptedContentDialogService Dialogs { get; } = new();

        public RecordingSystemInterop Interop { get; } = new();

        public ManualTimeProvider Clock { get; } = new();

        public RecordingLogger<UnhandledErrorReporter> Logger { get; } = new();

        public UnhandledErrorReporter Build(IContentDialogService? dialogs = null)
            => new(Windows, dialogs ?? Dialogs, Interop, Clock, Logger);
    }

    private static Exception Thrown(string message)
    {
        try
        {
            throw new InvalidOperationException(message);
        }
        catch (InvalidOperationException caught)
        {
            return caught;
        }
    }

    private static void Settle()
    {
        for (int pass = 0; pass < 5; pass++)
        {
            Dispatcher.UIThread.RunJobs();
        }
    }

    [Fact]
    public void AnError_IsLoggedAtError_AndShownOnceOnTheWindow()
    {
        _fixture.Run(() =>
        {
            Harness harness = new();
            UnhandledErrorReporter reporter = harness.Build();
            StartWindow window = new();
            harness.Windows.SetCurrentWindow(window);

            reporter.Report(Thrown("the command failed"), UnhandledErrorSource.Dispatcher);
            Settle();

            Assert.Contains(harness.Logger.Entries, entry => entry.Level == LogLevel.Error && entry.Exception?.Message == "the command failed");

            ContentDialog dialog = Assert.Single(harness.Dialogs.Shown);
            Assert.Equal("Something went wrong", dialog.Title);
            Assert.Equal("Close", dialog.CloseButtonText);
            UnhandledErrorViewModel model = Assert.IsType<UnhandledErrorViewModel>(Assert.IsType<UnhandledErrorView>(dialog.Content).DataContext);
            Assert.Equal("the command failed", model.Message);
            Assert.Contains("still running", model.Lead, StringComparison.Ordinal);
            Assert.False(model.HasMore);
        });
    }

    [Fact]
    public void BeforeTheFirstWindow_TheErrorWaits_ThenShowsOnIt()
    {
        _fixture.Run(() =>
        {
            Harness harness = new();
            UnhandledErrorReporter reporter = harness.Build();

            reporter.Report(Thrown("under the splash"), UnhandledErrorSource.Startup);
            reporter.Report(Thrown("and another"), UnhandledErrorSource.FireAndForget);
            Settle();

            Assert.Empty(harness.Dialogs.Shown);

            harness.Windows.SetCurrentWindow(new StartWindow());
            Settle();

            ContentDialog dialog = Assert.Single(harness.Dialogs.Shown);
            UnhandledErrorViewModel model = (UnhandledErrorViewModel)((UnhandledErrorView)dialog.Content!).DataContext!;
            Assert.Equal("under the splash", model.Message);
            Assert.Equal("1 more error occurred.", model.MoreText);
        });
    }

    [Fact]
    public void WhileAnotherQuestionIsOpen_TheErrorWaitsForItsAnswer()
    {
        _fixture.Run(() =>
        {
            Harness harness = new();
            UnhandledErrorReporter reporter = harness.Build();
            StartWindow window = new();
            harness.Windows.SetCurrentWindow(window);
            ContentDialog host = ((IHostWindow)window).DialogHost;

            Task<DialogResult> question = host.ShowAsync();

            reporter.Report(Thrown("during a question"), UnhandledErrorSource.Dispatcher);
            Settle();

            Assert.Empty(harness.Dialogs.Shown);

            host.HideAsync().GetAwaiter().GetResult();
            Settle();

            Assert.True(question.IsCompleted);
            Assert.Single(harness.Dialogs.Shown);
        });
    }

    [Fact]
    public void ACancellationWhileClosing_IsNeitherShownNorLoggedAsAnError()
    {
        _fixture.Run(() =>
        {
            Harness harness = new();
            UnhandledErrorReporter reporter = harness.Build();
            harness.Windows.SetCurrentWindow(new StartWindow());

            reporter.ShutdownStarted();
            reporter.Report(new OperationCanceledException(), UnhandledErrorSource.FireAndForget);
            Settle();

            Assert.Empty(harness.Dialogs.Shown);
            Assert.DoesNotContain(harness.Logger.Entries, entry => entry.Level >= LogLevel.Warning);
        });
    }

    [Fact]
    public void ADialogThatCannotBeShown_IsLogged_AndNeverTriedAgain()
    {
        _fixture.Run(() =>
        {
            Harness harness = new();
            ThrowingDialogService throwing = new();
            UnhandledErrorReporter reporter = harness.Build(throwing);
            harness.Windows.SetCurrentWindow(new StartWindow());

            reporter.Report(Thrown("first"), UnhandledErrorSource.Dispatcher);
            Settle();
            reporter.Report(Thrown("second"), UnhandledErrorSource.Dispatcher);
            Settle();

            Assert.Equal(1, throwing.Attempts);
            Assert.Contains(harness.Logger.Entries, entry => entry.Message.Contains("could not be shown", StringComparison.Ordinal));
            Assert.Equal(2, harness.Logger.Entries.Count(entry => entry.Message.Contains("reached the safety net", StringComparison.Ordinal)));
        });
    }

    [Fact]
    public void CopyDetails_PutsTheDetailsOnTheClipboard()
    {
        _fixture.RunAsync(async () =>
        {
            RecordingSystemInterop interop = new();
            UnhandledErrorViewModel model = new(new UnhandledErrorBatch(Thrown("copied")), interop);

            await model.CopyDetailsCommand.ExecuteAsync(null);

            string copied = Assert.Single(interop.Copied);
            Assert.StartsWith("System.InvalidOperationException: copied", copied, StringComparison.Ordinal);
        });
    }

    private sealed class ThrowingDialogService : IContentDialogService
    {
        public int Attempts { get; private set; }

        public void RegisterHost(ContentDialog dialog)
        {
        }

        public Task<DialogResult> ShowMessageAsync(string title, string message, string closeButtonText = "OK")
            => throw new InvalidOperationException("no host");

        public Task<DialogResult> ShowAsync(Action<ContentDialog> configure)
        {
            Attempts++;
            throw new InvalidOperationException("ContentDialog host has not been registered.");
        }

        public Task HideAsync() => Task.CompletedTask;
    }
}
