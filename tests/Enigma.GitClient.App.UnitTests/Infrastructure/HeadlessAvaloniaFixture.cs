using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using Xunit;

namespace Enigma.GitClient.App.UnitTests.Infrastructure;

/// <summary>
/// Stands up a headless Avalonia platform once per test run and gives tests a way to run work on
/// its UI thread.
/// </summary>
/// <remarks>
/// Avalonia can only be initialised once per process, and every control must be built and measured
/// on the thread that initialised it. The fixture therefore owns a dedicated thread: it sets the
/// platform up there, then pumps a queue of test work and the Avalonia dispatcher's own jobs.
/// </remarks>
public sealed class HeadlessAvaloniaFixture : IDisposable
{
    private readonly BlockingCollection<Action> _work = [];
    private readonly Thread _uiThread;
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    /// Initialises the fixture and waits for the headless platform to come up.
    /// </summary>
    public HeadlessAvaloniaFixture()
    {
        _uiThread = new Thread(RunUiThread)
        {
            IsBackground = true,
            Name = "avalonia-headless-tests",
        };

        _uiThread.Start();
        _ready.Task.GetAwaiter().GetResult();
    }

    /// <summary>
    /// Runs an action on the Avalonia UI thread and waits for it.
    /// </summary>
    /// <param name="action">The work to run.</param>
    public void Run(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

        _work.Add(() =>
        {
            try
            {
                action();
                completion.SetResult();
            }
            catch (Exception exception)
            {
                completion.SetException(exception);
            }
        });

        completion.Task.GetAwaiter().GetResult();
    }

    /// <summary>
    /// Runs a function on the Avalonia UI thread and returns its result.
    /// </summary>
    /// <typeparam name="TResult">The result type.</typeparam>
    /// <param name="function">The work to run.</param>
    /// <returns>The function's result.</returns>
    public TResult Run<TResult>(Func<TResult> function)
    {
        ArgumentNullException.ThrowIfNull(function);

        TResult result = default!;
        Run(() => result = function());
        return result;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _work.CompleteAdding();

        if (!_uiThread.Join(TimeSpan.FromSeconds(5)))
        {
            // A stuck UI thread must not hang the whole test run; it is a background thread and
            // the process will reclaim it.
        }

        _work.Dispose();
    }

    private void RunUiThread()
    {
        try
        {
            // Skia rather than the headless no-op renderer: it costs nothing here and it is what
            // makes CaptureRenderedFrame produce real pixels, so a snapshot test can assert the
            // window actually drew something.
            AppBuilder.Configure<App>()
                .UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();

            _ready.SetResult();
        }
        catch (Exception exception)
        {
            _ready.SetException(exception);
            return;
        }

        foreach (Action action in _work.GetConsumingEnumerable())
        {
            action();

            // Let anything the work posted to the dispatcher (bindings, layout passes) drain before
            // the next test observes the tree.
            Dispatcher.UIThread.RunJobs();
        }
    }
}

/// <summary>
/// The collection every headless UI test belongs to, so they share the one Avalonia platform and
/// never run concurrently on it.
/// </summary>
[CollectionDefinition(Name)]
public sealed class HeadlessCollection : ICollectionFixture<HeadlessAvaloniaFixture>
{
    /// <summary>
    /// The collection's name.
    /// </summary>
    public const string Name = "headless-avalonia";
}
