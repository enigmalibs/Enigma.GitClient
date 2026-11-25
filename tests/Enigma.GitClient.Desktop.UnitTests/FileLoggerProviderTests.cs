using System;
using System.IO;
using System.Linq;
using Enigma.GitClient.Core.Configuration;
using Enigma.GitClient.Desktop.Services;
using Enigma.GitClient.Desktop.UnitTests.Infrastructure;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Enigma.GitClient.Desktop.UnitTests;

/// <summary>
/// The log file (CODE-REVIEW-6E56 LOG-01): one redacted entry per line, one file a day in
/// <c>logs</c> under the configuration directory, and only the most recent days kept.
/// </summary>
/// <remarks>
/// Disposing the provider drains its queue, so each test reads the files after disposing it and
/// never waits on a delay.
/// </remarks>
public sealed class FileLoggerProviderTests : IDisposable
{
    private readonly string _root;
    private readonly ManualTimeProvider _clock = new();

    public FileLoggerProviderTests()
        => _root = Path.Combine(Path.GetTempPath(), "enigma-log-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private string LogDirectory => Path.Combine(_root, "logs");

    private FileLoggerProvider Build() => new(new AppPaths(_root), _clock);

    private string FileOf(DateOnly day) => Path.Combine(LogDirectory, RollingLogFile.FileNameOf(day));

    private string[] LogFileNames()
        => [.. Directory.EnumerateFiles(LogDirectory).Select(Path.GetFileName).OfType<string>().Order(StringComparer.Ordinal)];

    [Fact]
    public void TheLogDirectory_IsLogsUnderTheConfigurationDirectory()
        => Assert.Equal(LogDirectory, FileLoggerProvider.DirectoryIn(new FixedAppPaths(_root)));

    [Fact]
    public void AnEntry_IsOneLine_WithItsUtcTimeLevelCategoryAndMessage()
    {
        using (FileLoggerProvider provider = Build())
        {
            provider.CreateLogger("Enigma.GitClient.Tests").LogInformation("Opened {Path}", "C:/work/repo");
        }

        string[] lines = File.ReadAllLines(FileOf(new DateOnly(2026, 1, 1)));

        Assert.Equal(["2026-01-01T00:00:00.000Z INFO  Enigma.GitClient.Tests: Opened C:/work/repo"], lines);
    }

    [Fact]
    public void ACredential_NeverReachesTheFile_InTheMessageOrTheException()
    {
        using (FileLoggerProvider provider = Build())
        {
            InvalidOperationException exception = new("Pushing to https://bob:hunter2-in-the-exception@example.com/a.git failed");
            provider.CreateLogger("Tests").LogError(exception, "Cloning {Url} failed", "https://alice:s3cret-in-the-message@example.com/repo.git");
        }

        string text = File.ReadAllText(FileOf(new DateOnly(2026, 1, 1)));

        Assert.Contains("https://alice:***@example.com/repo.git", text, StringComparison.Ordinal);
        Assert.Contains("https://bob:***@example.com/a.git", text, StringComparison.Ordinal);
        Assert.DoesNotContain("s3cret-in-the-message", text, StringComparison.Ordinal);
        Assert.DoesNotContain("hunter2-in-the-exception", text, StringComparison.Ordinal);
    }

    [Fact]
    public void AnException_FollowsItsEntry_Indented()
    {
        using (FileLoggerProvider provider = Build())
        {
            provider.CreateLogger("Tests").LogError(new InvalidOperationException("broken"), "It failed");
            provider.CreateLogger("Tests").LogInformation("Next");
        }

        string[] lines = File.ReadAllLines(FileOf(new DateOnly(2026, 1, 1)));

        Assert.Equal("2026-01-01T00:00:00.000Z ERROR Tests: It failed", lines[0]);
        Assert.Equal("    System.InvalidOperationException: broken", lines[1]);
        Assert.Equal("2026-01-01T00:00:00.000Z INFO  Tests: Next", lines[^1]);
    }

    [Fact]
    public void ANewDay_StartsANewFile()
    {
        using (FileLoggerProvider provider = Build())
        {
            ILogger logger = provider.CreateLogger("Tests");
            logger.LogInformation("Before midnight");
            _clock.Advance(TimeSpan.FromDays(1));
            logger.LogInformation("After midnight");
        }

        Assert.Equal(["enigma-gitclient-20260101.log", "enigma-gitclient-20260102.log"], LogFileNames());
        Assert.EndsWith("Before midnight", Assert.Single(File.ReadAllLines(FileOf(new DateOnly(2026, 1, 1)))), StringComparison.Ordinal);
        Assert.EndsWith("After midnight", Assert.Single(File.ReadAllLines(FileOf(new DateOnly(2026, 1, 2)))), StringComparison.Ordinal);
    }

    [Fact]
    public void TheFilesOfOlderDays_AreDeleted_BeyondTheRetention()
    {
        Directory.CreateDirectory(LogDirectory);

        // Ten earlier days, with a gap: the most recent days kept are the days that have a file.
        DateOnly today = new(2026, 1, 1);
        for (int daysAgo = 1; daysAgo <= 11; daysAgo++)
        {
            if (daysAgo != 3)
            {
                File.WriteAllText(FileOf(today.AddDays(-daysAgo)), "earlier");
            }
        }

        File.WriteAllText(Path.Combine(LogDirectory, "notes.txt"), "not a log");
        File.WriteAllText(Path.Combine(LogDirectory, "enigma-gitclient-old.log"), "not a day");

        using (FileLoggerProvider provider = Build())
        {
            provider.CreateLogger("Tests").LogInformation("Today");
        }

        Assert.Equal(
            [
                "enigma-gitclient-20251225.log",
                "enigma-gitclient-20251226.log",
                "enigma-gitclient-20251227.log",
                "enigma-gitclient-20251228.log",
                "enigma-gitclient-20251230.log",
                "enigma-gitclient-20251231.log",
                "enigma-gitclient-20260101.log",
                "enigma-gitclient-old.log",
                "notes.txt",
            ],
            LogFileNames());
        Assert.Equal(FileLoggerProvider.RetainedDays, LogFileNames().Count(name => name.StartsWith("enigma-gitclient-2", StringComparison.Ordinal)));
    }

    [Fact]
    public void ADayAlreadyWritten_IsAppendedTo()
    {
        using (FileLoggerProvider provider = Build())
        {
            provider.CreateLogger("Tests").LogInformation("First run");
        }

        using (FileLoggerProvider provider = Build())
        {
            provider.CreateLogger("Tests").LogInformation("Second run");
        }

        string[] lines = File.ReadAllLines(FileOf(new DateOnly(2026, 1, 1)));

        Assert.Equal(2, lines.Length);
        Assert.EndsWith("First run", lines[0], StringComparison.Ordinal);
        Assert.EndsWith("Second run", lines[1], StringComparison.Ordinal);
    }

    [Fact]
    public void TwoWritersOfTheSameDay_NeverWriteOverEachOther()
    {
        // As two instances of the application do: each its own provider, the same directory.
        using (FileLoggerProvider first = Build())
        using (FileLoggerProvider second = Build())
        {
            for (int index = 0; index < 50; index++)
            {
                first.CreateLogger("First").LogInformation("Entry {Index}", index);
                second.CreateLogger("Second").LogInformation("Entry {Index}", index);
            }
        }

        string[] lines = File.ReadAllLines(FileOf(new DateOnly(2026, 1, 1)));

        Assert.Equal(100, lines.Length);
        Assert.All(lines, line => Assert.Matches(@"^2026-01-01T00:00:00\.000Z INFO  (First|Second): Entry \d+$", line));
        Assert.Equal(50, lines.Count(line => line.Contains(" First: ", StringComparison.Ordinal)));
    }

    [Fact]
    public void NothingIsWritten_BeforeTheFirstEntry_OrAfterDisposal()
    {
        FileLoggerProvider provider = Build();
        ILogger logger = provider.CreateLogger("Tests");
        provider.Dispose();

        logger.LogInformation("Too late");
        provider.Dispose();

        Assert.False(Directory.Exists(LogDirectory));
    }
}
