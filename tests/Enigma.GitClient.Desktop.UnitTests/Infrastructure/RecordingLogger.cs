using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;

namespace Enigma.GitClient.Desktop.UnitTests.Infrastructure;

/// <summary>
/// A logger that keeps what it is told, for a test asserting what a service logged — and how often.
/// </summary>
/// <typeparam name="T">The category.</typeparam>
/// <remarks>Safe to log to from several threads at once, as a watcher does from the operating system's.</remarks>
public sealed class RecordingLogger<T> : ILogger<T>
{
    private readonly ConcurrentQueue<RecordedLogEntry> _entries = new();

    /// <summary>Gets everything logged so far, oldest first.</summary>
    public IReadOnlyList<RecordedLogEntry> Entries => [.. _entries];

    /// <inheritdoc />
    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull
        => null;

    /// <inheritdoc />
    public bool IsEnabled(LogLevel logLevel) => true;

    /// <inheritdoc />
    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        ArgumentNullException.ThrowIfNull(formatter);

        _entries.Enqueue(new RecordedLogEntry(logLevel, formatter(state, exception), exception));
    }
}

/// <summary>
/// One message a <see cref="RecordingLogger{T}"/> was given.
/// </summary>
/// <param name="Level">How serious it was.</param>
/// <param name="Message">What it said.</param>
/// <param name="Exception">The exception it carried, if any.</param>
public sealed record RecordedLogEntry(LogLevel Level, string Message, Exception? Exception);
