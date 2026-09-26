using System;
using System.Collections.Generic;

namespace Enigma.GitClient.Core.Graph;

/// <summary>
/// The lane table at the boundary between two pages of history. Carrying it into the next
/// <see cref="CommitGraphLayout"/> pass makes paging seamless: laying out a history in pages
/// produces exactly the same rows as laying it out in one go.
/// </summary>
public sealed class GraphLayoutState
{
    private readonly List<string?> _lanes;
    private readonly List<int> _laneColours;

    /// <summary>
    /// Initialises an empty state, which is where a history starts.
    /// </summary>
    public GraphLayoutState()
    {
        _lanes = [];
        _laneColours = [];
        NextColour = 0;
    }

    private GraphLayoutState(List<string?> lanes, List<int> laneColours, int nextColour)
    {
        _lanes = lanes;
        _laneColours = laneColours;
        NextColour = nextColour;
    }

    /// <summary>
    /// Gets the number of lanes the table currently spans, including any freed gaps between them.
    /// </summary>
    public int LaneCount => _lanes.Count;

    /// <summary>
    /// Gets the number of lanes still waiting for a commit that has not been laid out yet.
    /// </summary>
    public int OpenLaneCount
    {
        get
        {
            int count = 0;
            foreach (string? sha in _lanes)
            {
                if (sha is not null)
                {
                    count++;
                }
            }

            return count;
        }
    }

    /// <summary>
    /// Gets the palette index the next newly opened lane will start searching from.
    /// </summary>
    public int NextColour { get; private set; }

    /// <summary>
    /// Creates an independent copy, so laying out a page never mutates the caller's state.
    /// </summary>
    /// <returns>The copy.</returns>
    public GraphLayoutState Clone() => new([.. _lanes], [.. _laneColours], NextColour);

    /// <summary>
    /// Gets the SHA a lane is waiting for, or <see langword="null"/> when the lane is free.
    /// </summary>
    /// <param name="lane">The lane index.</param>
    /// <returns>The awaited SHA, or <see langword="null"/>.</returns>
    public string? GetAwaitedSha(int lane)
        => lane >= 0 && lane < _lanes.Count ? _lanes[lane] : null;

    /// <summary>
    /// Gets the palette index assigned to a lane.
    /// </summary>
    /// <param name="lane">The lane index.</param>
    /// <returns>The palette index, or zero when the lane has never been opened.</returns>
    public int GetColour(int lane)
        => lane >= 0 && lane < _laneColours.Count ? _laneColours[lane] : 0;

    internal List<string?> Lanes => _lanes;

    internal List<int> LaneColours => _laneColours;

    internal void Reserve(int lane, string sha)
    {
        EnsureCapacity(lane);
        _lanes[lane] = sha;
    }

    internal void Release(int lane)
    {
        if (lane >= 0 && lane < _lanes.Count)
        {
            _lanes[lane] = null;
        }
    }

    internal int FindFreeLane()
    {
        for (int index = 0; index < _lanes.Count; index++)
        {
            if (_lanes[index] is null)
            {
                return index;
            }
        }

        return _lanes.Count;
    }

    internal int OpenLane(int lane, string sha, int colourCount)
    {
        EnsureCapacity(lane);

        // The colour is picked before the lane is occupied, so the lane's own stale colour from a
        // previous occupant does not count as "already in use".
        int colour = PickColour(colourCount);

        _lanes[lane] = sha;
        _laneColours[lane] = colour;
        return colour;
    }

    internal void TrimTrailingFreeLanes()
    {
        while (_lanes.Count > 0 && _lanes[^1] is null)
        {
            _lanes.RemoveAt(_lanes.Count - 1);
            _laneColours.RemoveAt(_laneColours.Count - 1);
        }
    }

    /// <summary>
    /// Picks the palette index for a lane about to be opened: the first colour, starting from the
    /// rotating cursor, that no currently open lane is using. When every colour is in use the
    /// cursor's colour is taken anyway — a graph that wide has no non-repeating option left.
    /// </summary>
    private int PickColour(int colourCount)
    {
        for (int attempt = 0; attempt < colourCount; attempt++)
        {
            int candidate = (NextColour + attempt) % colourCount;

            if (!IsColourInUse(candidate))
            {
                NextColour = (candidate + 1) % colourCount;
                return candidate;
            }
        }

        int fallback = NextColour % colourCount;
        NextColour = (fallback + 1) % colourCount;
        return fallback;
    }

    private bool IsColourInUse(int colour)
    {
        for (int index = 0; index < _lanes.Count; index++)
        {
            if (_lanes[index] is not null && _laneColours[index] == colour)
            {
                return true;
            }
        }

        return false;
    }

    private void EnsureCapacity(int lane)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(lane);

        while (_lanes.Count <= lane)
        {
            _lanes.Add(null);
            _laneColours.Add(0);
        }
    }
}
