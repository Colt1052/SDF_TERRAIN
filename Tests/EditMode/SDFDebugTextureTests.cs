using NUnit.Framework;
using UnityEngine;
using SDFTerrain.Terrain;

namespace SDFTerrain.Tests
{
    public class SDFDebugTextureTests
    {
        [Test]
        public void Build_TextureDimensionsMatchSampleGrid()
        {
            var field = new TerrainField(10f);
            TerrainFieldSampler.Result result = TerrainFieldSampler.Sample(field, 16, 20f);

            Texture2D texture = SDFDebugTexture.Build(result);

            Assert.AreEqual(result.Samples.GetLength(0), texture.width);
            Assert.AreEqual(result.Samples.GetLength(1), texture.height);
        }

        [Test]
        public void Build_SolidSamplePixel_IsNotEqualToAirSamplePixel()
        {
            var field = new TerrainField(10f);
            TerrainFieldSampler.Result result = TerrainFieldSampler.Sample(field, 16, 20f);

            Texture2D texture = SDFDebugTexture.Build(result);

            // Center of the grid is deep inside the planet (solid), a corner is far outside (air).
            int center = 8;
            Color32 solidPixel = texture.GetPixel(center, center);
            Color32 airPixel = texture.GetPixel(0, 0);

            Assert.AreNotEqual(solidPixel, airPixel);
        }

        [Test]
        public void Build_ExistingTextureWithMatchingDimensions_IsReused()
        {
            var field = new TerrainField(10f);
            TerrainFieldSampler.Result result = TerrainFieldSampler.Sample(field, 16, 20f);

            Texture2D first = SDFDebugTexture.Build(result);
            Texture2D second = SDFDebugTexture.Build(result, first);

            Assert.AreSame(first, second);
        }

        [Test]
        public void Build_ExistingTextureWithDifferentDimensions_IsReplaced()
        {
            var field = new TerrainField(10f);
            TerrainFieldSampler.Result small = TerrainFieldSampler.Sample(field, 8, 20f);
            TerrainFieldSampler.Result large = TerrainFieldSampler.Sample(field, 32, 20f);

            Texture2D first = SDFDebugTexture.Build(small);
            Texture2D second = SDFDebugTexture.Build(large, first);

            Assert.AreNotSame(first, second);
            Assert.AreEqual(large.Samples.GetLength(0), second.width);
        }
    }
}
