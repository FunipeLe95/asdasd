using System;
using UnityEngine;

namespace Voron.Inventory
{
    [CreateAssetMenu(menuName = "Voron/Item", fileName = "Item")]
    public sealed class ItemData : ScriptableObject, IInventoryDefinition
    {
        [SerializeField] private string id;
        [SerializeField] private string displayName;
        [SerializeField, TextArea(2, 6)] private string description;
        [SerializeField] private Sprite icon;
        [SerializeField] private ItemType type;
        [SerializeField] private bool stackable;
        [SerializeField, Min(1)] private int maxStack = 1;
        [SerializeField] private GameObject worldPrefab;

        public string Id => id;
        public string DisplayName => displayName;
        public string Description => description;
        public Sprite Icon => icon;
        public ItemType Type => type;
        public bool Stackable => stackable;
        public int MaxStack => stackable ? Mathf.Max(1, maxStack) : 1;
        public int StackLimit => MaxStack;
        public GameObject WorldPrefab => worldPrefab;

        public void Configure(string id, string displayName, string description, ItemType type,
            bool stackable = false, int maxStack = 1, Sprite icon = null, GameObject worldPrefab = null)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException("An item requires a stable non-empty ID.", nameof(id));
            if (maxStack < 1)
                throw new ArgumentOutOfRangeException(nameof(maxStack));

            this.id = id.Trim();
            this.displayName = displayName;
            this.description = description;
            this.type = type;
            this.stackable = stackable;
            this.maxStack = stackable ? maxStack : 1;
            this.icon = icon;
            this.worldPrefab = worldPrefab;
        }

        private void OnValidate()
        {
            id = id?.Trim();
            maxStack = stackable ? Mathf.Max(1, maxStack) : 1;
        }
    }
}
