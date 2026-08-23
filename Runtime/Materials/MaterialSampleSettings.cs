using System;
using UnityEngine;

namespace SDFTerrain.Materials
{
    /// <summary>
    /// Configuration for depth-based material sampling. Defines a sequence of
    /// <see cref="MaterialBand"/> entries that map depth below the terrain surface to material
    /// IDs, a fallback material used when the query position is in air, and a seed for any
    /// deterministic variation. Immutable — construct a new instance rather than mutating.
    /// </summary>
    public readonly struct MaterialSampleSettings
    {
        /// <summary>Bands evaluated in order. The first band containing the query depth wins.</summary>
        public readonly MaterialBand[] Bands;

        /// <summary>Material ID returned when the position is in air (SDF &gt; 0).</summary>
        public readonly string FallbackMaterialId;

        /// <summary>Seed for deterministic variation (reserved for future ore/vein jitter).</summary>
        public readonly int Seed;

        public MaterialSampleSettings(MaterialBand[] bands, string fallbackMaterialId, int seed = 0)
        {
            if (bands == null || bands.Length == 0)
            {
                throw new ArgumentNullException(nameof(bands), "At least one material band is required.");
            }

            if (string.IsNullOrEmpty(fallbackMaterialId))
            {
                throw new ArgumentNullException(nameof(fallbackMaterialId), "Fallback material ID must not be null or empty.");
            }

            Bands = bands;
            FallbackMaterialId = fallbackMaterialId;
            Seed = seed;
        }

        /// <summary>
        /// Creates a typical surface-to-core profile: Dirt (0-5 units), Stone (5-20 units),
        /// Ice (20+ units). Fallback is air when outside the terrain.
        /// </summary>
        public static MaterialSampleSettings DirtStoneIce(float dirtDepth, float stoneDepth, string airMaterialId = "air", int seed = 0)
        {
            var bands = new MaterialBand[]
            {
                new MaterialBand("dirt", 0f, dirtDepth),
                new MaterialBand("stone", dirtDepth, stoneDepth),
                new MaterialBand("ice", stoneDepth, float.PositiveInfinity),
            };

            return new MaterialSampleSettings(bands, airMaterialId, seed);
        }
    }
}
