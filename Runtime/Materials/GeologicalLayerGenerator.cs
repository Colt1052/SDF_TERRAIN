using UnityEngine;
using SDFTerrain.Terrain;

namespace SDFTerrain.Materials
{
    /// <summary>
    /// Result of a geological layer query: the material ID and the local physical conditions
    /// (temperature, pressure) at that position.
    /// </summary>
    public readonly struct GeologicalSampleResult
    {
        /// <summary>The material ID determined by the layer evaluation.</summary>
        public readonly string MaterialId;

        /// <summary>Local temperature in Kelvin at the query depth.</summary>
        public readonly float Temperature;

        /// <summary>Local pressure in megapascals at the query depth.</summary>
        public readonly float Pressure;

        /// <summary>Whether the material is in its molten state (temperature exceeds melt threshold).</summary>
        public readonly bool IsMolten;

        public GeologicalSampleResult(string materialId, float temperature, float pressure, bool isMolten)
        {
            MaterialId = materialId;
            Temperature = temperature;
            Pressure = pressure;
            IsMolten = isMolten;
        }
    }

    /// <summary>
    /// Pure-function utility that determines which geological layer and material occupies a
    /// given position in a <see cref="TerrainField"/>. Evaluates the <see cref="GeologicalProfile"/>
    /// from surface to core, perturbing layer boundaries with noise and checking temperature
    /// against melt thresholds.
    /// </summary>
    public static class GeologicalLayerGenerator
    {
        /// <summary>
        /// Determines the geological material at <paramref name="localPosition"/> within
        /// <paramref name="field"/> using <paramref name="profile"/>.
        /// <para>
        /// If the position is in air (SDF > 0), returns the air material.
        /// Otherwise, computes depth below surface and walks the layer list. The nominal
        /// start depth of each layer is perturbed by 2D positional noise scaled by the
        /// layer's <see cref="GeologicalLayer.NoiseAmplitude"/>. The first layer whose
        /// perturbed start depth is less than or equal to the query depth is selected.
        /// </para>
        /// <para>
        /// Temperature is computed from the profile's gradient and surface temperature.
        /// If the temperature exceeds the layer's melt threshold, the molten material is used.
        /// </para>
        /// </summary>
        public static GeologicalSampleResult Sample(
            TerrainField field,
            Vector2 localPosition,
            GeologicalProfile profile,
            MaterialDatabase database)
        {
            if (field == null)
                throw new System.ArgumentNullException(nameof(field));
            if (profile == null)
                throw new System.ArgumentNullException(nameof(profile));
            if (database == null)
                throw new System.ArgumentNullException(nameof(database));

            float sdf = field.Sample(localPosition);

            // In air — return air material
            if (sdf > 0f)
            {
                return new GeologicalSampleResult(
                    profile.AirMaterialId,
                    profile.SurfaceTemperature,
                    0f,
                    false);
            }

            // Depth below surface
            float depth = -sdf;

            // Compute local physical conditions
            float temperature = ComputeTemperature(depth, profile);
            float pressure = ComputePressure(depth, profile);

            // Evaluate layers surface-to-core
            for (int i = 0; i < profile.Layers.Length; i++)
            {
                GeologicalLayer layer = profile.Layers[i];

                // Perturbed boundary: nominal depth + noise-based undulation
                float noise = GeologicalLayerNoise.Sample(
                    localPosition * profile.NoiseFrequency,
                    profile.NoiseSeed,
                    profile.NoiseFrequency,
                    octaves: 3);

                float perturbedStartDepth = layer.StartDepth + (layer.NoiseAmplitude * noise);

                if (depth >= perturbedStartDepth)
                {
                    // Determine if this material should be molten
                    string materialId = layer.MaterialId;
                    bool isMolten = false;

                    if (layer.MeltThreshold > 0f && temperature >= layer.MeltThreshold)
                    {
                        isMolten = true;
                        materialId = !string.IsNullOrEmpty(layer.MoltenMaterialId)
                            ? layer.MoltenMaterialId
                            : layer.MaterialId + "_molten";
                    }

                    return new GeologicalSampleResult(materialId, temperature, pressure, isMolten);
                }
            }

            // Exhausted all layers — use fallback
            return new GeologicalSampleResult(
                profile.FallbackMaterialId,
                temperature,
                pressure,
                false);
        }

        /// <summary>
        /// Returns the material ID at the given position without computing temperature/pressure.
        /// Equivalent to <see cref="Sample"/>.MaterialId but avoids the extra struct allocation.
        /// </summary>
        public static string SampleId(
            TerrainField field,
            Vector2 localPosition,
            GeologicalProfile profile)
        {
            return Sample(field, localPosition, profile, null).MaterialId;
        }

        private static float ComputeTemperature(float depth, GeologicalProfile profile)
        {
            if (profile.CoreTemperature > 0f)
            {
                // Gradient-based temperature from surface to core
                return profile.SurfaceTemperature + (depth * profile.TemperatureGradient);
            }

            return profile.SurfaceTemperature + (depth * profile.TemperatureGradient);
        }

        private static float ComputePressure(float depth, GeologicalProfile profile)
        {
            return depth * profile.PressureGradient;
        }
    }
}
