using System;
using UnityEngine;
using SDFTerrain.Materials;
using SDFTerrain.Terrain;

namespace SDFTerrain.Resources
{
    /// <summary>
    /// Calculates the material composition of a terrain removal operation.
    /// Samples the material at multiple points within the brush region to produce
    /// a <see cref="MaterialVolumeResult"/> that maps each material to its removed volume.
    /// </summary>
    public static class ExcavationCalculator
    {
        /// <summary>
        /// Computes the material composition of a circular excavation region.
        /// Uses a grid-based sampling approach to integrate the material at each point.
        /// </summary>
        /// <param name="materialLayer">The material state layer providing per-position material queries.</param>
        /// <param name="field">The terrain SDF (before the removal edit is applied).</param>
        /// <param name="center">Planet-local center of the excavation.</param>
        /// <param name="radius">Radius of the excavation circle.</param>
        /// <param name="chunkIndex">Chunk index for optimized lookups.</param>
        /// <param name="sampleResolution">Number of samples along each axis within the circle.</param>
        /// <returns>Material volumes map with the volume of each material removed.</returns>
        public static MaterialVolumeResult CalculateRemoval(
            MaterialLayer materialLayer,
            TerrainField field,
            Vector2 center,
            float radius,
            int chunkIndex,
            int sampleResolution = 8)
        {
            if (materialLayer == null)
                throw new ArgumentNullException(nameof(materialLayer));
            if (field == null)
                throw new ArgumentNullException(nameof(field));

            var result = new MaterialVolumeResult();

            if (radius <= 0f)
                return result;

            if (sampleResolution < 2)
                sampleResolution = 2;

            float stepX = (2f * radius) / sampleResolution;
            float stepY = (2f * radius) / sampleResolution;
            float areaPerSample = stepX * stepY;

            float minX = center.x - radius;
            float maxX = center.x + radius;
            float minY = center.y - radius;
            float maxY = center.y + radius;

            for (float y = minY; y <= maxY; y += stepY)
            {
                for (float x = minX; x <= maxX; x += stepX)
                {
                    Vector2 pos = new Vector2(x, y);

                    // Only sample points inside the circle
                    float dx = pos.x - center.x;
                    float dy = pos.y - center.y;
                    if (dx * dx + dy * dy > radius * radius)
                        continue;

                    // Only sample solid terrain
                    MaterialSample sample = materialLayer.Sample(field, pos, chunkIndex);
                    if (sample.IsSolid && sample.MaterialId.IsValid)
                    {
                        result.Add(sample.MaterialId, areaPerSample);
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Computes the material composition of terrain being added.
        /// When adding terrain with a known material, the entire area is that material.
        /// </summary>
        public static MaterialVolumeResult CalculateAddition(Vector2 center, float radius, MaterialId materialId)
        {
            if (!materialId.IsValid)
                return new MaterialVolumeResult();

            var result = new MaterialVolumeResult();
            float area = Mathf.PI * radius * radius;
            result.Add(materialId, area);
            return result;
        }
    }
}
