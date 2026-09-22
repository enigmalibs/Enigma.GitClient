using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Enigma.GitClient.App.UnitTests.Infrastructure;

/// <summary>
/// A clock that only moves when a test moves it, so a service that ticks every fifteen seconds can be
/// tested in none.
/// </summary>
/// <remarks>
/// Also what the shared test container runs on: a real clock would let the automatic refresh fire in
/// the middle of an unrelated test that happened to run for fifteen seconds.
/// </remarks>
public sealed class ManualTimeProvider : TimeProvider
{
    private readonly List<ManualTimer> _timers = [];
    private readonly object _gate = new();
    private DateTimeOffset _now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>Gets how many timers are waiting to fire.</summary>
    public int ActiveTimers
    {
        get
        {
            lock (_gate)
            {
                return _timers.Count(timer => timer.Due is not null);
            }
        }
    }

    /// <inheritdoc />
    public override DateTimeOffset GetUtcNow()
    {
        lock (_gate)
        {
            return _now;
        }
    }

    /// <inheritdoc />
    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        ManualTimer timer = new(this, callback, state);

        lock (_gate)
        {
            _timers.Add(timer);
        }

        timer.Change(dueTime, period);
        return timer;
    }

    /// <summary>
    /// Moves the clock on, firing every timer that falls due on the way, in order.
    /// </summary>
    /// <param name="by">How far.</param>
    public void Advance(TimeSpan by)
    {
        DateTimeOffset target;

        lock (_gate)
        {
            target = _now + by;
        }

        while (true)
        {
            ManualTimer? next;

            lock (_gate)
            {
                next = _timers
                    .Where(timer => timer.Due is { } due && due <= target)
                    .OrderBy(timer => timer.Due)
                    .FirstOrDefault();

                if (next is null)
                {
                    _now = target;
                    return;
                }

                _now = next.Due!.Value;
                next.Due = next.Period > TimeSpan.Zero ? _now + next.Period : null;
            }

            next.Fire();
        }
    }

    private void Remove(ManualTimer timer)
    {
        lock (_gate)
        {
            _timers.Remove(timer);
        }
    }

    private sealed class ManualTimer(ManualTimeProvider owner, TimerCallback callback, object? state) : ITimer
    {
        public DateTimeOffset? Due { get; set; }

        public TimeSpan Period { get; private set; }

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            lock (owner._gate)
            {
                Period = period == Timeout.InfiniteTimeSpan ? TimeSpan.Zero : period;
                Due = dueTime == Timeout.InfiniteTimeSpan ? null : owner._now + dueTime;
            }

            return true;
        }

        public void Fire() => callback(state);

        public void Dispose() => owner.Remove(this);

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
