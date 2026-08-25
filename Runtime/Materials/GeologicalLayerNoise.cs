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
        private const int MaxOctaves = 4;

        /// <summary>
        /// Precomputed parameters for a single octave of geological noise.
        /// Avoids per-sample allocation of <see cref="SeededRandom"/> instances.
        /// </summary>
        private readonly struct OctaveParams
        {
            public readonly int HarmonicX;
            public readonly int HarmonicY;
            public readonly float PhaseX;
            public readonly float PhaseY;
            public readonly float Amplitude;

            public OctaveParams(int harmonicX, int harmonicY, float phaseX, float phaseY, float amplitude)
            {
                HarmonicX = harmonicX;
                HarmonicY = harmonicY;
                PhaseX = phaseX;
                PhaseY = phaseY;
                Amplitude = amplitude;
            }
        }

        /// <summary>
        /// Caches the precomputed octave parameters for a given (seed, frequency, octaves) triplet.
        /// Because these three values are constant for a given <see cref="GeologicalProfile"/>,
        /// we compute the phases and harmonics once and reuse them for every sample call.
        /// </summary>
        private struct CacheEntry
        {
            public int Seed;
            public float Frequency;
            public int Octaves;
            public OctaveParams[] OctavesData;
        }

        // Not readonly — we replace the whole struct on cache invalidation.
        private static CacheEntry _cache;

        /// <summary>
        /// Clears the cached parameters. Call only if you change geological profile parameters
        /// at runtime and need a recomputation.
        /// </summary>
        public static void ClearCache()
        {
            _cache = default;
        }

        /// <summary>
        /// Samples 2D layered noise at <paramref name="position"/> using <paramref name="seed"/>
        /// and <paramref name="frequency"/>. Returns a value in [-1, 1]. Uses integer sine harmonics
        /// in both X and Y for continuous, deterministic output.
        ///
        /// Optimization: precomputes phase and harmonic values once per (seed, frequency, octaves)
        /// triplet. Subsequent calls with the same parameters are allocation-free and branch-light.
        /// </summary>
        public static float Sample(Vector2 position, int seed, float frequency, int octaves)
        {
            if (octaves <= 0)
                return 0f;

            // Clamp to maximum supported octaves.
            if (octaves > MaxOctaves)
                octaves = MaxOctaves;

            // Precompute octave parameters if cache is stale.
            // The (seed, frequency) is constant for a given GeologicalProfile, so this runs
            // once per profile change, then every subsequent sample is allocation-free.
            if (_cache.Seed != seed || _cache.Frequency != frequency || _cache.Octaves != octaves)
            {
                ComputeOctaveParams(seed, frequency, octaves);
            }

            // Evaluate precomputed harmonics — no allocations, no branches per octave.
            float sum = 0f;
            OctaveParams[] data = _cache.OctavesData;
            for (int i = 0; i < octaves; i++)
            {
                OctaveParams p = data[i];
                float wave = Mathf.Sin(p.HarmonicX * position.x + p.PhaseX)
                           * Mathf.Sin(p.HarmonicY * position.y + p.PhaseY);
                sum += p.Amplitude * wave;
            }

            // Amplitude normalization is baked: sum of 1.0 + 0.5 + 0.25 + ... = 2 - (0.5 ^ octaves).
            float maxAmplitude = 2f - Mathf.Pow(0.5f, octaves);
            return maxAmplitude > 0f ? sum / maxAmplitude : 0f;
        }

        private static void ComputeOctaveParams(int seed, float frequency, int octaves)
        {
            var data = new OctaveParams[MaxOctaves];
            float currentFreq = frequency;
            float amplitude = 1f;

            for (int octave = 0; octave < octaves; octave++)
            {
                // Compute phases deterministically — only done once per profile change.
                var rngX = new SeededRandom(seed + (octave * 1000003));
                var rngY = new SeededRandom(seed + (octave * 1000003) + 7919);

                int harmonicX = ToIntegerHarmonic(currentFreq);
                int harmonicY = ToIntegerHarmonic(currentFreq);

                float phaseX = rngX.NextFloat(0f, 2f * Mathf.PI);
                float phaseY = rngY.NextFloat(0f, 2f * Mathf.PI);

                data[octave] = new OctaveParams(harmonicX, harmonicY, phaseX, phaseY, amplitude);

                amplitude *= 0.5f;
                currentFreq *= 2.0f;
            }

            // Fill remaining slots to avoid boundary checks.
            for (int i = octaves; i < MaxOctaves; i++)
            {
                data[i] = new OctaveParams(1, 1, 0f, 0f, 0f);
            }

            _cache = new CacheEntry
            {
                Seed = seed,
                Frequency = frequency,
                Octaves = octaves,
                OctavesData = data
            };
        }

        private static int ToIntegerHarmonic(float frequency)
        {
            int harmonic = Mathf.RoundToInt(frequency);
            return Mathf.Clamp(harmonic, 1, MaxHarmonic);
        }
    }
}
