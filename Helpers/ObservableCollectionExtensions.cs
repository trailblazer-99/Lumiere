using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace LumiereMediaPlayer.Helpers;

public static class ObservableCollectionExtensions
{
    public static void UpdateInPlace<T>(this ObservableCollection<T> collection, IReadOnlyList<T> newItems) where T : class
    {
        if (collection.SequenceEqual(newItems))
        {
            return;
        }

        int minCount = System.Math.Min(collection.Count, newItems.Count);

        for (int i = 0; i < minCount; i++)
        {
            if (!ReferenceEquals(collection[i], newItems[i]))
            {
                collection[i] = newItems[i];
            }
        }

        // Remove excess items from the end
        for (int i = collection.Count - 1; i >= minCount; i--)
        {
            collection.RemoveAt(i);
        }

        // Add new items
        for (int i = minCount; i < newItems.Count; i++)
        {
            collection.Add(newItems[i]);
        }
    }
}
