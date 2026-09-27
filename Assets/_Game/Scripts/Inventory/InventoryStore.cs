using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Voron.Inventory
{
    public sealed class InventoryStack<T> where T : class, IInventoryDefinition
    {
        public T Definition { get; }
        public int Quantity { get; internal set; }

        internal InventoryStack(T definition, int quantity)
        {
            Definition = definition;
            Quantity = quantity;
        }
    }

    public sealed class InventoryStore<T> where T : class, IInventoryDefinition
    {
        private readonly List<InventoryStack<T>> stacks = new List<InventoryStack<T>>();
        private readonly ReadOnlyCollection<InventoryStack<T>> view;

        public int Capacity { get; }
        public IReadOnlyList<InventoryStack<T>> Stacks => view;
        public event Action Changed;

        public InventoryStore(int capacity)
        {
            if (capacity < 1 || capacity > 256)
                throw new ArgumentOutOfRangeException(nameof(capacity), "Capacity must be between 1 and 256.");

            Capacity = capacity;
            view = stacks.AsReadOnly();
        }

        public int Count(string id)
        {
            int total = 0;
            foreach (var stack in stacks)
                if (string.Equals(stack.Definition.Id, id, StringComparison.Ordinal))
                    total += stack.Quantity;
            return total;
        }

        public bool TryAdd(T definition, int quantity = 1)
        {
            if (definition == null || string.IsNullOrWhiteSpace(definition.Id) ||
                definition.StackLimit < 1 || quantity < 1)
                return false;

            int limit = definition.StackLimit;
            long free = (long)(Capacity - stacks.Count) * limit;
            foreach (var stack in stacks)
            {
                if (!string.Equals(stack.Definition.Id, definition.Id, StringComparison.Ordinal))
                    continue;

                // A duplicate ID must not merge two different content definitions.
                if (!ReferenceEquals(stack.Definition, definition) || stack.Quantity > limit)
                    return false;
                free += limit - stack.Quantity;
            }

            if (free < quantity || (long)Count(definition.Id) + quantity > int.MaxValue)
                return false;

            int remaining = quantity;
            foreach (var stack in stacks)
            {
                if (!ReferenceEquals(stack.Definition, definition))
                    continue;
                int added = Math.Min(limit - stack.Quantity, remaining);
                stack.Quantity += added;
                remaining -= added;
                if (remaining == 0)
                    break;
            }

            while (remaining > 0)
            {
                int added = Math.Min(limit, remaining);
                stacks.Add(new InventoryStack<T>(definition, added));
                remaining -= added;
            }

            Changed?.Invoke();
            return true;
        }

        public bool TryRemove(string id, int quantity = 1)
        {
            if (string.IsNullOrWhiteSpace(id) || quantity < 1 || Count(id) < quantity)
                return false;

            int remaining = quantity;
            for (int i = stacks.Count - 1; i >= 0 && remaining > 0; i--)
            {
                var stack = stacks[i];
                if (!string.Equals(stack.Definition.Id, id, StringComparison.Ordinal))
                    continue;

                int removed = Math.Min(remaining, stack.Quantity);
                stack.Quantity -= removed;
                remaining -= removed;
                if (stack.Quantity == 0)
                    stacks.RemoveAt(i);
            }

            Changed?.Invoke();
            return true;
        }
    }
}
