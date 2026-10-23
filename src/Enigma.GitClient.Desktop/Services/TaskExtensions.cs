using System;
using System.Threading;
using System.Threading.Tasks;

namespace Enigma.GitClient.Desktop.Services;

/// <summary>
/// Work started and not awaited.
/// </summary>
public static class TaskExtensions
{
    extension(Task task)
    {
        /// <summary>
        /// Lets the work run on without anyone awaiting it, and hands a failure to the safety net
        /// (<see cref="UnhandledExceptionHooks"/>) instead of letting it vanish in a task nobody looks at.
        /// </summary>
        /// <remarks>
        /// The one way the application starts work it does not await (BUG-9DE3). A cancelled task is no
        /// failure and says nothing; a failure is reported once, from whatever thread the task ends on —
        /// the safety net logs it there and shows it on the UI thread. Work that handles its own failures
        /// goes through here too: what it does not handle is exactly what the net is for.
        /// </remarks>
        public void Forget()
        {
            ArgumentNullException.ThrowIfNull(task);

            if (task.IsCompleted)
            {
                Observe(task);
                return;
            }

            // The continuation's own task is discarded on purpose: Observe cannot fail, so it never
            // faults.
            _ = task.ContinueWith(Observe, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
    }

    private static void Observe(Task task)
    {
        if (task is not { IsFaulted: true, Exception: { } failure })
        {
            return;
        }

        foreach (Exception exception in failure.InnerExceptions)
        {
            UnhandledExceptionHooks.Report(exception, UnhandledErrorSource.FireAndForget);
        }
    }
}
