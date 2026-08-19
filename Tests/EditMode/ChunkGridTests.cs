using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using SDFTerrain.Terrain;

namespace SDFTerrain.Tests
{
    public class ChunkGridTests
    {
        [Test]
        public void Constructor_NonPositiveCount_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new ChunkGrid(0));
        }

        [Test]
        public void ChunkCount_MatchesConstructorArgument()
        {
            var grid = new ChunkGrid(8);
            Assert.AreEqual(8, grid.ChunkCount);
        }

        [Test]
        public void GetChunk_OutOfRange_Throws()
        {
            var grid = new ChunkGrid(4);
            Assert.Throws<ArgumentOutOfRangeException>(() => grid.GetChunk(4));
            Assert.Throws<ArgumentOutOfRangeException>(() => grid.GetChunk(-1));
        }

        [Test]
        public void GetChunkAt_ZeroAngle_ReturnsFirstChunk()
        {
            var grid = new ChunkGrid(4);
            Assert.AreEqual(0, grid.GetChunkAt(0f).Index);
        }

        [Test]
        public void GetChunkAt_MidwayIntoSecondChunk_ReturnsSecondChunk()
        {
            var grid = new ChunkGrid(4);
            // Each chunk spans PI/2; midway into chunk 1 is 3*PI/4.
            Assert.AreEqual(1, grid.GetChunkAt(3f * Mathf.PI / 4f).Index);
        }

        [Test]
        public void GetChunkAt_JustBelowTwoPi_ReturnsLastChunk()
        {
            var grid = new ChunkGrid(4);
            Assert.AreEqual(3, grid.GetChunkAt(2f * Mathf.PI - 0.001f).Index);
        }

        [Test]
        public void GetChunkAt_NegativeAngle_WrapsCorrectly()
        {
            var grid = new ChunkGrid(4);
            // -0.001 wraps to just below 2*PI, i.e. the last chunk.
            Assert.AreEqual(3, grid.GetChunkAt(-0.001f).Index);
        }

        [Test]
        public void GetNextChunk_WrapsFromLastToFirst()
        {
            var grid = new ChunkGrid(4);
            Assert.AreEqual(0, grid.GetNextChunk(3).Index);
        }

        [Test]
        public void GetPreviousChunk_WrapsFromFirstToLast()
        {
            var grid = new ChunkGrid(4);
            Assert.AreEqual(3, grid.GetPreviousChunk(0).Index);
        }

        [Test]
        public void GetNextChunk_NonWrapping_ReturnsAdjacentIndex()
        {
            var grid = new ChunkGrid(4);
            Assert.AreEqual(2, grid.GetNextChunk(1).Index);
        }

        [Test]
        public void AllChunks_AreDirtyInitially()
        {
            var grid = new ChunkGrid(4);
            Assert.AreEqual(4, grid.DirtyChunks().Count());
        }

        [Test]
        public void ClearAllDirty_ClearsEveryChunk()
        {
            var grid = new ChunkGrid(4);
            grid.ClearAllDirty();
            Assert.AreEqual(0, grid.DirtyChunks().Count());
        }

        [Test]
        public void MarkDirtyAt_OnlyMarksTargetedChunk()
        {
            var grid = new ChunkGrid(4);
            grid.ClearAllDirty();

            grid.MarkDirtyAt(3f * Mathf.PI / 4f); // chunk 1

            var dirty = grid.DirtyChunks().ToList();
            Assert.AreEqual(1, dirty.Count);
            Assert.AreEqual(1, dirty[0].Index);
        }

        [Test]
        public void Chunks_CoverFullCircleWithoutGapsOrOverlap()
        {
            var grid = new ChunkGrid(6);

            for (int i = 0; i < grid.ChunkCount; i++)
            {
                TerrainChunk chunk = grid.GetChunk(i);
                TerrainChunk next = grid.GetNextChunk(i);

                if (i < grid.ChunkCount - 1)
                {
                    Assert.AreEqual(chunk.EndAngle, next.StartAngle, 1e-5f);
                }
            }

            Assert.AreEqual(0f, grid.GetChunk(0).StartAngle, 1e-5f);
            Assert.AreEqual(2f * Mathf.PI, grid.GetChunk(grid.ChunkCount - 1).EndAngle, 1e-5f);
        }
    }
}
