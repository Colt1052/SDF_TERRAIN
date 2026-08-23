using UnityEngine;
using SDFTerrain.Core;

namespace SDFTerrain.Materials
{
    /// <summary>
    /// 2D sine-harmonic noise for perturbing geological layer boundaries. Unlike
    /// <see cref="Terrain.TerrainNoise"/>, which is angular (seamless around the planet circumference),
    /// this operates in Cartesian space so that layer boundaries undulate naturally across the
    /// entire planet cross-section without angular artifacts.
    /// </summary>
    public static class GeologicalLayerNoise
    {
        private const int MaxHarmonic = 64;

        /// <summary>
        /// Samples 2D layered noise at <paramref name="position"/> using <paramref name="seed"/>
        /// and <paramref name="frequency"/>. Returns a value in [-1, 1]. Uses integer sine harmonics
        /// in both X and Y for continuous, deterministic output.
        /// </summary>
        public static float Sample(Vector2 position, int seed, float frequency, int octaves)
        {
            if (octaves <= 0)
                return 0f;

            float sum = 0f;
            float maxAmplitude = 0f;
            float amplitude = 1f;
            float currentFreq = frequency;

            for (int octave = 0; octave < octaves; octave++)
            {
                var rngX = new SeededRandom(seed + (octave * 1000003));
                var rngY = new SeededRandom(seed + (octave * 1000003) + 7919);

                int harmonicX = ToIntegerHarmonic(currentFreq);
                int harmonicY = ToIntegerHarmonic(currentFreq);

                float phaseX = rngX.NextFloat(0f, 2f * Mathf.PI);
                float phaseY = rngY.NextFloat(0f, 2f * Mathf.PI);

                // 2D harmonic: product of independent sine waves per axis
                float wave = Mathf.Sin(harmonicX * position.x + phaseX)
                           * Mathf.Sin(harmonicY * position.y + phaseY);

                sum += amplitude * wave;
                maxAmplitude += amplitude;

                amplitude *= 0.5f;
                currentFreq *= 2.0f;
            }

            return maxAmplitude > 0f ? sum / maxAmplitude : 0f;
        }

        private static int ToIntegerHarmonic(float frequency)
        {
            int harmonic = Mathf.RoundToInt(frequency);
            return Mathf.Clamp(harmonic, 1, MaxHarmonic);
        }
    }
}
