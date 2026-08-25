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
        public readonly MaterialVolumeResult MaterialVolumes;
        public readonly Dictionary<string, int> ResourcesGained;
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
        public readonly MaterialId MaterialPlaced;
        public readonly Dictionary<string, int> ResourcesConsumed;
        public readonly bool Succeeded;

        internal PlacementResult(MaterialId materialPlaced, Dictionary<string, int> resourcesConsumed, bool succeeded)
        {
            MaterialPlaced = materialPlaced;
            ResourcesConsumed = resourcesConsumed;
            Succeeded = succeeded;
        }
    }

    /// <summary>
    /// Orchestrates the full excavation and placement pipeline. Uses before/after solid
    /// area measurement (same method used by ChunkTerrainRenderer.ApplyBrush for
    /// BrushAreaDelta) for guaranteed 1:1 reversibility between mining and placing.
    /// </summary>
    public class TerrainExcavationSystem
    {
        private TerrainField _field;
        private MaterialLayer _materialLayer;
        private Inventory _inventory;
        private ResourceYieldTable _yieldTable;
        private MaterialDatabase _database;

        /// <summary>Grid resolution for area sampling. Must match between mine and place.</summary>
        public int SampleResolution { get; set; } = 16;

        public TerrainExcavationSystem(
            TerrainField field,
            MaterialLayer materialLayer,
            Inventory inventory,
            ResourceYieldTable yieldTable,
            MaterialDatabase database)
        {
            _field = field ?? throw new ArgumentNullException(nameof(field));
            _materialLayer = materialLayer ?? throw new ArgumentNullException(nameof(materialLayer));
            _inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
            _yieldTable = yieldTable ?? throw new ArgumentNullException(nameof(yieldTable));
            _database = database ?? throw new ArgumentNullException(nameof(database));
        }

        /// <summary>
        /// Excavates terrain: measures area before/after, samples materials at solid points,
        /// yields resources proportional to the actual area removed.
        /// </summary>
        public ExcavationResult Excavate(Vector2 localPosition, float radius, int chunkIndex)
        {
            if (radius <= 0f)
                return new ExcavationResult(new MaterialVolumeResult(), new Dictionary<string, int>(), false);

            // If the brush center is already in air, there's nothing to mine here.
            if (_field.Sample(localPosition) > 0f)
                return new ExcavationResult(new MaterialVolumeResult(), new Dictionary<string, int>(), false);

            // Measure solid area BEFORE the edit
            float solidAreaBefore = _field.GetSolidAreaInCircle(localPosition, radius, SampleResolution);
            if (solidAreaBefore <= 0f)
                return new ExcavationResult(new MaterialVolumeResult(), new Dictionary<string, int>(), false);

            // Sample materials at solid points for yield composition
            var volumes = SampleMaterialsInCircle(localPosition, radius, chunkIndex);

            // Apply the removal edit
            TerrainEdit edit = new TerrainEdit(localPosition, radius, isAdditive: true, clamped: true);
            _field.ApplyEdit(edit);

            // Measure solid area AFTER the edit — this is the ground truth
            float solidAreaAfter = _field.GetSolidAreaInCircle(localPosition, radius, SampleResolution);
            float areaRemoved = solidAreaBefore - solidAreaAfter;

            if (areaRemoved <= 0f)
                return new ExcavationResult(volumes, new Dictionary<string, int>(), false);

            // Scale volumes to match actual area removed (material samples may include
            // cells that didn't transition to air due to CSG cone shape)
            var scaledVolumes = ScaleByArea(volumes, areaRemoved);
            Dictionary<string, int> resources = _yieldTable.Convert(scaledVolumes);

            foreach (var kvp in resources)
                _inventory.Add(kvp.Key, kvp.Value);

            return new ExcavationResult(scaledVolumes, resources, true);
        }

        /// <summary>
        /// Places terrain: measures area before/after, consumes resources proportional
        /// to the actual area added. Uses the same area measurement as Excavate for 1:1.
        /// </summary>
        public PlacementResult Place(Vector2 localPosition, float radius, MaterialId materialId, string resourceId)
        {
            if (!materialId.IsValid)
                throw new ArgumentException("MaterialId must be valid.", nameof(materialId));
            if (string.IsNullOrEmpty(resourceId))
                throw new ArgumentNullException(nameof(resourceId));

            // Measure solid area BEFORE the edit
            float solidAreaBefore = _field.GetSolidAreaInCircle(localPosition, radius, SampleResolution);

            // If fully solid (no air to fill), skip
            float circleArea = Mathf.PI * radius * radius;
            if (solidAreaBefore >= circleArea)
                return new PlacementResult(materialId, new Dictionary<string, int>(), false);

            // Upper-bound cost check (worst case: all air cells become solid)
            float maxArea = circleArea - solidAreaBefore;
            float yieldRate = _yieldTable.GetYieldRate(materialId);
            int maxItemsNeeded = (int)System.Math.Floor(maxArea * yieldRate);

            if (!_inventory.HasAtLeast(resourceId, maxItemsNeeded))
            {
                int current = _inventory.GetQuantity(resourceId);
                Debug.Log($"[Placement] Failed: need {maxItemsNeeded} {resourceId}, have {current}.");
                return new PlacementResult(materialId, new Dictionary<string, int>(), false);
            }

            // Apply the addition edit
            TerrainEdit edit = new TerrainEdit(localPosition, radius, isAdditive: false, clamped: true);
            _field.ApplyEdit(edit);

            // Apply material override
            _materialLayer.ApplyEdit(localPosition, radius, materialId);

            // Measure solid area AFTER the edit — ground truth for cost
            float solidAreaAfter = _field.GetSolidAreaInCircle(localPosition, radius, SampleResolution);
            float areaAdded = solidAreaAfter - solidAreaBefore;

            // Charge for actual area added (may be less than upper bound due to CSG cone shape)
            int itemsNeeded = (int)System.Math.Floor(areaAdded * yieldRate);
            _inventory.Remove(resourceId, itemsNeeded);
            var consumed = new Dictionary<string, int> { { resourceId, itemsNeeded } };

            string matName = _database.HasMaterial(materialId) ? _database.GetName(materialId) : materialId.ToString();
            Debug.Log($"[Placement] Placed {matName} at {localPosition} (r={radius:F1}). Area +{areaAdded:F2}m². Consumed {itemsNeeded} {resourceId}.");

            return new PlacementResult(materialId, consumed, true);
        }

        /// <summary>Samples materials at solid grid points within the brush circle.</summary>
        private MaterialVolumeResult SampleMaterialsInCircle(Vector2 center, float radius, int chunkIndex)
        {
            var result = new MaterialVolumeResult();
            float step = (2f * radius) / SampleResolution;
            float areaPerSample = step * step;

            for (float y = -radius; y <= radius; y += step)
            {
                for (float x = -radius; x <= radius; x += step)
                {
                    Vector2 pos = center + new Vector2(x, y);
                    if (Vector2.Distance(pos, center) > radius)
                        continue;

                    MaterialSample sample = _materialLayer.Sample(_field, pos, chunkIndex);
                    if (sample.IsSolid && sample.MaterialId.IsValid)
                        result.Add(sample.MaterialId, areaPerSample);
                }
            }

            return result;
        }

        /// <summary>Scales a MaterialVolumeResult so its total equals the given area.</summary>
        private static MaterialVolumeResult ScaleByArea(MaterialVolumeResult source, float targetArea)
        {
            if (source.TotalVolume <= 0f)
                return source;

            float scale = targetArea / source.TotalVolume;
            var result = new MaterialVolumeResult();
            source.ForEach((materialId, volume) => result.Add(materialId, volume * scale));
            return result;
        }

        /// <summary>Returns the material layer this system operates on.</summary>
        public MaterialLayer GetMaterialLayer() => _materialLayer;

        /// <summary>Returns the inventory this system operates on.</summary>
        public Inventory GetInventory() => _inventory;

        /// <summary>
        /// Replaces the internal references with new systems. Used by
        /// <see cref="WorldPersistence"/> after loading a save file.
        /// </summary>
        public void Rewire(TerrainField field, MaterialLayer materialLayer, Inventory inventory)
        {
            _field = field ?? throw new ArgumentNullException(nameof(field));
            _materialLayer = materialLayer ?? throw new ArgumentNullException(nameof(materialLayer));
            _inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
        }
    }
}
