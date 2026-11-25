using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace Enigma.GitClient.Desktop.ViewModels;

/// <summary>
/// An <see cref="ObservableCollection{T}"/> that can be refilled, or added to, with a single
/// notification instead of one per item.
/// </summary>
/// <typeparam name="T">The type of the items.</typeparam>
/// <remarks>
/// Every <see cref="CollectionChanged"/> costs the bound list a layout pass. A list refilled item by item
/// pays it once per item, plus once for the clear, on every refresh: thousands of times for the tags of
/// an old repository. Avalonia's lists take a reset and an add of several items alike.
/// </remarks>
public sealed class RangeObservableCollection<T> : ObservableCollection<T>
{
    private static readonly PropertyChangedEventArgs CountChanged = new(nameof(Count));

    // The name ObservableCollection itself raises for its indexer.
    private static readonly PropertyChangedEventArgs IndexerChanged = new("Item[]");

    /// <summary>
    /// Initialises a new, empty instance.
    /// </summary>
    public RangeObservableCollection()
    {
    }

    /// <summary>
    /// Initialises a new instance holding the given items.
    /// </summary>
    /// <param name="items">The items, in order.</param>
    public RangeObservableCollection(IEnumerable<T> items)
        : base(items)
    {
    }

    /// <summary>
    /// Replaces every item with the given ones, in their order, and says so once, as a reset.
    /// </summary>
    /// <param name="items">The new items. Read in full before anything changes, so they may be worked out from this collection.</param>
    public void ReplaceWith(IEnumerable<T> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        CheckReentrancy();

        List<T> replacement = [.. items];

        if (Count == 0 && replacement.Count == 0)
        {
            return;
        }

        Items.Clear();

        foreach (T item in replacement)
        {
            Items.Add(item);
        }

        OnPropertyChanged(CountChanged);
        OnPropertyChanged(IndexerChanged);
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }

    /// <summary>
    /// Adds the given items at the end, in their order, and says so once, as one add of all of them.
    /// </summary>
    /// <param name="items">The items to add. Read in full before anything changes, so they may be worked out from this collection.</param>
    public void AddRange(IEnumerable<T> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        CheckReentrancy();

        List<T> added = [.. items];

        if (added.Count == 0)
        {
            return;
        }

        int start = Count;

        foreach (T item in added)
        {
            Items.Add(item);
        }

        OnPropertyChanged(CountChanged);
        OnPropertyChanged(IndexerChanged);
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, added, start));
    }
}
