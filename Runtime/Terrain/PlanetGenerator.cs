using System;

namespace SDFTerrain.Terrain
{
    /// <summary>
    /// Deterministic generation pipeline for a planet's terrain, driven entirely by its seed.
    /// Stages run in a fixed order (per SCOPE.md): large-scale shape, then terrain height noise,
    /// geological layers, caves, ore, materials, vegetation, entities. Only the large-scale shape
    /// stage (a perfect sphere) is implemented so far; later tasks extend GenerateBaseShape's
    /// output rather than replacing it, so the seed parameter is threaded through now even though
    /// this stage does not yet consume randomness.
    /// </summary>
    public static class PlanetGenerator
    {
        /// <summary>
        /// Generates the base terrain field for a planet: a perfect sphere of the given radius.
        /// Purely a function of radius and seed — calling this twice with the same inputs always
        /// produces an equivalent field.
        /// </summary>
        public static TerrainField GenerateBaseShape(float radius, int seed)
        {
            return GenerateBaseShape(radius, seed, TerrainNoiseSettings.None);
        }

        /// <summary>
        /// Generates the base terrain field for a planet: a sphere of the given radius perturbed
        /// by deterministic height noise. Purely a function of radius, seed, and noise settings —
        /// calling this twice with the same inputs always produces an equivalent field.
        /// </summary>
        public static TerrainField GenerateBaseShape(float radius, int seed, TerrainNoiseSettings noiseSettings)
        {
            if (radius <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(radius), radius, "Radius must be positive.");
            }

            return new TerrainField(radius, seed, noiseSettings);
        }
    }
}
