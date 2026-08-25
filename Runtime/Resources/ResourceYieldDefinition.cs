using System;
using System.Collections.Generic;
using SDFTerrain.Materials;

namespace SDFTerrain.Resources
{
    /// <summary>
    /// Defines how much of a resource is yielded when a certain volume of a material is
    /// excavated. Decouples terrain materials from inventory items.
    ///
    /// Example: 1 m² of Stone yields 100 "stone" resource items.
    /// </summary>
    public readonly struct ResourceYieldDefinition : System.IEquatable<ResourceYieldDefinition>
    {
        /// <summary>The material being excavated.</summary>
        public readonly MaterialId MaterialId;

        /// <summary>The resource ID produced (e.g., "stone", "iron_ore").</summary>
        public readonly string ResourceId;

        /// <summary>How many resource items are produced per unit area of material (2D).</summary>
        public readonly float YieldPerUnitArea;

        public ResourceYieldDefinition(MaterialId materialId, string resourceId, float yieldPerUnitArea)
        {
            if (!materialId.IsValid)
                throw new ArgumentException("MaterialId must be valid.", nameof(materialId));
            if (string.IsNullOrEmpty(resourceId))
                throw new ArgumentNullException(nameof(resourceId));
            if (yieldPerUnitArea < 0f)
                yieldPerUnitArea = 0f;

            MaterialId = materialId;
            ResourceId = resourceId;
            YieldPerUnitArea = yieldPerUnitArea;
        }

        /// <summary>
        /// Computes the integer quantity of resources produced from excavating
        /// <paramref name="area"/> of this material.
        /// </summary>
        public int ComputeQuantity(float area)
        {
            if (area <= 0f)
                return 0;

            return (int)System.Math.Floor(area * YieldPerUnitArea);
        }

        public bool Equals(ResourceYieldDefinition other)
            => MaterialId == other.MaterialId
                && ResourceId == other.ResourceId
                && YieldPerUnitArea == other.YieldPerUnitArea;

        public override bool Equals(object obj) => obj is ResourceYieldDefinition other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + MaterialId.GetHashCode();
                hash = hash * 31 + ResourceId.GetHashCode();
                hash = hash * 31 + YieldPerUnitArea.GetHashCode();
                return hash;
            }
        }
    }

    /// <summary>
    /// Collection of yield rules mapping materials to resources. Provides the conversion
    /// logic between excavation results and inventory additions.
    /// </summary>
    public class ResourceYieldTable
    {
        private readonly Dictionary<int, ResourceYieldDefinition> _yields = new Dictionary<int, ResourceYieldDefinition>();

        /// <summary>Adds or replaces a yield rule for a material.</summary>
        public void AddRule(ResourceYieldDefinition rule)
        {
            _yields[rule.MaterialId.Value] = rule;
        }

        /// <summary>
        /// Returns the yield rate (items per unit area) for a material, or 100f as default.
        /// </summary>
        public float GetYieldRate(MaterialId materialId)
        {
            if (_yields.TryGetValue(materialId.Value, out ResourceYieldDefinition rule))
                return rule.YieldPerUnitArea;
            return 100f;
        }

        /// <summary>
        /// Converts a <see cref="MaterialVolumeResult"/> into resource quantities.
        /// Returns a dictionary mapping resource IDs to quantities.
        /// </summary>
        public Dictionary<string, int> Convert(MaterialVolumeResult volumes)
        {
            var result = new Dictionary<string, int>();

            volumes.ForEach((materialId, area) =>
            {
                if (_yields.TryGetValue(materialId.Value, out ResourceYieldDefinition rule))
                {
                    int quantity = rule.ComputeQuantity(area);
                    if (quantity > 0)
                    {
                        result[rule.ResourceId] = result.TryGetValue(rule.ResourceId, out int existing)
                            ? existing + quantity
                            : quantity;
                    }
                }
            });

            return result;
        }

        /// <summary>
        /// Creates a default yield table where 1 m² of material yields 100 resource items,
        /// and the resource ID matches the material's string name.
        /// </summary>
        public static ResourceYieldTable Default(MaterialDatabase database)
        {
            var table = new ResourceYieldTable();

            float yieldRate = 100f; // 100 items per unit area

            foreach (var material in database.AllMaterials)
            {
                MaterialId id = database.GetMaterialId(material.Id);
                if (id.IsValid && id != MaterialId.Air)
                {
                    table.AddRule(new ResourceYieldDefinition(id, material.Id, yieldRate));
                }
            }

            return table;
        }
    }
}
