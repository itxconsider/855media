using System.Collections.ObjectModel;
using System.Collections.Specialized;
using _855Media.Core.Utilities;
using Xunit;

namespace _855Media.Core.Tests.Utilities;

public sealed class TargetRefreshTests
{
    [Fact]
    public void Selecting78DevicesSchedulesOneRefreshUsingFinalSelection()
    {
        var pending = new Queue<Action>();
        var selected = new bool[78];
        var counts = new List<int>();
        using var refresh = new CoalescedAction(
            pending.Enqueue,
            () => counts.Add(selected.Count(s => s))
        );
        for (var i = 0; i < selected.Length; i++)
        {
            selected[i] = true;
            refresh.Request();
        }
        Assert.Single(pending);
        pending.Dequeue()();
        Assert.Equal(new[] { 78 }, counts);
        selected[0] = false;
        refresh.Request();
        pending.Dequeue()();
        Assert.Equal(new[] { 78, 77 }, counts);
    }

    [Fact]
    public void DisposePreventsAlreadyQueuedRefreshAndFutureRequests()
    {
        var pending = new Queue<Action>();
        var count = 0;
        var refresh = new CoalescedAction(pending.Enqueue, () => count++);
        refresh.Request();
        refresh.Dispose();
        pending.Dequeue()();
        refresh.Request();
        Assert.Empty(pending);
        Assert.Equal(0, count);
    }

    [Fact]
    public void RefreshCanRequestAnotherPass()
    {
        var pending = new Queue<Action>();
        var count = 0;
        CoalescedAction? refresh = null;
        using (
            refresh = new CoalescedAction(
                pending.Enqueue,
                () =>
                {
                    if (++count == 1)
                        refresh!.Request();
                }
            )
        )
        {
            refresh.Request();
            pending.Dequeue()();
            Assert.Single(pending);
            pending.Dequeue()();
            Assert.Equal(2, count);
        }
    }

    [Fact]
    public void FailedScheduleDoesNotSuppressRetry()
    {
        var pending = new Queue<Action>();
        var fail = true;
        var count = 0;
        using var refresh = new CoalescedAction(
            a =>
            {
                if (fail)
                    throw new InvalidOperationException();
                pending.Enqueue(a);
            },
            () => count++
        );
        Assert.Throws<InvalidOperationException>(refresh.Request);
        fail = false;
        refresh.Request();
        pending.Dequeue()();
        Assert.Equal(1, count);
    }

    [Fact]
    public void Unchanged140TargetsEmitNoCollectionNotifications()
    {
        var rows = Enumerable.Range(0, 140).Select(_ => new object()).ToArray();
        var collection = new ObservableCollection<object>(rows);
        var notifications = 0;
        collection.CollectionChanged += (_, _) => notifications++;
        for (var i = 0; i < 78; i++)
            CollectionSync.Apply(collection, rows);
        Assert.Equal(0, notifications);
        Assert.Equal(rows, collection);
    }

    [Fact]
    public void FilteringAndReorderingKeepsRowsAndNeverResetsCollection()
    {
        var a = new object();
        var b = new object();
        var c = new object();
        var d = new object();
        var collection = new ObservableCollection<object>(new[] { a, b, c });
        var events = new List<NotifyCollectionChangedAction>();
        collection.CollectionChanged += (_, e) => events.Add(e.Action);
        CollectionSync.Apply(collection, new[] { c, a, d });
        Assert.Equal(new[] { c, a, d }, collection);
        Assert.DoesNotContain(NotifyCollectionChangedAction.Reset, events);
        Assert.Contains(NotifyCollectionChangedAction.Move, events);
        CollectionSync.Apply(collection, Array.Empty<object>());
        Assert.Empty(collection);
        Assert.DoesNotContain(NotifyCollectionChangedAction.Reset, events);
    }

    [Fact]
    public void DuplicateDesiredRowsFailBeforeMutatingTheBoundCollection()
    {
        var row = new object();
        var collection = new ObservableCollection<object>(new[] { row });
        Assert.Throws<ArgumentException>(() =>
            CollectionSync.Apply(collection, new[] { row, row })
        );
        Assert.Same(row, Assert.Single(collection));
    }
}
