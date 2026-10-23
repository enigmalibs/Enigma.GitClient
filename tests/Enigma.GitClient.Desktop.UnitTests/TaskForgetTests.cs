using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Enigma.GitClient.Desktop.Services;
using Enigma.GitClient.Desktop.UnitTests.Infrastructure;
using Xunit;

namespace Enigma.GitClient.Desktop.UnitTests;

/// <summary>
/// <c>Forget()</c> (BUG-9DE3): work started and not awaited hands its failure to the safety net, once,
/// and says nothing when it is cancelled or succeeds.
/// </summary>
/// <remarks>
/// In the headless collection because the hooks are process-wide: no other test may attach a reporter
/// meanwhile. Assertions look for this test's own exceptions, so a late report from an earlier test
/// cannot fail them.
/// </remarks>
[Collection(HeadlessCollection.Name)]
public sealed class TaskForgetTests : IDisposable
{
    private readonly RecordingUnhandledErrorReporter _errors = new();

    public TaskForgetTests() => UnhandledExceptionHooks.Attach(_errors);

    public void Dispose() => UnhandledExceptionHooks.Detach();

    private int ReportsOf(Exception exception)
        => _errors.Reports.Count(report => ReferenceEquals(report.Exception, exception));

    [Fact]
    public void ATaskThatFailsLater_ReportsItsException_Once()
    {
        TaskCompletionSource source = new();
        InvalidOperationException failure = new("later");

        source.Task.Forget();

        Assert.Equal(0, ReportsOf(failure));

        source.SetException(failure);

        Assert.Equal(1, ReportsOf(failure));
        Assert.Equal(UnhandledErrorSource.FireAndForget, _errors.Reports.Single(report => ReferenceEquals(report.Exception, failure)).Source);
    }

    [Fact]
    public void ATaskAlreadyFaulted_IsReportedAtOnce()
    {
        InvalidOperationException failure = new("already");

        Task.FromException(failure).Forget();

        Assert.Equal(1, ReportsOf(failure));
    }

    [Fact]
    public void EveryExceptionOfAFault_IsReported()
    {
        TaskCompletionSource source = new();
        InvalidOperationException first = new("first");
        ArgumentException second = new("second");

        source.Task.Forget();
        source.SetException([first, second]);

        Assert.Equal(1, ReportsOf(first));
        Assert.Equal(1, ReportsOf(second));
    }

    [Fact]
    public async Task AnAsyncMethodThatThrowsAfterAnAwait_IsReported()
    {
        InvalidOperationException failure = new("after an await");
        TaskCompletionSource gate = new(TaskCreationOptions.RunContinuationsAsynchronously);

        async Task WorkAsync()
        {
            await gate.Task.ConfigureAwait(false);
            throw failure;
        }

        Task work = WorkAsync();
        work.Forget();
        gate.SetResult();

        await Task.WhenAny(work, Task.Delay(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));

        for (int wait = 0; wait < 100 && ReportsOf(failure) == 0; wait++)
        {
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }

        Assert.Equal(1, ReportsOf(failure));
    }

    [Fact]
    public void ACancelledTask_SaysNothing()
    {
        using CancellationTokenSource cancelled = new();
        cancelled.Cancel();

        Task.FromCanceled(cancelled.Token).Forget();

        TaskCompletionSource source = new();
        source.Task.Forget();
        source.SetCanceled(TestContext.Current.CancellationToken);

        Assert.DoesNotContain(_errors.Reports, report => report.Exception is OperationCanceledException);
    }

    [Fact]
    public void ATaskThatSucceeds_SaysNothing()
    {
        int before = _errors.Reports.Count;
        TaskCompletionSource source = new();

        Task.CompletedTask.Forget();
        source.Task.Forget();
        source.SetResult();

        Assert.Equal(before, _errors.Reports.Count);
    }
}
