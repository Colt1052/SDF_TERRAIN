using System;
using System.Collections.Generic;

namespace SDFTerrain.Resources
{
    /// <summary>
    /// Represents a quantity of a single resource in an inventory slot.
    /// </summary>
    public readonly struct InventorySlot : System.IEquatable<InventorySlot>
    {
        public readonly string ResourceId;
        public readonly int Quantity;
        public readonly int MaxStack;

        public InventorySlot(string resourceId, int quantity, int maxStack = 9999)
        {
            if (string.IsNullOrEmpty(resourceId))
                throw new ArgumentNullException(nameof(resourceId));
            if (quantity < 0)
                quantity = 0;
            if (maxStack < 1)
                maxStack = 9999;

            ResourceId = resourceId;
            Quantity = quantity;
            MaxStack = maxStack;
        }

        public bool IsEmpty => Quantity <= 0;

        public bool Equals(InventorySlot other)
            => ResourceId == other.ResourceId && Quantity == other.Quantity && MaxStack == other.MaxStack;

        public override bool Equals(object obj) => obj is InventorySlot other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + ResourceId.GetHashCode();
                hash = hash * 31 + Quantity.GetHashCode();
                return hash;
            }
        }

        public static bool operator ==(InventorySlot left, InventorySlot right) => left.Equals(right);
        public static bool operator !=(InventorySlot left, InventorySlot right) => !left.Equals(right);
    }

    /// <summary>
    /// Inventory system independent of terrain. Holds resources as string-keyed slots with
    /// quantities and optional stack limits.
    /// </summary>
    public class Inventory
    {
        private readonly Dictionary<string, InventorySlot> _slots = new Dictionary<string, InventorySlot>();

        public int SlotCount => _slots.Count;

        public event Action<string, int> Changed;

        public int Add(string resourceId, int quantity)
        {
            if (string.IsNullOrEmpty(resourceId))
                throw new ArgumentNullException(nameof(resourceId));
            if (quantity <= 0)
                return 0;

            if (_slots.TryGetValue(resourceId, out InventorySlot slot))
            {
                int space = slot.MaxStack - slot.Quantity;
                int added = System.Math.Min(quantity, space);

                if (added <= 0)
                    return 0;

                _slots[resourceId] = new InventorySlot(resourceId, slot.Quantity + added, slot.MaxStack);
                Changed?.Invoke(resourceId, _slots[resourceId].Quantity);
                return added;
            }

            _slots[resourceId] = new InventorySlot(resourceId, quantity);
            Changed?.Invoke(resourceId, quantity);
            return quantity;
        }

        public int Remove(string resourceId, int quantity)
        {
            if (string.IsNullOrEmpty(resourceId))
                throw new ArgumentNullException(nameof(resourceId));
            if (quantity <= 0)
                return 0;

            if (!_slots.TryGetValue(resourceId, out InventorySlot slot))
                return 0;

            int actual = System.Math.Min(quantity, slot.Quantity);
            if (actual <= 0)
                return 0;

            int remaining = slot.Quantity - actual;
            if (remaining <= 0)
            {
                _slots.Remove(resourceId);
            }
            else
            {
                _slots[resourceId] = new InventorySlot(resourceId, remaining, slot.MaxStack);
            }

            Changed?.Invoke(resourceId, remaining);
            return actual;
        }

        public int GetQuantity(string resourceId)
        {
            return _slots.TryGetValue(resourceId, out InventorySlot slot) ? slot.Quantity : 0;
        }

        public bool HasAtLeast(string resourceId, int quantity)
        {
            return GetQuantity(resourceId) >= quantity;
        }

        public void ForEach(System.Action<InventorySlot> action)
        {
            if (action == null)
                return;

            foreach (var kvp in _slots)
            {
                if (kvp.Value.Quantity > 0)
                {
                    action(kvp.Value);
                }
            }
        }

        public void Clear()
        {
            _slots.Clear();
        }

        public Dictionary<string, int> ToDictionary()
        {
            var dict = new Dictionary<string, int>();
            foreach (var kvp in _slots)
            {
                if (kvp.Value.Quantity > 0)
                {
                    dict[kvp.Value.ResourceId] = kvp.Value.Quantity;
                }
            }
            return dict;
        }
    }
}
