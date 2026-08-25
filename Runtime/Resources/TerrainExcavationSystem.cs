using System;
using System.Collections.Generic;
using UnityEngine;
using SDFTerrain.Materials;
using SDFTerrain.Terrain;

namespace SDFTerrain.Resources
{
    /// <summary>
    /// Result of a full excavation operation: includes material volumes removed,
    /// resources gained, and whether the operation succeeded.
    /// </summary>
    public readonly struct ExcavationResult
    {
        /// <summary>Material volumes removed by the excavation.</summary>
        public readonly MaterialVolumeResult MaterialVolumes;

        /// <summary>Resources added to inventory (resourceId -> quantity).</summary>
        public readonly Dictionary<string, int> ResourcesGained;

        /// <summary>True if the excavation actually removed terrain.</summary>
        public readonly bool WasApplied;

        internal ExcavationResult(MaterialVolumeResult materialVolumes, Dictionary<string, int> resourcesGained, bool wasApplied)
        {
            MaterialVolumes = materialVolumes;
            ResourcesGained = resourcesGained;
            WasApplied = wasApplied;
        }
    }

    /// <summary>
    /// Result of a placement operation: includes the material placed, SDF edit applied,
    /// and resources consumed from inventory.
    /// </summary>
    public readonly struct PlacementResult
    {
        /// <summary>The material that was placed.</summary>
        public readonly MaterialId MaterialPlaced;

        /// <summary>Resources consumed from inventory (resourceId -> quantity consumed).</summary>
        public readonly Dictionary<string, int> ResourcesConsumed;

        /// <summary>True if the placement actually added terrain.</summary>
        public readonly bool Succeeded;

        internal PlacementResult(MaterialId materialPlaced, Dictionary<string, int> resourcesConsumed, bool succeeded)
        {
            MaterialPlaced = materialPlaced;
            ResourcesConsumed = resourcesConsumed;
            Succeeded = succeeded;
        }
    }

    /// <summary>
    /// Orchestrates the full excavation and placement pipeline:
    ///
    /// Excavation: Terrain Removal -> Material Volumes -> Resource Yield -> Inventory
    /// Placement: Inventory Check -> Consume Resources -> SDF Addition + Material Addition
    ///
    /// This system does NOT touch rendering. It operates on the SDF, material layer, and inventory.
    /// </summary>
    public class TerrainExcavationSystem
    {
        private MaterialLayer _materialLayer;
        private TerrainField _field;
        private Inventory _inventory;
        private readonly ResourceYieldTable _yieldTable;
        private readonly MaterialDatabase _database;

        /// <summary>Grid resolution for material sampling during excavation.</summary>
        public int SampleResolution { get; set; } = 8;

        public TerrainExcavationSystem(
            TerrainField field,
            MaterialLayer materialLayer,
            Inventory inventory,
            ResourceYieldTable yieldTable,
            MaterialDatabase database)
        {
            if (field == null)
                throw new ArgumentNullException(nameof(field));
            if (materialLayer == null)
                throw new ArgumentNullException(nameof(materialLayer));
            if (inventory == null)
                throw new ArgumentNullException(nameof(inventory));
            if (yieldTable == null)
                throw new ArgumentNullException(nameof(yieldTable));
            if (database == null)
                throw new ArgumentNullException(nameof(database));

            _field = field;
            _materialLayer = materialLayer;
            _inventory = inventory;
            _yieldTable = yieldTable;
            _database = database;
        }

