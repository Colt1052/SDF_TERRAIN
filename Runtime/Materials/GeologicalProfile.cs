using System;
using UnityEngine;

namespace SDFTerrain.Materials
{
    /// <summary>
    /// Configuration for geological layer generation. Defines the sequence of layers from
    /// surface to core, plus heat and pressure gradients that modulate material transitions.
    /// </summary>
    public class GeologicalProfile
    {
        /// <summary>Layers evaluated surface-to-core. First layer whose (perturbed) start depth <= query depth wins.</summary>
        public readonly GeologicalLayer[] Layers;

        /// <summary>Temperature gradient: Kelvin increase per world unit of depth.</summary>
        public readonly float TemperatureGradient;

        /// <summary>Base surface temperature in Kelvin (e.g., from planet atmosphere).</summary>
        public readonly float SurfaceTemperature;

        /// <summary>Core temperature override. When non-zero, the core uses this instead of the gradient.</summary>
        public readonly float CoreTemperature;

        /// <summary>Pressure gradient: megapascals per world unit of depth.</summary>
        public readonly float PressureGradient;

        /// <summary>Seed for deterministic geological noise.</summary>
        public readonly int NoiseSeed;

        /// <summary>Scales the noise frequency — higher = more crumpled boundaries.</summary>
        public readonly float NoiseFrequency;

        /// <summary>Fallback material when all layers are exhausted.</summary>
        public readonly string FallbackMaterialId;

        /// <summary>Material to use when in air (outside the terrain).</summary>
        public readonly string AirMaterialId;

        public GeologicalProfile(
            GeologicalLayer[] layers,
            float temperatureGradient,
            float surfaceTemperature,
            float coreTemperature,
            float pressureGradient,
            int noiseSeed,
            float noiseFrequency,
            string fallbackMaterialId,
            string airMaterialId = "air")
        {
            if (layers == null || layers.Length == 0)
                throw new ArgumentNullException(nameof(layers), "At least one layer is required.");

            Layers = layers;
            TemperatureGradient = temperatureGradient;
            SurfaceTemperature = surfaceTemperature;
            CoreTemperature = coreTemperature;
            PressureGradient = pressureGradient;
            NoiseSeed = noiseSeed;
            NoiseFrequency = noiseFrequency;
            FallbackMaterialId = fallbackMaterialId;
            AirMaterialId = airMaterialId;
        }

        /// <summary>
        /// Creates a default Earth-like profile: Dirt (0-3 units), Stone (3-15 units),
        /// Deep stone (15-30 units), Mantle (30+ units).
        /// </summary>
        public static GeologicalProfile EarthLike(int noiseSeed = 42, float noiseFrequency = 0.3f)
        {
            var layers = new GeologicalLayer[]
            {
                // Surface layer — soil/dirt
                new GeologicalLayer("dirt", 0f, 0.5f, 0f),
                // Stone layer — common bedrock
                new GeologicalLayer("stone", 3f, 1.0f, 0f),
                // Deep stone — harder, denser rock
                new GeologicalLayer("deep_stone", 15f, 1.5f, 0f),
                // Mantle — hot, plastic rock
                new GeologicalLayer("mantle", 30f, 2.0f, 1300f, "molten_mantle"),
            };

            return new GeologicalProfile(
                layers: layers,
                temperatureGradient: 50f,
                surfaceTemperature: 290f,
                coreTemperature: 6000f,
                pressureGradient: 10f,
                noiseSeed: noiseSeed,
                noiseFrequency: noiseFrequency,
                fallbackMaterialId: "mantle",
                airMaterialId: "air");
        }

        /// <summary>
        /// Creates a simple ice world profile: Snow (0-2 units), Ice (2-20 units), Frozen core (20+ units).
        /// </summary>
        public static GeologicalProfile IceWorld(int noiseSeed = 42, float noiseFrequency = 0.2f)
        {
            var layers = new GeologicalLayer[]
            {
                new GeologicalLayer("snow", 0f, 0.3f, 0f),
                new GeologicalLayer("ice", 2f, 0.5f, 0f),
                new GeologicalLayer("ice", 20f, 0.5f, 250f, "water"),
            };

            return new GeologicalProfile(
                layers: layers,
                temperatureGradient: 10f,
                surfaceTemperature: 180f,
                coreTemperature: 300f,
                pressureGradient: 5f,
                noiseSeed: noiseSeed,
                noiseFrequency: noiseFrequency,
                fallbackMaterialId: "ice",
                airMaterialId: "air");
        }
    }
}
