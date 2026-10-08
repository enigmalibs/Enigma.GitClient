using System;
using System.Collections.Generic;
using System.Threading;
using Enigma.GitClient.Core.Repositories;
using Enigma.GitClient.Desktop.Services;

namespace Enigma.GitClient.Desktop.UnitTests.Infrastructure;

/// <summary>
/// An <see cref="IRepositoryEventSource"/> that watches nothing: a test reports the changes itself,
/// through the sink each watch was handed.
/// </summary>
/// <remarks>
/// The watcher starts a watch on the thread pool, so what is recorded here is read under a lock.
/// </remarks>
public sealed class ScriptedRepositoryEventSource : IRepositoryEventSource
{
    private readonly object _gate = new();
    private readonly List<ScriptedWatch> _watches = [];
    private int _attempts;

    /// <summary>
    /// Gets or sets what starting a watch throws, as the operating system would refuse one; unset, every
    /// watch starts.
    /// </summary>
    public Exception? Failure { get; set; }

    /// <summary>Gets how many watches were asked for, whether they started or not.</summary>
    public int Attempts => Volatile.Read(ref _attempts);

    /// <summary>Gets every watch started so far, oldest first.</summary>
    public IReadOnlyList<ScriptedWatch> Watches
    {
        get
        {
            lock (_gate)
            {
                return [.. _watches];
            }
        }
    }

    /// <summary>Gets the latest watch started.</summary>
    public ScriptedWatch Current
    {
        get
        {
            lock (_gate)
            {
                return _watches[^1];
            }
        }
    }

    /// <inheritdoc />
    public IDisposable Watch(RepositoryHandle repository, IRepositoryEventSink sink)
    {
        Interlocked.Increment(ref _attempts);

        if (Failure is { } failure)
        {
            throw failure;
        }

        ScriptedWatch watch = new(repository, sink);

        lock (_gate)
        {
            _watches.Add(watch);
        }

        return watch;
    }
}

/// <summary>
/// One watch a <see cref="ScriptedRepositoryEventSource"/> started.
/// </summary>
/// <param name="repository">The repository it watches.</param>
/// <param name="sink">Where the test reports what changed.</param>
public sealed class ScriptedWatch(RepositoryHandle repository, IRepositoryEventSink sink) : IDisposable
{
    private int _disposed;

    /// <summary>Gets the repository the watch is on.</summary>
    public RepositoryHandle Repository { get; } = repository;

    /// <summary>Gets the sink the watcher handed over, which a test reports through.</summary>
    public IRepositoryEventSink Sink { get; } = sink;

    /// <summary>Gets a value indicating whether the watcher has let go of the watch.</summary>
    public bool IsDisposed => Volatile.Read(ref _disposed) == 1;

    /// <inheritdoc />
    public void Dispose() => Interlocked.Exchange(ref _disposed, 1);
}
