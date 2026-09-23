using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Enigma.GitClient.Core.Configuration;
using Xunit;

namespace Enigma.GitClient.Core.UnitTests.Configuration;

/// <summary>
/// Replacing a configuration file in one step, which is what lets several running instances share
/// it.
/// </summary>
public sealed class AtomicFileTests : IDisposable
{
    private readonly string _root;

    public AtomicFileTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "enigma-atomic-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private string Target => Path.Combine(_root, "document.json");

    [Fact]
    public void WriteAllText_CreatesTheFileAndLeavesNothingBeside()
    {
        AtomicFile.WriteAllText(Target, "{\"a\":1}");

        Assert.Equal("{\"a\":1}", File.ReadAllText(Target));
        Assert.Equal([Target], Directory.GetFiles(_root));
    }

    [Fact]
    public void WriteAllText_ReplacesWhatWasThere()
    {
        File.WriteAllText(Target, "a much longer document than the one that replaces it");

        AtomicFile.WriteAllText(Target, "short");

        Assert.Equal("short", File.ReadAllText(Target));
    }

    [Fact]
    public void WriteAllText_WritesUtf8WithoutAByteOrderMark()
    {
        AtomicFile.WriteAllText(Target, "é");

        Assert.Equal([0xC3, 0xA9], File.ReadAllBytes(Target));
    }

    [Fact]
    public async Task WriteAllTextAsync_ReplacesTheFileAndLeavesNothingBeside()
    {
        File.WriteAllText(Target, "old");

        await AtomicFile.WriteAllTextAsync(Target, "new", Encoding.UTF8, TestContext.Current.CancellationToken);

        Assert.Equal("new", File.ReadAllText(Target));
        Assert.Equal([Target], Directory.GetFiles(_root));
    }

    [Fact]
    public async Task WriteAllTextAsync_Cancelled_LeavesTheOldFileAndNoTemporary()
    {
        File.WriteAllText(Target, "old");

        using CancellationTokenSource cancelled = new();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => AtomicFile.WriteAllTextAsync(Target, "new", null, cancelled.Token));

        Assert.Equal("old", File.ReadAllText(Target));
        Assert.Equal([Target], Directory.GetFiles(_root));
    }

    [Fact]
    public async Task AReaderRacingTheWriter_AlwaysReadsAWholeDocument()
    {
        // Two documents of very different lengths, so a torn read would be a truncated one.
        string small = JsonSerializer.Serialize(new { Items = Enumerable.Range(0, 5).ToArray() });
        string large = JsonSerializer.Serialize(new { Items = Enumerable.Range(0, 5000).ToArray() });
        AtomicFile.WriteAllText(Target, small);

        using CancellationTokenSource stop = new();
        CancellationToken token = TestContext.Current.CancellationToken;

        Task writer = Task.Run(
            () =>
            {
                for (int round = 0; round < 300; round++)
                {
                    AtomicFile.WriteAllText(Target, round % 2 == 0 ? large : small);
                }

                stop.Cancel();
            },
            token);

        int reads = 0;

        while (!stop.IsCancellationRequested)
        {
            string text;

            try
            {
                text = File.ReadAllText(Target);
            }
            catch (IOException)
            {
                // Windows may refuse to open a file for the instant it is being replaced; what must
                // never happen is reading half of one.
                continue;
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }

            Assert.True(text == small || text == large, $"A torn read of {text.Length} characters");
            reads++;
        }

        await writer;

        Assert.True(reads > 0);
    }
}
