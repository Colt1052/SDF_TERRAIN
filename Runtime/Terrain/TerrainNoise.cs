using UnityEngine;
using SDFTerrain.Core;

namespace SDFTerrain.Terrain
{
    /// <summary>
    /// Deterministic, seamless height noise around a planet's circumference. Seamlessness (no
    /// crack at the angle-0/2*PI seam) is achieved by summing sine harmonics with integer
    /// frequencies — sin(k * angle + phase) is exactly periodic over 2*PI for any integer k, so
    /// any sum of such terms is too. This avoids the wrap-seam artifacts a grid-based noise
    /// (e.g. Perlin sampled in x/y and reprojected to angle) would need special-casing for.
    /// </summary>
    public static class TerrainNoise
    {
        private const int MaxHarmonic = 64;

        /// <summary>
        /// Returns a height offset (same units as radius) for the given angle, built from
        /// fractal or ridged harmonic noise with optional domain warp, entirely determined by
        /// seed and settings. Reconstructs per-octave phases from scratch every call — prefer the
        /// <see cref="TerrainNoiseCache"/> overload when sampling repeatedly for the same
        /// (seed, settings) pair, e.g. every lattice point of a chunk rebuild.
        /// </summary>
        public static float SampleHeight(float angleRadians, int seed, TerrainNoiseSettings settings)
        {
            if (settings.Amplitude == 0f)
            {
                return 0f;
            }

            float warpedAngle = ApplyDomainWarp(angleRadians, seed, settings);
            float raw = settings.Ridged
                ? SampleRidgedFractal(warpedAngle, seed, settings)
                : SampleFractal(warpedAngle, seed, settings);

            return raw * settings.Amplitude;
        }

        /// <summary>
        /// Same as <see cref="SampleHeight(float, int, TerrainNoiseSettings)"/>, but reads
        /// precomputed phases from <paramref name="cache"/> instead of reconstructing a
        /// <see cref="SeededRandom"/> per octave — bit-identical output, computed far cheaper
        /// when sampling many points for the same field.
        /// </summary>
        public static float SampleHeight(float angleRadians, TerrainNoiseCache cache, TerrainNoiseSettings settings)
        {
            if (settings.Amplitude == 0f)
            {
                return 0f;
            }

            float warpedAngle = ApplyDomainWarp(angleRadians, cache, settings);
            float raw = settings.Ridged
                ? SampleRidgedFractal(warpedAngle, cache, settings)
                : SampleFractal(warpedAngle, cache, settings);

            return raw * settings.Amplitude;
        }

        private static float ApplyDomainWarp(float angleRadians, int seed, TerrainNoiseSettings settings)
        {
            if (settings.WarpStrength == 0f)
            {
                return angleRadians;
            }

            var rng = new SeededRandom(seed ^ unchecked((int)0x57A9F00D));
            int harmonic = ToIntegerHarmonic(settings.WarpFrequency);
            float phase = rng.NextFloat(0f, 2f * Mathf.PI);
            float warp = Mathf.Sin((harmonic * angleRadians) + phase);
            return angleRadians + (warp * settings.WarpStrength);
        }

        private static float ApplyDomainWarp(float angleRadians, TerrainNoiseCache cache, TerrainNoiseSettings settings)
        {
            if (settings.WarpStrength == 0f)
            {
                return angleRadians;
            }

            int harmonic = ToIntegerHarmonic(settings.WarpFrequency);
            float warp = Mathf.Sin((harmonic * angleRadians) + cache.WarpPhase);
            return angleRadians + (warp * settings.WarpStrength);
        }

        private static float SampleFractal(float angleRadians, int seed, TerrainNoiseSettings settings)
        {
            float sum = 0f;
            float maxAmplitude = 0f;
            float amplitude = 1f;
            float frequency = settings.Frequency;

            for (int octave = 0; octave < settings.Octaves; octave++)
            {
                var rng = new SeededRandom(seed + (octave * 1000003));
                int harmonic = ToIntegerHarmonic(frequency);
                float phase = rng.NextFloat(0f, 2f * Mathf.PI);

                sum += amplitude * Mathf.Sin((harmonic * angleRadians) + phase);
                maxAmplitude += amplitude;

                amplitude *= settings.Persistence;
                frequency *= settings.Lacunarity;
            }

            return maxAmplitude > 0f ? sum / maxAmplitude : 0f;
        }

        private static float SampleFractal(float angleRadians, TerrainNoiseCache cache, TerrainNoiseSettings settings)
        {
            float sum = 0f;
            float maxAmplitude = 0f;
            float amplitude = 1f;
            float frequency = settings.Frequency;

            for (int octave = 0; octave < settings.Octaves; octave++)
            {
                int harmonic = ToIntegerHarmonic(frequency);
                float phase = cache.OctavePhases[octave];

                sum += amplitude * Mathf.Sin((harmonic * angleRadians) + phase);
                maxAmplitude += amplitude;

                amplitude *= settings.Persistence;
                frequency *= settings.Lacunarity;
            }

            return maxAmplitude > 0f ? sum / maxAmplitude : 0f;
        }

        private static float SampleRidgedFractal(float angleRadians, int seed, TerrainNoiseSettings settings)
        {
            float sum = 0f;
            float maxAmplitude = 0f;
            float amplitude = 1f;
            float frequency = settings.Frequency;

            for (int octave = 0; octave < settings.Octaves; octave++)
            {
                var rng = new SeededRandom(seed + (octave * 1000003));
                int harmonic = ToIntegerHarmonic(frequency);
                float phase = rng.NextFloat(0f, 2f * Mathf.PI);

                float wave = Mathf.Sin((harmonic * angleRadians) + phase);
                float ridge = 1f - Mathf.Abs(wave);

                sum += amplitude * ridge;
                maxAmplitude += amplitude;

                amplitude *= settings.Persistence;
                frequency *= settings.Lacunarity;
            }

            float normalized = maxAmplitude > 0f ? sum / maxAmplitude : 0f;
            // Re-center ridged output around zero so it composes the same way as fractal noise
            // (an unshifted ridge is always >= 0, which would only ever raise the surface).
            return (normalized * 2f) - 1f;
        }

        private static float SampleRidgedFractal(float angleRadians, TerrainNoiseCache cache, TerrainNoiseSettings settings)
        {
            float sum = 0f;
            float maxAmplitude = 0f;
            float amplitude = 1f;
            float frequency = settings.Frequency;

            for (int octave = 0; octave < settings.Octaves; octave++)
            {
                int harmonic = ToIntegerHarmonic(frequency);
                float phase = cache.OctavePhases[octave];

                float wave = Mathf.Sin((harmonic * angleRadians) + phase);
                float ridge = 1f - Mathf.Abs(wave);

                sum += amplitude * ridge;
                maxAmplitude += amplitude;

                amplitude *= settings.Persistence;
                frequency *= settings.Lacunarity;
            }

            float normalized = maxAmplitude > 0f ? sum / maxAmplitude : 0f;
            return (normalized * 2f) - 1f;
        }

        private static int ToIntegerHarmonic(float frequency)
        {
            int harmonic = Mathf.RoundToInt(frequency);
            return Mathf.Clamp(harmonic, 1, MaxHarmonic);
        }
    }
}
