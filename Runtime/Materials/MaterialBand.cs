using System;

namespace SDFTerrain.Materials
{
    /// <summary>
    /// Maps a depth range below the terrain surface to a material ID. Depth is measured in world
    /// units from the surface inward (0 = surface, positive = below). When sampling, the first
    /// band whose range contains the query depth is selected.
    /// </summary>
    public readonly struct MaterialBand
    {
        /// <summary>Material ID to assign within this band (e.g. "dirt", "stone").</summary>
        public readonly string MaterialId;

        /// <summary>Minimum depth (inclusive). 0 means the surface.</summary>
        public readonly float MinDepth;

        /// <summary>Maximum depth (exclusive). Use <see cref="float.PositiveInfinity"/> for no upper limit.</summary>
        public readonly float MaxDepth;

        public MaterialBand(string materialId, float minDepth, float maxDepth)
        {
            if (string.IsNullOrEmpty(materialId))
            {
                throw new ArgumentNullException(nameof(materialId), "Material ID must not be null or empty.");
            }

            if (minDepth < 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(minDepth), minDepth, "Min depth must be non-negative.");
            }

            if (maxDepth < 0f || maxDepth < minDepth)
            {
                throw new ArgumentOutOfRangeException(nameof(maxDepth), maxDepth,
                    "Max depth must be positive and greater than or equal to min depth.");
            }

            MaterialId = materialId;
            MinDepth = minDepth;
            MaxDepth = maxDepth;
        }

        /// <summary>
        /// Returns true if <paramref name="depth"/> falls within this band's range
        /// ([MinDepth, MaxDepth)).
        /// </summary>
        public bool ContainsDepth(float depth)
        {
            return depth >= MinDepth && depth < MaxDepth;
        }
    }
}
