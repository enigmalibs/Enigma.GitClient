using System;
using Enigma.GitClient.Core.Repositories;

namespace Enigma.GitClient.Desktop.Services;

/// <summary>
/// Watches a repository's files and says what changed — the operating system's side of
/// <see cref="IRepositoryWatcher"/>, kept behind this interface so everything the watcher decides is
/// tested without touching a disk.
/// </summary>
public interface IRepositoryEventSource
{
    /// <summary>
    /// Starts watching a repository.
    /// </summary>
    /// <param name="repository">The repository.</param>
    /// <param name="sink">
    /// Told about every change and every fault, on whatever thread the operating system reports it.
    /// </param>
    /// <returns>The running watch; disposing it stops it, and the sink hears nothing more.</returns>
    /// <exception cref="System.IO.IOException">
    /// The operating system refused: out of inotify instances or watches, a directory gone.
    /// </exception>
    /// <remarks>
    /// May take a while on a large tree — on Linux, a watch is added to every directory before this
    /// returns — so it is never called on the UI thread.
    /// </remarks>
    IDisposable Watch(RepositoryHandle repository, IRepositoryEventSink sink);
}

/// <summary>
/// Receives what an <see cref="IRepositoryEventSource"/> reports.
/// </summary>
/// <remarks>
/// Called on the operating system's threads, possibly several at once: an implementation touches
/// nothing but thread-safe state.
/// </remarks>
public interface IRepositoryEventSink
{
    /// <summary>
    /// Says something changed.
    /// </summary>
    /// <param name="changes">What it was, already told apart from what does not matter.</param>
    void OnChanged(RepositoryChanges changes);

    /// <summary>
    /// Says the watch went wrong.
    /// </summary>
    /// <param name="error">What went wrong.</param>
    /// <param name="eventsLost">
    /// <see langword="true"/> when events were only lost — a buffer overflowed — and the watch goes
    /// on; <see langword="false"/> when the watch itself failed and will report nothing reliable again.
    /// </param>
    void OnFaulted(Exception error, bool eventsLost);
}
