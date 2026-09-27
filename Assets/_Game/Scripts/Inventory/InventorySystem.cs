using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

namespace Voron.Inventory
{
    [DisallowMultipleComponent]
    public sealed class InventorySystem : MonoBehaviour
    {
        [SerializeField, Range(1, 64)] private int capacity = 16;
        private InventoryStore<ItemData> store;
        private readonly List<InventoryItem> items = new List<InventoryItem>();
        private ReadOnlyCollection<InventoryItem> view;

        public int Capacity => capacity;
        public IReadOnlyList<InventoryItem> Items
        {
            get
            {
                EnsureStore();
                return view;
            }
        }

        public event Action Changed;

        private void Awake() => EnsureStore();

        private void OnDestroy()
        {
            if (store != null)
                store.Changed -= SyncItems;
        }

        public void Configure(int capacity)
        {
            if (capacity < 1 || capacity > 64)
                throw new ArgumentOutOfRangeException(nameof(capacity));
            if (store != null && store.Stacks.Count != 0)
                throw new InvalidOperationException("Cannot reconfigure an occupied inventory.");

            if (store != null)
                store.Changed -= SyncItems;
            this.capacity = capacity;
            store = null;
            EnsureStore();
            SyncItems();
        }

        public bool TryAdd(ItemData data, int quantity = 1)
        {
            EnsureStore();
            return data != null && store.TryAdd(data, quantity);
        }

        public bool TryRemove(string id, int quantity = 1)
        {
            EnsureStore();
            return store.TryRemove(id, quantity);
        }

        public int Count(string id)
        {
            EnsureStore();
            return store.Count(id);
        }

        private void EnsureStore()
        {
            if (store != null)
                return;
            capacity = Mathf.Clamp(capacity, 1, 64);
            view = items.AsReadOnly();
            store = new InventoryStore<ItemData>(capacity);
            store.Changed += SyncItems;
        }

        private void SyncItems()
        {
            items.Clear();
            foreach (var stack in store.Stacks)
                items.Add(new InventoryItem(stack.Definition, stack.Quantity));
            Changed?.Invoke();
        }
    }
}
