// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Collections.ObjectModel;

namespace Foundry.Utilities.Collections;

/// <summary>
/// Provides in-place synchronization helpers for observable collections.
/// </summary>
public static class ObservableCollectionExtensions
{
    /// <summary>
    /// Synchronizes a collection by reference identity without raising a collection reset event.
    /// </summary>
    /// <typeparam name="T">The reference type stored in the collection.</typeparam>
    /// <param name="collection">The collection to update.</param>
    /// <param name="desiredItems">The desired item references in their target order.</param>
    public static void SynchronizeReferences<T>(this ObservableCollection<T> collection, IReadOnlyList<T> desiredItems)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(collection);
        ArgumentNullException.ThrowIfNull(desiredItems);

        for (int index = collection.Count - 1; index >= 0; index--)
        {
            if (!ContainsReference(desiredItems, collection[index]))
            {
                collection.RemoveAt(index);
            }
        }

        for (int targetIndex = 0; targetIndex < desiredItems.Count; targetIndex++)
        {
            T desiredItem = desiredItems[targetIndex];
            if (targetIndex < collection.Count && ReferenceEquals(collection[targetIndex], desiredItem))
            {
                continue;
            }

            int existingIndex = FindReferenceIndex(collection, desiredItem, targetIndex + 1);
            if (existingIndex >= 0)
            {
                collection.Move(existingIndex, targetIndex);
            }
            else
            {
                collection.Insert(targetIndex, desiredItem);
            }
        }

        while (collection.Count > desiredItems.Count)
        {
            collection.RemoveAt(collection.Count - 1);
        }
    }

    private static bool ContainsReference<T>(IReadOnlyList<T> items, T item)
        where T : class
    {
        return FindReferenceIndex(items, item, 0) >= 0;
    }

    private static int FindReferenceIndex<T>(IReadOnlyList<T> items, T item, int startIndex)
        where T : class
    {
        for (int index = startIndex; index < items.Count; index++)
        {
            if (ReferenceEquals(items[index], item))
            {
                return index;
            }
        }

        return -1;
    }
}
