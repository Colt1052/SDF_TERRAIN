using System;

namespace SDFTerrain.Materials
{
    /// <summary>
    /// Defines one geological stratum in a <see cref="GeologicalProfile"/>.
    /// Layers are evaluated surface-to-core; the first layer whose (perturbed) start depth
    /// is less than or equal to the query depth is selected.
    /// </summary>
    public readonly struct GeologicalLayer
    {
        /// <summary>Material ID to assign within this layer (e.g., "dirt", "stone").</summary>
        public readonly string MaterialId;

        /// <summary>Depth below surface (world units) where this layer nominally begins.</summary>
        public readonly float StartDepth;

        /// <summary>
        /// Maximum perturbation of this layer's boundary due to noise (world units).
        /// The actual boundary at a given position is <c>StartDepth + NoiseAmplitude * noise(pos)</c>.
        /// </summary>
        public readonly float NoiseAmplitude;

        /// <summary>
        /// Temperature (Kelvin) at which this material transitions to its molten form.
        /// If the local temperature exceeds this value, the generator uses the molten
        /// material ID (<c>MaterialId + "_molten"</c>). A value of zero means no melt transition.
        /// </summary>
        public readonly float MeltThreshold;

        /// <summary>
        /// Material ID to use when the local temperature exceeds <see cref="MeltThreshold"/>.
        /// Falls back to <c>MaterialId + "_molten"</c> when null/empty.
        /// </summary>
        public readonly string MoltenMaterialId;

        public GeologicalLayer(
            string materialId,
            float startDepth,
            float noiseAmplitude,
            float meltThreshold,
            string moltenMaterialId = null)
        {
            if (string.IsNullOrEmpty(materialId))
                throw new ArgumentNullException(nameof(materialId));

            if (startDepth < 0f)
                throw new ArgumentOutOfRangeException(nameof(startDepth), "Start depth must be non-negative.");

            if (noiseAmplitude < 0f)
                throw new ArgumentOutOfRangeException(nameof(noiseAmplitude), "Noise amplitude must be non-negative.");

            MaterialId = materialId;
            StartDepth = startDepth;
            NoiseAmplitude = noiseAmplitude;
            MeltThreshold = meltThreshold;
            MoltenMaterialId = moltenMaterialId;
        }
    }
}
