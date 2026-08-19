using System;
using NUnit.Framework;
using UnityEngine;
using SDFTerrain.Terrain;

namespace SDFTerrain.Tests
{
    public class PlanetGeneratorTests
    {
        [Test]
        public void GenerateBaseShape_NonPositiveRadius_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => PlanetGenerator.GenerateBaseShape(0f, seed: 1));
        }

        [Test]
        public void GenerateBaseShape_RadiusMatchesRequestedRadius()
        {
            TerrainField field = PlanetGenerator.GenerateBaseShape(25f, seed: 1);
            Assert.AreEqual(25f, field.BaseRadius);
        }

        [Test]
        public void GenerateBaseShape_SurfaceIsContinuousAroundFullCircle()
        {
            TerrainField field = PlanetGenerator.GenerateBaseShape(15f, seed: 1);

            for (int i = 0; i < 360; i++)
            {
                float angle = i * Mathf.Deg2Rad;
                Vector2 surfacePoint = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * 15f;
                Assert.AreEqual(0f, field.Sample(surfacePoint), 1e-3f);
            }
        }

        [Test]
        public void GenerateBaseShape_HasNoEditsInitially()
        {
            TerrainField field = PlanetGenerator.GenerateBaseShape(10f, seed: 1);
            Assert.AreEqual(0, field.Edits.Count);
        }

        [Test]
        public void GenerateBaseShape_SameInputs_ProducesEquivalentField()
        {
            TerrainField fieldA = PlanetGenerator.GenerateBaseShape(20f, seed: 99);
            TerrainField fieldB = PlanetGenerator.GenerateBaseShape(20f, seed: 99);

            for (int i = 0; i < 16; i++)
            {
                float angle = i * (2f * Mathf.PI / 16f);
                Vector2 point = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * 12f;
                Assert.AreEqual(fieldA.Sample(point), fieldB.Sample(point), 1e-5f);
            }
        }

        [Test]
        public void GenerateBaseShape_WithNoise_SameSeed_ProducesEquivalentField()
        {
            var noise = new TerrainNoiseSettings(
                amplitude: 3f, frequency: 4f, octaves: 3, persistence: 0.5f, lacunarity: 2f,
                ridged: false, warpStrength: 0.2f, warpFrequency: 2f);

            TerrainField fieldA = PlanetGenerator.GenerateBaseShape(20f, seed: 55, noise);
            TerrainField fieldB = PlanetGenerator.GenerateBaseShape(20f, seed: 55, noise);

            for (int i = 0; i < 16; i++)
            {
                float angle = i * (2f * Mathf.PI / 16f);
                Assert.AreEqual(fieldA.SurfaceRadiusAt(angle), fieldB.SurfaceRadiusAt(angle), 1e-5f);
            }
        }

        [Test]
        public void GenerateBaseShape_WithNoise_VariesSurfaceRadiusByAngle()
        {
            var noise = new TerrainNoiseSettings(
                amplitude: 5f, frequency: 6f, octaves: 3, persistence: 0.5f, lacunarity: 2f,
                ridged: false, warpStrength: 0f, warpFrequency: 1f);

            TerrainField field = PlanetGenerator.GenerateBaseShape(20f, seed: 5, noise);

            float radiusAtZero = field.SurfaceRadiusAt(0f);
            float radiusAtQuarter = field.SurfaceRadiusAt(Mathf.PI / 2f);

            Assert.AreNotEqual(radiusAtZero, radiusAtQuarter);
        }

        [Test]
        public void GenerateBaseShape_WithNoise_SurfaceIsSeamlessAcrossAngleZero()
        {
            var noise = new TerrainNoiseSettings(
                amplitude: 4f, frequency: 5f, octaves: 4, persistence: 0.5f, lacunarity: 2f,
                ridged: true, warpStrength: 0.3f, warpFrequency: 3f);

            TerrainField field = PlanetGenerator.GenerateBaseShape(20f, seed: 11, noise);

            float justBelowTwoPi = field.SurfaceRadiusAt(2f * Mathf.PI - 1e-4f);
            float justAboveZero = field.SurfaceRadiusAt(1e-4f);

            Assert.AreEqual(justBelowTwoPi, justAboveZero, 1e-2f);
        }
    }
}
