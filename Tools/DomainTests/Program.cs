using Voron.Inventory;

internal static class Program
{
    private sealed class Definition : IInventoryDefinition
    {
        public string Id { get; }
        public int StackLimit { get; }
        public Definition(string id, int stackLimit = 1) { Id = id; StackLimit = stackLimit; }
    }

    private static int passed;

    private static void Main()
    {
        Run("empty inventory", () =>
        {
            var store = new InventoryStore<Definition>(16);
            Equal(0, store.Stacks.Count);
            Equal(0, store.Count("badge"));
            Equal(16, store.Capacity);
        });
        Run("invalid capacities reject", () =>
        {
            Throws<ArgumentOutOfRangeException>(() => new InventoryStore<Definition>(0));
            Throws<ArgumentOutOfRangeException>(() => new InventoryStore<Definition>(257));
        });
        Run("non-stackable occupies individual slots", () =>
        {
            var store = new InventoryStore<Definition>(3);
            var key = new Definition("key");
            Check(store.TryAdd(key, 3));
            Equal(3, store.Stacks.Count);
            Check(store.Stacks.All(stack => stack.Quantity == 1));
        });
        Run("stack filling before new slots", () =>
        {
            var store = new InventoryStore<Definition>(3);
            var film = new Definition("film", 5);
            Check(store.TryAdd(film, 3));
            Check(store.TryAdd(film, 9));
            Sequence(new[] { 5, 5, 2 }, store.Stacks.Select(s => s.Quantity));
        });
        Run("overflow is atomic", () =>
        {
            var store = new InventoryStore<Definition>(2);
            var film = new Definition("film", 3);
            Check(store.TryAdd(film, 5));
            string before = Snapshot(store);
            Check(!store.TryAdd(film, 2));
            Equal(before, Snapshot(store));
        });
        Run("full slots can still fill partial stack", () =>
        {
            var store = new InventoryStore<Definition>(1);
            var film = new Definition("film", 3);
            Check(store.TryAdd(film, 2));
            Check(store.TryAdd(film));
            Check(!store.TryAdd(film));
            Equal(3, store.Count(film.Id));
        });
        Run("removal across stacks reclaims slots", () =>
        {
            var store = new InventoryStore<Definition>(3);
            var film = new Definition("film", 3);
            Check(store.TryAdd(film, 8));
            Check(store.TryRemove("film", 4));
            Sequence(new[] { 3, 1 }, store.Stacks.Select(s => s.Quantity));
            Check(store.TryAdd(new Definition("key")));
        });
        Run("insufficient removal is atomic", () =>
        {
            var store = new InventoryStore<Definition>(2);
            Check(store.TryAdd(new Definition("key")));
            string before = Snapshot(store);
            Check(!store.TryRemove("key", 2));
            Equal(before, Snapshot(store));
        });
        Run("invalid requests preserve state", () =>
        {
            var store = new InventoryStore<Definition>(2);
            var key = new Definition("key");
            Check(!store.TryAdd(null));
            Check(!store.TryAdd(new Definition(" ")));
            Check(!store.TryAdd(new Definition("bad", 0)));
            Check(!store.TryAdd(key, 0));
            Check(!store.TryAdd(key, -1));
            Check(!store.TryRemove(null));
            Check(!store.TryRemove("key", -1));
            Equal(0, store.Stacks.Count);
        });
        Run("one event per successful transaction", () =>
        {
            var store = new InventoryStore<Definition>(2);
            var film = new Definition("film", 3);
            int events = 0;
            store.Changed += () => events++;
            Check(store.TryAdd(film, 5));
            Check(!store.TryAdd(film, 2));
            Check(!store.TryRemove("missing"));
            Check(store.TryRemove("film", 4));
            Equal(2, events);
        });
        Run("events observe committed state", () =>
        {
            var store = new InventoryStore<Definition>(2);
            store.Changed += () => Equal(2, store.Count("key"));
            Check(store.TryAdd(new Definition("key"), 2));
        });
        Run("distinct definitions with same ID rejected", () =>
        {
            var store = new InventoryStore<Definition>(3);
            Check(store.TryAdd(new Definition("film", 3), 2));
            Check(!store.TryAdd(new Definition("film", 3)));
            Check(!store.TryAdd(new Definition("film", 9)));
            Equal(2, store.Count("film"));
        });
        Run("ID matching is ordinal", () =>
        {
            var store = new InventoryStore<Definition>(2);
            Check(store.TryAdd(new Definition("KEY")));
            Check(store.TryAdd(new Definition("key")));
            Equal(1, store.Count("KEY"));
            Equal(1, store.Count("key"));
        });
        Run("integer limits do not overflow", () =>
        {
            var store = new InventoryStore<Definition>(256);
            var film = new Definition("film", int.MaxValue);
            Check(store.TryAdd(film, int.MaxValue));
            Check(!store.TryAdd(film));
            Equal(int.MaxValue, store.Count("film"));
            Check(store.TryRemove("film", int.MaxValue));
            Equal(0, store.Stacks.Count);
        });
        Run("items of other IDs are untouched", () =>
        {
            var store = new InventoryStore<Definition>(4);
            Check(store.TryAdd(new Definition("film", 3), 5));
            Check(store.TryAdd(new Definition("key")));
            Check(store.TryRemove("film", 5));
            Equal(1, store.Stacks.Count);
            Equal("key", store.Stacks[0].Definition.Id);
        });
        Run("read-only stack collection", () =>
        {
            var store = new InventoryStore<Definition>(2);
            Check(store.TryAdd(new Definition("key")));
            var collection = (IList<InventoryStack<Definition>>)store.Stacks;
            Throws<NotSupportedException>(() => collection.Clear());
        });
        Run("seeded 10000-transaction invariant exercise", RandomTransactions);
        Console.WriteLine($"PASS: {passed} domain tests; native Unity checks are separate.");
    }

