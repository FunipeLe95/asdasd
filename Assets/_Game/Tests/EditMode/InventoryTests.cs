using NUnit.Framework;
using UnityEngine;
using Voron.Inventory;

namespace Voron.Tests
{
    public sealed class InventoryTests
    {
        private GameObject owner;
        private InventorySystem inventory;
        private ItemData film;
        private ItemData key;

        [SetUp]
        public void SetUp()
        {
            owner = new GameObject("InventoryTest");
            inventory = owner.AddComponent<InventorySystem>();
            inventory.Configure(2);
            film = ScriptableObject.CreateInstance<ItemData>();
            film.Configure("film", "Film", "Case film.", ItemType.Consumable, true, 3);
            key = ScriptableObject.CreateInstance<ItemData>();
            key.Configure("key", "Key", "Apartment key.", ItemType.Key);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(owner);
            Object.DestroyImmediate(film);
            Object.DestroyImmediate(key);
        }

        [Test]
        public void NewInventoryIsEmpty() => Assert.That(inventory.Items, Is.Empty);

        [Test]
        public void StackLimitAndAtomicOverflowAreRespected()
        {
            Assert.That(inventory.TryAdd(film, 5), Is.True);
            Assert.That(inventory.TryAdd(film, 2), Is.False);
            Assert.That(inventory.Count(film.Id), Is.EqualTo(5));
            Assert.That(inventory.Items[0].Quantity, Is.EqualTo(3));
            Assert.That(inventory.Items[1].Quantity, Is.EqualTo(2));
        }

        [Test]
        public void FailureDoesNotFireChanged()
        {
            int changes = 0;
            inventory.Changed += () => changes++;
            Assert.That(inventory.TryAdd(key, 2), Is.True);
            Assert.That(inventory.TryAdd(key), Is.False);
            Assert.That(inventory.TryRemove(key.Id, 3), Is.False);
            Assert.That(changes, Is.EqualTo(1));
        }

        [Test]
        public void RemovedStacksFreeCapacity()
        {
            Assert.That(inventory.TryAdd(film, 5), Is.True);
            Assert.That(inventory.TryRemove(film.Id, 3), Is.True);
            Assert.That(inventory.TryAdd(key), Is.True);
            Assert.That(inventory.Count(film.Id), Is.EqualTo(2));
        }

        [Test]
        public void ExistingInventoryCannotBeSilentlyReconfigured()
        {
            Assert.That(inventory.TryAdd(key), Is.True);
            Assert.Throws<System.InvalidOperationException>(() => inventory.Configure(16));
            Assert.That(inventory.Count(key.Id), Is.EqualTo(1));
        }

        [Test]
        public void DestroyedDefinitionsAreRejected()
        {
            Object.DestroyImmediate(key);
            Assert.That(inventory.TryAdd(key), Is.False);
        }

        [Test]
        public void NonStackableDefinitionAlwaysUsesOnePerSlot()
        {
            key.Configure("key", "Key", "Apartment key.", ItemType.Key, false, 99);
            Assert.That(key.MaxStack, Is.EqualTo(1));
            Assert.That(inventory.TryAdd(key, 2), Is.True);
            Assert.That(inventory.Items.Count, Is.EqualTo(2));
        }
    }
}
