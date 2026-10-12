using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace _855Media.Core.Utilities;

public static class CollectionSync
{
    // Retain unchanged rows so bound controls are not torn down on every filter update.
    public static void Apply<T>(ObservableCollection<T> collection, IReadOnlyList<T> desired)
    {
        var wanted = new HashSet<T>(desired);
        if (wanted.Count != desired.Count)
            throw new ArgumentException("Desired rows must be unique.", nameof(desired));
        for (var i = collection.Count - 1; i >= 0; i--)
            if (!wanted.Contains(collection[i]))
                collection.RemoveAt(i);
        for (var i = 0; i < desired.Count; i++)
        {
            if (
                i < collection.Count
                && EqualityComparer<T>.Default.Equals(collection[i], desired[i])
            )
                continue;
            var old = collection.IndexOf(desired[i]);
            if (old >= 0)
                collection.Move(old, i);
            else
                collection.Insert(i, desired[i]);
        }
        while (collection.Count > desired.Count)
            collection.RemoveAt(collection.Count - 1);
    }
}