    private static void RandomTransactions()
    {
        var random = new Random(7041998);
        var definitions = new[] { new Definition("key"), new Definition("film", 4), new Definition("note", 2) };
        var store = new InventoryStore<Definition>(8);
        var counts = definitions.ToDictionary(d => d.Id, _ => 0);
        int events = 0;
        int successes = 0;
        store.Changed += () => events++;
        for (int attempt = 0; attempt < 10000; attempt++)
        {
            var definition = definitions[random.Next(definitions.Length)];
            int quantity = random.Next(1, 12);
            bool add = random.Next(2) == 0;
            string before = Snapshot(store);
            int free = (store.Capacity - store.Stacks.Count) * definition.StackLimit +
                store.Stacks.Where(s => s.Definition == definition).Sum(s => definition.StackLimit - s.Quantity);
            bool expected = add ? free >= quantity : counts[definition.Id] >= quantity;
            bool actual = add ? store.TryAdd(definition, quantity) : store.TryRemove(definition.Id, quantity);
            Equal(expected, actual);
            if (actual)
            {
                counts[definition.Id] += add ? quantity : -quantity;
                successes++;
            }
            else
                Equal(before, Snapshot(store));
            Equal(successes, events);
            Check(store.Stacks.Count <= store.Capacity);
            Check(store.Stacks.All(s => s.Quantity >= 1 && s.Quantity <= s.Definition.StackLimit));
            foreach (var item in definitions)
                Equal(counts[item.Id], store.Count(item.Id));
        }
    }

    private static string Snapshot(InventoryStore<Definition> store) =>
        string.Join("|", store.Stacks.Select(stack => $"{stack.Definition.Id}:{stack.Quantity}"));

    private static void Run(string name, Action test)
    {
        test();
        passed++;
        Console.WriteLine($"PASS {name}");
    }

    private static void Check(bool condition)
    {
        if (!condition) throw new InvalidOperationException("Assertion failed.");
    }

    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"Expected {expected}; got {actual}.");
    }

    private static void Sequence(IEnumerable<int> expected, IEnumerable<int> actual) => Check(expected.SequenceEqual(actual));

    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }
}
