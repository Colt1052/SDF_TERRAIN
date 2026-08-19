using System;
using NUnit.Framework;
using UnityEngine;
using SDFTerrain.Terrain;

namespace SDFTerrain.Tests
{
    public class TerrainFieldSamplerTests
    {
        [Test]
        public void Sample_NullField_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => TerrainFieldSampler.Sample(null, 8, 10f));
        }

        [Test]
        public void Sample_NonPositiveResolution_Throws()
        {
            var field = new TerrainField(10f);
            Assert.Throws<ArgumentOutOfRangeException>(() => TerrainFieldSampler.Sample(field, 0, 10f));
        }

        [Test]
        public void Sample_NonPositiveMaxRadius_Throws()
        {
            var field = new TerrainField(10f);
            Assert.Throws<ArgumentOutOfRangeException>(() => TerrainFieldSampler.Sample(field, 8, 0f));
        }

        [Test]
        public void Sample_GridDimensionsMatchResolutionPlusOne()
        {
            var field = new TerrainField(10f);
            TerrainFieldSampler.Result result = TerrainFieldSampler.Sample(field, 16, 20f);

            Assert.AreEqual(17, result.Samples.GetLength(0));
            Assert.AreEqual(17, result.Samples.GetLength(1));
        }

        [Test]
        public void Sample_OriginIsBottomLeftOfBoundingBox()
        {
            var field = new TerrainField(10f);
            TerrainFieldSampler.Result result = TerrainFieldSampler.Sample(field, 16, 20f);

            Assert.AreEqual(new Vector2(-20f, -20f), result.Origin);
        }

        [Test]
        public void Sample_CenterCellIsSolid()
        {
            var field = new TerrainField(10f);
            TerrainFieldSampler.Result result = TerrainFieldSampler.Sample(field, 16, 20f);

            int center = 8;
            Assert.Less(result.Samples[center, center], 0f);
        }

        [Test]
        public void Sample_CornerCellIsAir()
        {
            var field = new TerrainField(10f);
            TerrainFieldSampler.Result result = TerrainFieldSampler.Sample(field, 16, 20f);

            Assert.Greater(result.Samples[0, 0], 0f);
        }

        [Test]
        public void Sample_ProducesMeshableGeometryViaMarchingSquares()
        {
            var field = new TerrainField(10f);
            TerrainFieldSampler.Result result = TerrainFieldSampler.Sample(field, 24, 20f);

            Meshing.MeshData meshData = Meshing.MarchingSquaresMesher.Generate(result.Samples, result.CellSize, result.Origin);

            Assert.Greater(meshData.Triangles.Count, 0);
        }
    }
}
