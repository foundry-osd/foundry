// Copyright (c) Foundry Project contributors.
// Licensed under the MIT License.
// See the LICENSE file in the project root for more information.

using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Foundry.Utilities.Collections;

namespace Foundry.Utilities.Tests.Collections;

public sealed class ObservableCollectionExtensionsTests
{
    [Fact]
    public void SynchronizeReferences_WhenNarrowing_RemovesExcludedItemsWithoutMovingRetainedItems()
    {
        object removed = new();
        object retainedFirst = new();
        object retainedSecond = new();
        ObservableCollection<object> items = [removed, retainedFirst, retainedSecond];
        List<NotifyCollectionChangedAction> actions = [];
        items.CollectionChanged += (_, e) => actions.Add(e.Action);

        items.SynchronizeReferences([retainedFirst, retainedSecond]);

        Assert.Collection(
            items,
            item => Assert.Same(retainedFirst, item),
            item => Assert.Same(retainedSecond, item));
        Assert.Equal([NotifyCollectionChangedAction.Remove], actions);
    }

    [Fact]
    public void SynchronizeReferences_PreservesRetainedItemsWithoutResettingCollection()
    {
        object removed = new();
        object retainedFirst = new();
        object retainedSecond = new();
        object added = new();
        ObservableCollection<object> items = [removed, retainedFirst, retainedSecond];
        List<NotifyCollectionChangedAction> actions = [];
        items.CollectionChanged += (_, e) => actions.Add(e.Action);

        items.SynchronizeReferences([retainedSecond, added, retainedFirst]);

        Assert.Collection(
            items,
            item => Assert.Same(retainedSecond, item),
            item => Assert.Same(added, item),
            item => Assert.Same(retainedFirst, item));
        Assert.DoesNotContain(NotifyCollectionChangedAction.Reset, actions);
    }

    [Fact]
    public void SynchronizeReferences_WhenItemsAlreadyMatch_DoesNotRaiseCollectionChanged()
    {
        object first = new();
        object second = new();
        ObservableCollection<object> items = [first, second];
        int changeCount = 0;
        items.CollectionChanged += (_, _) => changeCount++;

        items.SynchronizeReferences([first, second]);

        Assert.Equal(0, changeCount);
    }
}
