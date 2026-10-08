using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Enigma.GitClient.Core.Repositories;
using Enigma.GitClient.Desktop.Services;
using Xunit;

namespace Enigma.GitClient.Desktop.UnitTests;

/// <summary>
/// The operating system's side of the watcher, on a real directory laid out the way git lays a
/// repository out: what it reports, what it keeps quiet about, and that it stops when told to.
/// </summary>
/// <remarks>
/// No git process and no Avalonia type: the directories are made by hand, so these run anywhere and
/// alongside everything else.
/// </remarks>
public sealed class FileSystemRepositoryEventSourceTests : IDisposable
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "enigma-watch-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    /// <summary>A plain repository's skeleton: a work tree, its .git, refs and objects.</summary>
    private RepositoryHandle MakeRepository(string name = "repo")
    {
        string workTree = Path.Combine(_root, name);
        string gitDirectory = Path.Combine(workTree, ".git");

        Directory.CreateDirectory(Path.Combine(gitDirectory, "refs", "heads"));
        Directory.CreateDirectory(Path.Combine(gitDirectory, "objects"));
        File.WriteAllText(Path.Combine(gitDirectory, "HEAD"), "ref: refs/heads/main\n");

        return new RepositoryHandle(workTree, gitDirectory);
    }

    // ---------------------------------------------------------------- the layout

    [Fact]
    public void ThePlainLayout_HasTheGitDirectoryAsItsCommonDirectory()
    {
        RepositoryHandle repository = MakeRepository();

        RepositoryLayout layout = FileSystemRepositoryEventSource.ResolveLayout(repository);

        Assert.Equal(repository.WorkTreePath, layout.WorkTree);
        Assert.Equal(repository.GitDirectory, layout.CommonDirectory);
    }

    [Fact]
    public void ALinkedWorktreesCommonDirectory_IsWhatItsCommondirFileNames()
    {
        RepositoryHandle main = MakeRepository("main");
        string gitDirectory = Path.Combine(main.GitDirectory, "worktrees", "wt");
        Directory.CreateDirectory(gitDirectory);
        File.WriteAllText(Path.Combine(gitDirectory, "commondir"), "../..\n");

        RepositoryLayout layout = FileSystemRepositoryEventSource.ResolveLayout(new RepositoryHandle(Path.Combine(_root, "wt"), gitDirectory));

        Assert.Equal(main.GitDirectory, layout.CommonDirectory);
        Assert.Equal(gitDirectory, layout.GitDirectory);
    }

    [Theory]
    [InlineData("../../nowhere")]
    [InlineData("   ")]
    public void ACommondirNamingNothing_IsIgnored(string named)
    {
        RepositoryHandle repository = MakeRepository();
        File.WriteAllText(Path.Combine(repository.GitDirectory, "commondir"), named);

        Assert.Equal(repository.GitDirectory, FileSystemRepositoryEventSource.ResolveLayout(repository).CommonDirectory);
    }

    // ---------------------------------------------------------------- the watch

    [Fact]
    public async Task AWorkTreeFile_IsReportedAsTheWorkingTree()
    {
        RepositoryHandle repository = MakeRepository();
        RecordingSink sink = new();

        using IDisposable watch = new FileSystemRepositoryEventSource().Watch(repository, sink);

        await File.WriteAllTextAsync(Path.Combine(repository.WorkTreePath, "new.txt"), "hello", TestContext.Current.CancellationToken);

        await sink.UntilAsync(RepositoryChanges.WorkingTree);
    }

    [Fact]
    public async Task HeadWrittenThroughItsLock_IsReportedAsTheReferences()
    {
        RepositoryHandle repository = MakeRepository();
        RecordingSink sink = new();

        using IDisposable watch = new FileSystemRepositoryEventSource().Watch(repository, sink);

        // As git writes it: the lock first, then renamed into place.
        string head = Path.Combine(repository.GitDirectory, "HEAD");
        await File.WriteAllTextAsync(head + ".lock", "ref: refs/heads/other\n", TestContext.Current.CancellationToken);
        File.Move(head + ".lock", head, overwrite: true);

        await sink.UntilAsync(RepositoryChanges.References);
    }

    [Fact]
    public async Task ABranchCreated_IsReportedAsTheReferences()
    {
        RepositoryHandle repository = MakeRepository();
        RecordingSink sink = new();

        using IDisposable watch = new FileSystemRepositoryEventSource().Watch(repository, sink);

        Directory.CreateDirectory(Path.Combine(repository.GitDirectory, "refs", "heads", "feature"));
        await File.WriteAllTextAsync(
            Path.Combine(repository.GitDirectory, "refs", "heads", "feature", "watcher"),
            new string('a', 40) + "\n",
            TestContext.Current.CancellationToken);

        await sink.UntilAsync(RepositoryChanges.References);
    }

    [Fact]
    public async Task ObjectsWritten_AreNotReported()
    {
        RepositoryHandle repository = MakeRepository();
        RecordingSink sink = new();

        using IDisposable watch = new FileSystemRepositoryEventSource().Watch(repository, sink);

        string objects = Path.Combine(repository.GitDirectory, "objects", "ab");
        Directory.CreateDirectory(objects);
        await File.WriteAllTextAsync(Path.Combine(objects, "cdef"), "blob", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(repository.GitDirectory, "FETCH_HEAD"), "x", TestContext.Current.CancellationToken);

        // A work-tree file written afterwards: once it has been reported, so has everything before it.
        await File.WriteAllTextAsync(Path.Combine(repository.WorkTreePath, "after.txt"), "x", TestContext.Current.CancellationToken);
        await sink.UntilAsync(RepositoryChanges.WorkingTree);

        Assert.All(sink.Changes, changes => Assert.Equal(RepositoryChanges.WorkingTree, changes));
        Assert.Empty(sink.Faults);
    }

    [Fact]
    public async Task ADisposedWatch_ReportsNothingMore()
    {
        RepositoryHandle repository = MakeRepository();
        RecordingSink sink = new();

        IDisposable watch = new FileSystemRepositoryEventSource().Watch(repository, sink);
        watch.Dispose();

        await File.WriteAllTextAsync(Path.Combine(repository.WorkTreePath, "late.txt"), "x", TestContext.Current.CancellationToken);
        await Task.Delay(300, TestContext.Current.CancellationToken);

        Assert.Empty(sink.Changes);
    }

    [Fact]
    public void AWorkTreeThatIsGone_CannotBeWatched()
    {
        RepositoryHandle repository = new(Path.Combine(_root, "gone"), Path.Combine(_root, "gone", ".git"));

        Assert.Throws<DirectoryNotFoundException>(() => new FileSystemRepositoryEventSource().Watch(repository, new RecordingSink()));
    }

    /// <summary>
    /// Keeps what a watch reports, and lets a test wait for one kind of change in real time.
    /// </summary>
    private sealed class RecordingSink : IRepositoryEventSink
    {
        private readonly ConcurrentQueue<RepositoryChanges> _changes = new();
        private readonly ConcurrentQueue<Exception> _faults = new();

        public RepositoryChanges[] Changes => [.. _changes];

        public Exception[] Faults => [.. _faults];

        public void OnChanged(RepositoryChanges changes) => _changes.Enqueue(changes);

        // Recorded, never thrown: this runs on the watcher's own thread, where nothing would catch it.
        public void OnFaulted(Exception error, bool eventsLost) => _faults.Enqueue(error);

        public async Task UntilAsync(RepositoryChanges expected)
        {
            using CancellationTokenSource patience = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            patience.CancelAfter(Patience);

            while (!_changes.Any(changes => changes.HasFlag(expected)))
            {
                await Task.Delay(10, patience.Token);
            }
        }
    }
}
