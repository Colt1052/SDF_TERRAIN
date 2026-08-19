using System;
using NUnit.Framework;
using UnityEngine;
using SDFTerrain.Core;
using SDFTerrain.Terrain;

namespace SDFTerrain.Tests
{
    public class ChunkSeamCacheTests
    {
        [Test]
        public void Constructor_NullGrid_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new ChunkSeamCache(null));
        }

        [Test]
        public void GetEndDirection_MatchesChunkEndAngleDirection()
        {
            var grid = new ChunkGrid(4);
            var seamCache = new ChunkSeamCache(grid);

            for (int i = 0; i < grid.ChunkCount; i++)
            {
                TerrainChunk chunk = grid.GetChunk(i);
                Vector2 expected = RadialMath.DirectionAt(chunk.EndAngle);
                Assert.AreEqual(expected, seamCache.GetEndDirection(i));
            }
        }

        [Test]
        public void GetStartDirection_MatchesPreviousChunksEndDirection_ExactlyForSharedSeam()
        {
            var grid = new ChunkGrid(4);
            var seamCache = new ChunkSeamCache(grid);

            for (int i = 0; i < grid.ChunkCount; i++)
            {
                int previousIndex = (i - 1 + grid.ChunkCount) % grid.ChunkCount;

                // Exact equality: both chunks sharing a boundary ray must read the same cached
                // Vector2 value, not merely two independently-computed values that happen to be
                // numerically close.
                Assert.AreEqual(seamCache.GetEndDirection(previousIndex), seamCache.GetStartDirection(i));
            }
        }
    }
}