        /// <summary>
        /// Excavates terrain at <paramref name="localPosition"/> with the given radius,
        /// producing material volumes and converting them to inventory resources.
        ///
        /// Pipeline: Terrain Removal -> Material Volumes -> Resource Yield -> Inventory
        /// </summary>
        public ExcavationResult Excavate(Vector2 localPosition, float radius, int chunkIndex)
        {
            if (radius <= 0f)
                return new ExcavationResult(new MaterialVolumeResult(), new Dictionary<string, int>(), false);

            // Calculate material composition BEFORE applying the removal edit.
            MaterialVolumeResult volumes = ExcavationCalculator.CalculateRemoval(
                _materialLayer,
                _field,
                localPosition,
                radius,
                chunkIndex,
                SampleResolution);

            // Convert volumes to resources.
            Dictionary<string, int> resources = _yieldTable.Convert(volumes);

            // Apply resources to inventory.
            foreach (var kvp in resources)
            {
                _inventory.Add(kvp.Key, kvp.Value);
            }

            // Apply the terrain removal edit.
            TerrainEdit edit = new TerrainEdit(localPosition, radius, isAdditive: true);
            _field.ApplyEdit(edit);

            // Log excavation result.
            if (volumes.TotalVolume > 0f)
            {
                Debug.Log($"[Excavation] Removed {volumes.TotalVolume:F2} m³ at {localPosition}. Resources: {FormatResources(resources)}");
            }

            return new ExcavationResult(volumes, resources, volumes.TotalVolume > 0f);
        }

        /// <summary>
        /// Places terrain at <paramref name="localPosition"/> with the given material and radius.
        /// Consumes the required resources from inventory first. If insufficient resources,
        /// the placement fails and no resources are consumed.
        ///
        /// A successful placement creates: SDF addition + Material addition.
        /// </summary>
        public PlacementResult Place(Vector2 localPosition, float radius, MaterialId materialId, string resourceId)
        {
            if (!materialId.IsValid)
                throw new ArgumentException("MaterialId must be valid.", nameof(materialId));
            if (string.IsNullOrEmpty(resourceId))
                throw new ArgumentNullException(nameof(resourceId));

            // Calculate how many resources are needed for this placement.
            float area = UnityEngine.Mathf.PI * radius * radius;

            // Look up the yield rule for this material to determine the resource cost.
            float yieldRate = _yieldTable.GetYieldRate(materialId);
            // Cost is inverse of yield: if 1m³ produces Y items, placing costs Y*area items.
            int itemsNeeded = (int)System.Math.Ceiling((double)(area * yieldRate));

            // Check inventory first.
            if (!_inventory.HasAtLeast(resourceId, itemsNeeded))
            {
                int current = _inventory.GetQuantity(resourceId);
                Debug.Log($"[Placement] Failed: need {itemsNeeded} {resourceId}, have {current}.");
                return new PlacementResult(materialId, new Dictionary<string, int>(), false);
            }

            // Consume resources.
            _inventory.Remove(resourceId, itemsNeeded);
            var consumed = new Dictionary<string, int> { { resourceId, itemsNeeded } };

            // Apply terrain addition edit.
            TerrainEdit edit = new TerrainEdit(localPosition, radius, isAdditive: false);
            _field.ApplyEdit(edit);

            // Apply material override.
            _materialLayer.ApplyEdit(localPosition, radius, materialId);

            string matName = _database.HasMaterial(materialId) ? _database.GetName(materialId) : materialId.ToString();
            Debug.Log($"[Placement] Placed {matName} at {localPosition} (r={radius:F1}). Consumed {itemsNeeded} {resourceId}.");

            return new PlacementResult(materialId, consumed, true);
        }

        /// <summary>Returns the material layer this system operates on.</summary>
        public MaterialLayer GetMaterialLayer() => _materialLayer;

        /// <summary>Returns the inventory this system operates on.</summary>
        public Inventory GetInventory() => _inventory;

        /// <summary>
        /// Replaces the internal references with new systems. Used by
        /// <see cref="WorldPersistence"/> after loading a save file to wire the fresh
        /// field, material layer, and inventory back into the excavation pipeline.
        /// </summary>
        public void Rewire(TerrainField field, MaterialLayer materialLayer, Inventory inventory)
        {
            if (field == null)
                throw new ArgumentNullException(nameof(field));
            if (materialLayer == null)
                throw new ArgumentNullException(nameof(materialLayer));
            if (inventory == null)
                throw new ArgumentNullException(nameof(inventory));

            _field = field;
            _materialLayer = materialLayer;
            _inventory = inventory;
        }

        private static string FormatResources(Dictionary<string, int> resources)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            bool first = true;
            foreach (var kvp in resources)
            {
                if (!first) sb.Append(", ");
                sb.Append($"{kvp.Key}:{kvp.Value}");
                first = false;
            }
            return sb.ToString();
        }

    }
}
