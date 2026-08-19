using NUnit.Framework;
using UnityEngine;
using SDFTerrain.Terrain;

namespace SDFTerrain.Tests
{
    public class TerrainNoiseTests
    {
        [Test]
        public void SampleHeight_ZeroAmplitude_IsAlwaysZero()
        {
            var settings = TerrainNoiseSettings.None;
            Assert.AreEqual(0f, TerrainNoise.SampleHeight(1.23f, seed: 1, settings));
        }

        [Test]
        public void SampleHeight_SameSeedAndAngle_IsDeterministic()
        {
            var settings = new TerrainNoiseSettings(
                amplitude: 2f, frequency: 4f, octaves: 3, persistence: 0.5f, lacunarity: 2f,
                ridged: false, warpStrength: 0.1f, warpFrequency: 2f);

            float a = TerrainNoise.SampleHeight(0.77f, seed: 42, settings);
            float b = TerrainNoise.SampleHeight(0.77f, seed: 42, settings);

            Assert.AreEqual(a, b, 1e-6f);
        }

        [Test]
        public void SampleHeight_DifferentSeeds_ProduceDifferentHeights()
        {
            var settings = new TerrainNoiseSettings(
                amplitude: 2f, frequency: 4f, octaves: 3, persistence: 0.5f, lacunarity: 2f,
                ridged: false, warpStrength: 0f, warpFrequency: 1f);

            float a = TerrainNoise.SampleHeight(0.77f, seed: 1, settings);
            float b = TerrainNoise.SampleHeight(0.77f, seed: 2, settings);

            Assert.AreNotEqual(a, b);
        }

        [Test]
        public void SampleHeight_IsSeamlessAcrossTheAngleZeroBoundary()
        {
            var settings = new TerrainNoiseSettings(
                amplitude: 3f, frequency: 5f, octaves: 4, persistence: 0.5f, lacunarity: 2f,
                ridged: false, warpStrength: 0.3f, warpFrequency: 3f);

            float justBelowTwoPi = TerrainNoise.SampleHeight(2f * Mathf.PI - 1e-4f, seed: 7, settings);
            float justAboveZero = TerrainNoise.SampleHeight(1e-4f, seed: 7, settings);

            Assert.AreEqual(justBelowTwoPi, justAboveZero, 1e-2f);
        }

        [Test]
        public void SampleHeight_StaysWithinAmplitudeBounds()
        {
            var settings = new TerrainNoiseSettings(
                amplitude: 5f, frequency: 3f, octaves: 5, persistence: 0.5f, lacunarity: 2f,
                ridged: true, warpStrength: 0.2f, warpFrequency: 2f);

            for (int i = 0; i < 360; i++)
            {
                float angle = i * Mathf.Deg2Rad;
                float height = TerrainNoise.SampleHeight(angle, seed: 3, settings);
                Assert.LessOrEqual(Mathf.Abs(height), 5f + 1e-3f);
            }
        }

        [Test]
        public void SampleHeight_RidgedAndFractal_ProduceDifferentResults()
        {
            var fractal = new TerrainNoiseSettings(
                amplitude: 2f, frequency: 4f, octaves: 3, persistence: 0.5f, lacunarity: 2f,
                ridged: false, warpStrength: 0f, warpFrequency: 1f);
            var ridged = new TerrainNoiseSettings(
                amplitude: 2f, frequency: 4f, octaves: 3, persistence: 0.5f, lacunarity: 2f,
                ridged: true, warpStrength: 0f, warpFrequency: 1f);

            float a = TerrainNoise.SampleHeight(0.5f, seed: 9, fractal);
            float b = TerrainNoise.SampleHeight(0.5f, seed: 9, ridged);

            Assert.AreNotEqual(a, b);
        }

        [Test]
        public void SampleHeight_WithCache_MatchesPerCallComputation_Fractal()
        {
            var settings = new TerrainNoiseSettings(
                amplitude: 3f, frequency: 5f, octaves: 4, persistence: 0.5f, lacunarity: 2f,
                ridged: false, warpStrength: 0.3f, warpFrequency: 3f);
            var cache = TerrainNoiseCache.Build(seed: 7, settings);

            for (int i = 0; i < 16; i++)
            {
                float angle = i * (Mathf.PI / 8f);
                float expected = TerrainNoise.SampleHeight(angle, seed: 7, settings);
                float actual = TerrainNoise.SampleHeight(angle, cache, settings);
                Assert.AreEqual(expected, actual, 1e-6f);
            }
        }

        [Test]
        public void SampleHeight_WithCache_MatchesPerCallComputation_Ridged()
        {
            var settings = new TerrainNoiseSettings(
                amplitude: 5f, frequency: 3f, octaves: 5, persistence: 0.5f, lacunarity: 2f,
                ridged: true, warpStrength: 0.2f, warpFrequency: 2f);
            var cache = TerrainNoiseCache.Build(seed: 3, settings);

            for (int i = 0; i < 16; i++)
            {
                float angle = i * (Mathf.PI / 8f);
                float expected = TerrainNoise.SampleHeight(angle, seed: 3, settings);
                float actual = TerrainNoise.SampleHeight(angle, cache, settings);
                Assert.AreEqual(expected, actual, 1e-6f);
            }
        }

        [Test]
        public void SampleHeight_WithCache_NoWarp_MatchesPerCallComputation()
        {
            var settings = new TerrainNoiseSettings(
                amplitude: 2f, frequency: 4f, octaves: 3, persistence: 0.5f, lacunarity: 2f,
                ridged: false, warpStrength: 0f, warpFrequency: 1f);
            var cache = TerrainNoiseCache.Build(seed: 42, settings);

            float expected = TerrainNoise.SampleHeight(0.77f, seed: 42, settings);
            float actual = TerrainNoise.SampleHeight(0.77f, cache, settings);

            Assert.AreEqual(expected, actual, 1e-6f);
        }
    }
}
