using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Enigma.GitClient.Core.Repositories;

namespace Enigma.GitClient.Desktop.Services;

/// <summary>
/// Default <see cref="IRepositoryEventSource"/>: the operating system's own notifications, through
/// <see cref="FileSystemWatcher"/> — inotify on Linux, <c>ReadDirectoryChangesW</c> on Windows,
/// FSEvents on macOS.
/// </summary>
/// <remarks>
/// <para>
/// Three watches, four in a linked worktree:
/// </para>
/// <list type="bullet">
/// <item><description>the work tree, recursive;</description></item>
/// <item><description>
/// the git directory, flat: HEAD, the index and the merge state, never <c>objects/</c>;
/// </description></item>
/// <item><description><c>refs/</c> of the common directory, recursive;</description></item>
/// <item><description>
/// the common directory, flat, when it is not the git directory: its <c>packed-refs</c> and
/// <c>config</c>.
/// </description></item>
/// </list>
/// <para>
/// On Linux a recursive watch is one inotify watch per directory, added before
/// <see cref="Watch"/> returns — which is why it is never called on the UI thread — and it cannot leave
/// a subtree out: the work tree's watch covers <c>node_modules</c> and the git directory too, whose
/// events <see cref="RepositoryLayout.Classify"/> drops. Every watch gets a 64 KB buffer, the most
/// Windows takes for a network share's.
/// </para>
/// </remarks>
public sealed class FileSystemRepositoryEventSource : IRepositoryEventSource
{
    /// <summary>The buffer every watch is given, in bytes.</summary>
    public const int BufferSize = 64 * 1024;

    /// <inheritdoc />
    public IDisposable Watch(RepositoryHandle repository, IRepositoryEventSink sink)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(sink);

        RepositoryWatch watch = new(ResolveLayout(repository), sink);

        try
        {
            watch.Start();
            return watch;
        }
        catch
        {
            watch.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Works out where a repository's references live: the directory git's own <c>commondir</c> file
    /// names, relative to the git directory, or the git directory itself when there is none.
    /// </summary>
    /// <param name="repository">The repository.</param>
    /// <returns>Its layout.</returns>
    /// <remarks>
    /// A <c>commondir</c> that cannot be read, or that names nothing that exists, is ignored: watching
    /// the git directory alone still catches HEAD and the index, and the periodic refresh the rest.
    /// </remarks>
    public static RepositoryLayout ResolveLayout(RepositoryHandle repository)
    {
        ArgumentNullException.ThrowIfNull(repository);

        string common = repository.GitDirectory;

        try
        {
            string file = Path.Combine(repository.GitDirectory, "commondir");

            if (File.Exists(file) && File.ReadAllText(file).Trim() is { Length: > 0 } named)
            {
                string resolved = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.Combine(repository.GitDirectory, named)));

                if (Directory.Exists(resolved))
                {
                    common = resolved;
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            // Read as no common directory at all.
        }

        return new RepositoryLayout(repository.WorkTreePath, repository.GitDirectory, common);
    }

    /// <summary>
    /// One repository's watches, reporting to one sink until disposed.
    /// </summary>
    private sealed class RepositoryWatch(RepositoryLayout layout, IRepositoryEventSink sink) : IDisposable
    {
        private readonly List<FileSystemWatcher> _watchers = [];
        private int _disposed;

        public void Start()
        {
            const NotifyFilters Files = NotifyFilters.FileName | NotifyFilters.LastWrite;
            const NotifyFilters FilesAndFolders = Files | NotifyFilters.DirectoryName;

            Add(layout.WorkTree, recursive: true, FilesAndFolders);
            Add(layout.GitDirectory, recursive: false, Files);
            Add(Path.Combine(layout.CommonDirectory, "refs"), recursive: true, FilesAndFolders);

            if (!string.Equals(layout.CommonDirectory, layout.GitDirectory, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            {
                Add(layout.CommonDirectory, recursive: false, Files);
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 1)
            {
                return;
            }

            foreach (FileSystemWatcher watcher in _watchers)
            {
                watcher.Created -= OnChanged;
                watcher.Changed -= OnChanged;
                watcher.Deleted -= OnChanged;
                watcher.Renamed -= OnRenamed;
                watcher.Error -= OnError;

                // Disposing stops it and lets its inotify instance go.
                watcher.Dispose();
            }

            _watchers.Clear();
        }

        private void Add(string directory, bool recursive, NotifyFilters filters)
        {
            if (!Directory.Exists(directory))
            {
                throw new DirectoryNotFoundException($"The directory '{directory}' cannot be watched: it does not exist.");
            }

            FileSystemWatcher watcher = new(directory)
            {
                IncludeSubdirectories = recursive,
                NotifyFilter = filters,
                InternalBufferSize = BufferSize,
            };

            watcher.Created += OnChanged;
            watcher.Changed += OnChanged;
            watcher.Deleted += OnChanged;
            watcher.Renamed += OnRenamed;
            watcher.Error += OnError;

            // Listed before it starts, so a start that throws still has it disposed with the rest.
            _watchers.Add(watcher);
            watcher.EnableRaisingEvents = true;
        }

        private void OnChanged(object sender, FileSystemEventArgs e) => Report(layout.Classify(e.FullPath));

        // Both ends: HEAD.lock renamed to HEAD is a change of HEAD, a file renamed out of the work tree
        // is a change of the work tree.
        private void OnRenamed(object sender, RenamedEventArgs e)
            => Report(layout.Classify(e.OldFullPath) | layout.Classify(e.FullPath));

        private void OnError(object sender, ErrorEventArgs e)
        {
            if (Volatile.Read(ref _disposed) == 1)
            {
                return;
            }

            Exception error = e.GetException();
            sink.OnFaulted(error, eventsLost: error is InternalBufferOverflowException);
        }

        private void Report(RepositoryChanges changes)
        {
            if (changes != RepositoryChanges.None && Volatile.Read(ref _disposed) == 0)
            {
                sink.OnChanged(changes);
            }
        }
    }
}
