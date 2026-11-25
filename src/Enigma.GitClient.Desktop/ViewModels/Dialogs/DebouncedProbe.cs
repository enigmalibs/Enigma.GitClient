using System;
using System.Threading;
using System.Threading.Tasks;
using Enigma.GitClient.Desktop.Services;

namespace Enigma.GitClient.Desktop.ViewModels.Dialogs;

/// <summary>
/// A check of the disk that a dialog makes as the user types: run off the UI thread, a short while
/// after the last change, and replaced by the next change.
/// </summary>
/// <typeparam name="TResult">What the check answers.</typeparam>
/// <remarks>
/// <para>
/// A keystroke costs no disk access on the UI thread. The probe runs on the thread pool, and its answer
/// comes back on the context that started it, the UI thread, where it is handed over unless a newer
/// change has replaced it.
/// </para>
/// <para>
/// The probe itself cannot be interrupted: a probe that a change replaces runs to its end, and its
/// answer is dropped. It handles its own expected failures and returns them as an answer.
/// </para>
/// </remarks>
internal sealed class DebouncedProbe<TResult>
{
    /// <summary>
    /// How long after a change the probe runs, unless another change comes first.
    /// </summary>
    public static readonly TimeSpan Delay = TimeSpan.FromMilliseconds(250);

    private readonly Func<string, TResult> _probe;
    private readonly TimeProvider _time;

    private CancellationTokenSource? _pending;
    private TaskCompletionSource? _skipDelay;
    private Task _current = Task.CompletedTask;

    /// <summary>
    /// Initialises a new instance.
    /// </summary>
    /// <param name="probe">The check, given the path to look at. Runs on the thread pool.</param>
    /// <param name="time">Measures the delay.</param>
    public DebouncedProbe(Func<string, TResult> probe, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(probe);
        ArgumentNullException.ThrowIfNull(time);

        _probe = probe;
        _time = time;
    }

    /// <summary>
    /// Gets a value indicating whether a probe is waiting or running, its answer not handed over yet.
    /// </summary>
    public bool IsPending => _pending is not null;

    /// <summary>
    /// Starts a probe, replacing the one pending.
    /// </summary>
    /// <param name="path">The path to look at.</param>
    /// <param name="answered">Receives the answer, on the caller's context, unless a newer probe replaced this one.</param>
    public void Start(string path, Action<TResult> answered)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(answered);

        Cancel();

        CancellationTokenSource pending = new();
        TaskCompletionSource skipDelay = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending = pending;
        _skipDelay = skipDelay;

        Task current = RunAsync(path, answered, pending, skipDelay.Task);
        _current = current;
        current.Forget();
    }

    /// <summary>
    /// Drops the pending probe, if there is one: its answer is never handed over.
    /// </summary>
    public void Cancel()
    {
        _pending?.Cancel();
        _pending = null;
        _skipDelay = null;
    }

    /// <summary>
    /// Runs the pending probe now, without waiting for the rest of its delay, and waits until its answer
    /// has been handed over. Returns at once when nothing is pending.
    /// </summary>
    /// <returns>A task that completes once no probe is pending.</returns>
    public async Task FlushAsync()
    {
        // A loop, because the answer may itself start another probe.
        while (_pending is not null)
        {
            _skipDelay?.TrySetResult();
            await _current.ConfigureAwait(true);
        }
    }

    private async Task RunAsync(string path, Action<TResult> answered, CancellationTokenSource pending, Task skipDelay)
    {
        CancellationToken cancellation = pending.Token;

        try
        {
            // A delay cancelled by the next change completes too: the check below sees why.
            await Task.WhenAny(Task.Delay(Delay, _time, cancellation), skipDelay).ConfigureAwait(true);

            if (cancellation.IsCancellationRequested)
            {
                return;
            }

            TResult answer = await Task.Run(() => _probe(path), CancellationToken.None).ConfigureAwait(true);

            if (cancellation.IsCancellationRequested)
            {
                return;
            }

            _pending = null;
            _skipDelay = null;
            answered(answer);
        }
        finally
        {
            pending.Dispose();
        }
    }
}
