using System;

namespace SDFTerrain.Terrain
{
    /// <summary>
    /// Data-driven parameters for procedural terrain height noise. Immutable — construct a new
    /// instance rather than mutating fields, per CLAUDE.md's readonly-configuration guidance.
    /// </summary>
    public readonly struct TerrainNoiseSettings
    {
        public readonly float Amplitude;
        public readonly float Frequency;
        public readonly int Octaves;
        public readonly float Persistence;
        public readonly float Lacunarity;
        public readonly bool Ridged;
        public readonly float WarpStrength;
        public readonly float WarpFrequency;

        public TerrainNoiseSettings(
            float amplitude,
            float frequency,
            int octaves,
            float persistence,
            float lacunarity,
            bool ridged,
            float warpStrength,
            float warpFrequency)
        {
            if (octaves <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(octaves), octaves, "Octaves must be positive.");
            }

            Amplitude = amplitude;
            Frequency = frequency;
            Octaves = octaves;
            Persistence = persistence;
            Lacunarity = lacunarity;
            Ridged = ridged;
            WarpStrength = warpStrength;
            WarpFrequency = warpFrequency;
        }

        /// <summary>No warp, single octave, no height variation — equivalent to a perfect sphere.</summary>
        public static TerrainNoiseSettings None => new TerrainNoiseSettings(
            amplitude: 0f, frequency: 1f, octaves: 1, persistence: 0.5f, lacunarity: 2f,
            ridged: false, warpStrength: 0f, warpFrequency: 1f);
    }
}
