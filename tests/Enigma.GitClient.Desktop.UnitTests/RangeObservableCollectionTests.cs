using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using Enigma.GitClient.Desktop.ViewModels;
using Xunit;

namespace Enigma.GitClient.Desktop.UnitTests;

/// <summary>
/// The collection the pages refill their lists with (CODE-REVIEW-6E56 PERF-01): one notification per
/// refill or per batch added, never one per item, and the items in the order given.
/// </summary>
public sealed class RangeObservableCollectionTests
{
    private sealed class Recorder
    {
        public Recorder(RangeObservableCollection<string> collection)
        {
            collection.CollectionChanged += (_, e) => Changes.Add(e);
            ((INotifyPropertyChanged)collection).PropertyChanged += (_, e) => Properties.Add(e.PropertyName ?? string.Empty);
        }

        public List<NotifyCollectionChangedEventArgs> Changes { get; } = [];

        public List<string> Properties { get; } = [];
    }

    [Fact]
    public void ReplaceWith_SaysSoOnce_AsAReset_AndHoldsTheNewItemsInOrder()
    {
        RangeObservableCollection<string> collection = ["old", "older"];
        Recorder recorder = new(collection);

        collection.ReplaceWith(["c", "a", "b"]);

        Assert.Equal(["c", "a", "b"], collection);
        NotifyCollectionChangedEventArgs change = Assert.Single(recorder.Changes);
        Assert.Equal(NotifyCollectionChangedAction.Reset, change.Action);
        Assert.Equal(["Count", "Item[]"], recorder.Properties);
    }

    [Fact]
    public void ReplaceWith_TakesItsItemsFromTheCollectionItself()
    {
        RangeObservableCollection<string> collection = ["a", "b", "c"];

        collection.ReplaceWith(collection.Reverse().Where(item => item != "b"));

        Assert.Equal(["c", "a"], collection);
    }

    [Fact]
    public void ReplaceWith_NothingOnAnEmptyCollection_SaysNothing()
    {
        RangeObservableCollection<string> collection = [];
        Recorder recorder = new(collection);

        collection.ReplaceWith([]);

        Assert.Empty(recorder.Changes);
        Assert.Empty(recorder.Properties);
    }

    [Fact]
    public void ReplaceWith_Nothing_EmptiesTheCollection_WithOneReset()
    {
        RangeObservableCollection<string> collection = ["a", "b"];
        Recorder recorder = new(collection);

        collection.ReplaceWith([]);

        Assert.Empty(collection);
        Assert.Equal(NotifyCollectionChangedAction.Reset, Assert.Single(recorder.Changes).Action);
    }

    [Fact]
    public void AddRange_SaysSoOnce_AsOneAddOfEveryItem_AtTheEnd()
    {
        RangeObservableCollection<string> collection = ["first"];
        Recorder recorder = new(collection);

        collection.AddRange(["second", "third", "fourth"]);

        Assert.Equal(["first", "second", "third", "fourth"], collection);
        NotifyCollectionChangedEventArgs change = Assert.Single(recorder.Changes);
        Assert.Equal(NotifyCollectionChangedAction.Add, change.Action);
        Assert.Equal(1, change.NewStartingIndex);
        Assert.Equal(["second", "third", "fourth"], change.NewItems!.Cast<string>());
        Assert.Equal(["Count", "Item[]"], recorder.Properties);
    }

    [Fact]
    public void AddRange_Nothing_SaysNothing()
    {
        RangeObservableCollection<string> collection = ["only"];
        Recorder recorder = new(collection);

        collection.AddRange([]);

        Assert.Equal(["only"], collection);
        Assert.Empty(recorder.Changes);
        Assert.Empty(recorder.Properties);
    }
}
