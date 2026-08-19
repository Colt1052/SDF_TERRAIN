using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using SDFTerrain.Terrain;

namespace SDFTerrain.Tests
{
    public class ChunkGridTests
    {
        [Test]
        public void Constructor_NonPositiveRadius_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new ChunkGrid(0f, chunkSize: 1f));
        }

        [Test]
        public void Constructor_NonPositiveChunkSize_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new ChunkGrid(10f, chunkSize: 0f));
        }

        [Test]
        public void Constructor_CreatesExpectedGridDimensions()
        {
            // Radius 10, chunkSize 5 -> cols = ceil(20/5) = 4, rows = 4
            var grid = new ChunkGrid(10f, chunkSize: 5f);

            Assert.AreEqual(4, grid.Cols);
            Assert.AreEqual(4, grid.Rows);
            Assert.AreEqual(16, grid.ChunkCount);
        }

        [Test]
        public void Constructor_ChunkBoundingBoxesAreCorrect()
        {
            // Radius 5, chunkSize 5 -> cols = ceil(10/5) = 2, rows = 2
            // Grid spans -5 to +5 in both axes (centered on origin).
            var grid = new ChunkGrid(5f, chunkSize: 5f);

            // Chunk at col=0, row=0 -> minX=-5, maxX=0, minY=-5, maxY=0
            TerrainChunk chunk0 = grid.GetChunkAtGrid(0, 0);
            Assert.AreEqual(0, chunk0.Col);
            Assert.AreEqual(0, chunk0.Row);
            Assert.AreEqual(-5f, chunk0.MinX);
            Assert.AreEqual(0f, chunk0.MaxX);
            Assert.AreEqual(-5f, chunk0.MinY);
            Assert.AreEqual(0f, chunk0.MaxY);

            // Chunk at col=1, row=0 -> minX=0, maxX=5, minY=-5, maxY=0
            TerrainChunk chunk1 = grid.GetChunkAtGrid(1, 0);
            Assert.AreEqual(1, chunk1.Col);
            Assert.AreEqual(0, chunk1.Row);
            Assert.AreEqual(0f, chunk1.MinX);
            Assert.AreEqual(5f, chunk1.MaxX);
            Assert.AreEqual(-5f, chunk1.MinY);
            Assert.AreEqual(0f, chunk1.MaxY);

            // Chunk at col=0, row=1 -> minX=-5, maxX=0, minY=0, maxY=5
            TerrainChunk chunk2 = grid.GetChunkAtGrid(0, 1);
            Assert.AreEqual(0, chunk2.Col);
            Assert.AreEqual(1, chunk2.Row);
            Assert.AreEqual(-5f, chunk2.MinX);
            Assert.AreEqual(0f, chunk2.MaxX);
            Assert.AreEqual(0f, chunk2.MinY);
            Assert.AreEqual(5f, chunk2.MaxY);

            // Chunk at col=1, row=1 -> minX=0, maxX=0, minY=0, maxY=5
            TerrainChunk chunk3 = grid.GetChunkAtGrid(1, 1);
            Assert.AreEqual(1, chunk3.Col);
            Assert.AreEqual(1, chunk3.Row);
            Assert.AreEqual(0f, chunk3.MinX);
            Assert.AreEqual(5f, chunk3.MaxX);
            Assert.AreEqual(0f, chunk3.MinY);
            Assert.AreEqual(5f, chunk3.MaxY);
        }

        [Test]
        public void GetChunk_InvalidIndex_Throws()
        {
            var grid = new ChunkGrid(10f, chunkSize: 5f);
            Assert.Throws<ArgumentOutOfRangeException>(() => grid.GetChunk(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => grid.GetChunk(999));
        }

        [Test]
        public void GetChunkAt_Center_ReturnsCenterChunk()
        {
            var grid = new ChunkGrid(10f, chunkSize: 5f);

            TerrainChunk chunk = grid.GetChunkAt(Vector2.zero);

            Assert.IsNotNull(chunk);
            // Origin (0,0) should fall in the center chunk, not the edge.
            Assert.Greater(chunk.Col, 0);
            Assert.Less(chunk.Col, grid.Cols - 1);
            Assert.Greater(chunk.Row, 0);
            Assert.Less(chunk.Row, grid.Rows - 1);
        }

        [Test]
        public void GetChunkAt_PositionInChunk_ReturnsThatChunk()
        {
            var grid = new ChunkGrid(5f, chunkSize: 5f);

            // Position (3, 3) is in col=1, row=1 for grid -5..+5
            TerrainChunk chunk = grid.GetChunkAt(new Vector2(3f, 3f));

            Assert.AreEqual(1, chunk.Col);
            Assert.AreEqual(1, chunk.Row);
        }

        [Test]
        public void GetChunkAt_OutOfBounds_CreatesNewChunk()
        {
            var grid = new ChunkGrid(5f, chunkSize: 5f);
            int initialCount = grid.ChunkCount;

            // Far positive position -> outside original grid, creates a new chunk
            TerrainChunk chunk = grid.GetChunkAt(new Vector2(100f, 100f));

            Assert.IsNotNull(chunk);
            Assert.Greater(chunk.Col, grid.Cols - 1);
            Assert.Greater(chunk.Row, grid.Rows - 1);
            Assert.AreEqual(initialCount + 1, grid.ChunkCount);
        }

        [Test]
        public void GetChunkAt_OutOfBounds_SecondCall_ReturnsSameChunk()
        {
            var grid = new ChunkGrid(5f, chunkSize: 5f);

            TerrainChunk chunk1 = grid.GetChunkAt(new Vector2(100f, 100f));
            TerrainChunk chunk2 = grid.GetChunkAt(new Vector2(100f, 100f));

            Assert.AreSame(chunk1, chunk2);
        }

        [Test]
        public void GetChunkAtGrid_ValidCoordinates_ReturnsChunk()
        {
            var grid = new ChunkGrid(10f, chunkSize: 5f);

            TerrainChunk chunk = grid.GetChunkAtGrid(2, 1);

            Assert.AreEqual(2, chunk.Col);
            Assert.AreEqual(1, chunk.Row);
        }

        [Test]
        public void GetChunkAtGrid_OutOfBounds_Throws()
        {
            var grid = new ChunkGrid(10f, chunkSize: 5f);
            Assert.Throws<ArgumentOutOfRangeException>(() => grid.GetChunkAtGrid(4, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => grid.GetChunkAtGrid(0, 4));
            Assert.Throws<ArgumentOutOfRangeException>(() => grid.GetChunkAtGrid(-1, 0));
        }

        [Test]
        public void GetNeighbor_Right_ReturnsAdjacentChunk()
        {
            var grid = new ChunkGrid(10f, chunkSize: 5f);
            TerrainChunk chunk = grid.GetChunkAtGrid(1, 1);

            TerrainChunk neighbor = grid.GetNeighbor(chunk, ChunkGrid.ChunkNeighbor.Right);

            Assert.IsNotNull(neighbor);
            Assert.AreEqual(2, neighbor.Col);
            Assert.AreEqual(1, neighbor.Row);
        }

        [Test]
        public void GetNeighbor_Left_ReturnsAdjacentChunk()
        {
            var grid = new ChunkGrid(10f, chunkSize: 5f);
            TerrainChunk chunk = grid.GetChunkAtGrid(2, 1);

            TerrainChunk neighbor = grid.GetNeighbor(chunk, ChunkGrid.ChunkNeighbor.Left);

            Assert.IsNotNull(neighbor);
            Assert.AreEqual(1, neighbor.Col);
            Assert.AreEqual(1, neighbor.Row);
        }

        [Test]
        public void GetNeighbor_Top_ReturnsAdjacentChunk()
        {
            var grid = new ChunkGrid(10f, chunkSize: 5f);
            TerrainChunk chunk = grid.GetChunkAtGrid(1, 1);

            TerrainChunk neighbor = grid.GetNeighbor(chunk, ChunkGrid.ChunkNeighbor.Top);

            Assert.IsNotNull(neighbor);
            Assert.AreEqual(1, neighbor.Col);
            Assert.AreEqual(2, neighbor.Row);
        }

        [Test]
        public void GetNeighbor_Bottom_ReturnsAdjacentChunk()
        {
            var grid = new ChunkGrid(10f, chunkSize: 5f);
            TerrainChunk chunk = grid.GetChunkAtGrid(1, 2);

            TerrainChunk neighbor = grid.GetNeighbor(chunk, ChunkGrid.ChunkNeighbor.Bottom);

            Assert.IsNotNull(neighbor);
            Assert.AreEqual(1, neighbor.Col);
            Assert.AreEqual(1, neighbor.Row);
        }

        [Test]
        public void GetNeighbor_AtGridEdge_ReturnsNull()
        {
            var grid = new ChunkGrid(10f, chunkSize: 5f);

            // Top-left corner chunk: no Left or Bottom neighbor
            TerrainChunk corner = grid.GetChunkAtGrid(0, 0);
            Assert.IsNull(grid.GetNeighbor(corner, ChunkGrid.ChunkNeighbor.Left));
            Assert.IsNull(grid.GetNeighbor(corner, ChunkGrid.ChunkNeighbor.Bottom));

            // Right-edge chunk: no Right neighbor
            TerrainChunk rightEdge = grid.GetChunkAtGrid(grid.Cols - 1, 2);
            Assert.IsNull(grid.GetNeighbor(rightEdge, ChunkGrid.ChunkNeighbor.Right));

            // Top-edge chunk: no Top neighbor
            TerrainChunk topEdge = grid.GetChunkAtGrid(2, grid.Rows - 1);
            Assert.IsNull(grid.GetNeighbor(topEdge, ChunkGrid.ChunkNeighbor.Top));
        }

        [Test]
        public void GetNeighbor_NewChunk_AtEdge_ReturnsNull()
        {
            var grid = new ChunkGrid(5f, chunkSize: 5f);

            // Create a new chunk far outside the grid
            TerrainChunk newChunk = grid.GetChunkAt(new Vector2(100f, 100f));

            // Neighbors don't exist yet
            Assert.IsNull(grid.GetNeighbor(newChunk, ChunkGrid.ChunkNeighbor.Left));
            Assert.IsNull(grid.GetNeighbor(newChunk, ChunkGrid.ChunkNeighbor.Bottom));
        }

        [Test]
        public void ChunksInRect_SingleChunk()
        {
            var grid = new ChunkGrid(10f, chunkSize: 5f);
            var result = new List<int>();

            // Rect fully inside chunk at col=2, row=1 (X=[0,5], Y=[-5,0])
            grid.ChunksInRect(1f, 4f, -4f, -1f, result);

            Assert.AreEqual(1, result.Count);
            // Verify the returned chunk is at the expected position
            TerrainChunk chunk = grid.GetChunk(result[0]);
            Assert.AreEqual(2, chunk.Col);
            Assert.AreEqual(1, chunk.Row);
        }

        [Test]
        public void ChunksInRect_MultiChunk()
        {
            var grid = new ChunkGrid(10f, chunkSize: 5f);
            var result = new List<int>();

            // Rect spanning cols 1-3, rows 1-2
            grid.ChunksInRect(-4f, 6f, -9f, 1f, result);

            Assert.AreEqual(6, result.Count); // 3 cols x 2 rows
        }

        [Test]
        public void ChunksInRect_FullGrid()
        {
            var grid = new ChunkGrid(10f, chunkSize: 5f);
            var result = new List<int>();

            // Rect covering entire grid
            grid.ChunksInRect(-100f, 100f, -100f, 100f, result);

            Assert.AreEqual(grid.ChunkCount, result.Count);
        }

        [Test]
        public void ChunksInRect_PartialOverlap()
        {
            var grid = new ChunkGrid(10f, chunkSize: 5f);
            var result = new List<int>();

            // Rect that extends beyond grid bounds on one side
            grid.ChunksInRect(-100f, -3f, -100f, -3f, result);

            Assert.Greater(result.Count, 0);
            // All returned chunks should be on the left/bottom side
            foreach (int index in result)
            {
                TerrainChunk chunk = grid.GetChunk(index);
                Assert.Less(chunk.MaxX, -2f);
                Assert.Less(chunk.MaxY, -2f);
            }
        }

        [Test]
        public void ChunksInRect_OutOfBounds_CreatesChunks()
        {
            var grid = new ChunkGrid(5f, chunkSize: 5f);
            int initialCount = grid.ChunkCount;
            var result = new List<int>();

            // Rect far outside original grid
            grid.ChunksInRect(20f, 30f, 20f, 30f, result);

            Assert.Greater(result.Count, 0);
            Assert.Greater(grid.ChunkCount, initialCount);
            // All returned chunks should be outside the original grid
            foreach (int index in result)
            {
                TerrainChunk chunk = grid.GetChunk(index);
                Assert.True(chunk.Col >= grid.Cols || chunk.Row >= grid.Rows);
            }
        }

        [Test]
        public void AllChunks_AreDirtyInitially()
        {
            var grid = new ChunkGrid(10f, chunkSize: 5f);
            Assert.AreEqual(grid.ChunkCount, grid.DirtyChunks().Count());
        }

        [Test]
        public void ClearAllDirty_ClearsEveryChunk()
        {
            var grid = new ChunkGrid(10f, chunkSize: 5f);
            grid.ClearAllDirty();
            Assert.AreEqual(0, grid.DirtyChunks().Count());
        }

        [Test]
        public void MarkDirtyAt_OnlyMarksTargetedChunk()
        {
            var grid = new ChunkGrid(10f, chunkSize: 5f);
            grid.ClearAllDirty();

            grid.MarkDirtyAt(new Vector2(0f, 0f));

            var dirty = grid.DirtyChunks().ToList();
            Assert.AreEqual(1, dirty.Count);
        }

        [Test]
        public void GridChunks_AreContiguousNoGaps()
        {
            var grid = new ChunkGrid(10f, chunkSize: 5f);

            foreach (TerrainChunk chunk in grid.AllChunks)
            {
                // Chunk right edge should equal next chunk's left edge
                TerrainChunk rightNeighbor = grid.GetNeighbor(chunk, ChunkGrid.ChunkNeighbor.Right);
                if (rightNeighbor != null)
                    Assert.AreEqual(chunk.MaxX, rightNeighbor.MinX, 1e-5f);

                // Chunk top edge should equal row above's bottom edge
                TerrainChunk topNeighbor = grid.GetNeighbor(chunk, ChunkGrid.ChunkNeighbor.Top);
                if (topNeighbor != null)
                    Assert.AreEqual(chunk.MaxY, topNeighbor.MinY, 1e-5f);
            }
        }

        [Test]
        public void ChunkCount_IncludesDynamicChunks()
        {
            var grid = new ChunkGrid(5f, chunkSize: 5f);
            int initialCount = grid.ChunkCount;

            // Create a dynamic chunk
            grid.GetChunkAt(new Vector2(100f, 100f));

            Assert.AreEqual(initialCount + 1, grid.ChunkCount);
        }

        [Test]
        public void AllChunks_IncludesDynamicChunks()
        {
            var grid = new ChunkGrid(5f, chunkSize: 5f);

            // Create a dynamic chunk
            grid.GetChunkAt(new Vector2(100f, 100f));

            var allChunks = grid.AllChunks.ToList();
            Assert.AreEqual(grid.ChunkCount, allChunks.Count);

            // The dynamic chunk should be in AllChunks
            Assert.IsTrue(allChunks.Any(c => c.Col > grid.Cols - 1 || c.Row > grid.Rows - 1));
        }

        [Test]
        public void GetOrCreateChunkAtGrid_CreatesMissingChunk()
        {
            var grid = new ChunkGrid(5f, chunkSize: 5f);
            int initialCount = grid.ChunkCount;

            TerrainChunk chunk = grid.GetOrCreateChunkAtGrid(10, 10);

            Assert.IsNotNull(chunk);
            Assert.AreEqual(10, chunk.Col);
            Assert.AreEqual(10, chunk.Row);
            Assert.AreEqual(initialCount + 1, grid.ChunkCount);
        }

        [Test]
        public void GetOrCreateChunkAtGrid_ReturnsExistingChunk()
        {
            var grid = new ChunkGrid(10f, chunkSize: 5f);

            TerrainChunk chunk1 = grid.GetOrCreateChunkAtGrid(2, 2);
            TerrainChunk chunk2 = grid.GetOrCreateChunkAtGrid(2, 2);

            Assert.AreSame(chunk1, chunk2);
        }

        [Test]
        public void ChunksInRect_OutOfBounds_NoCreate_ReturnsEmpty()
        {
            var grid = new ChunkGrid(5f, chunkSize: 5f);
            var result = new List<int>();

            // Rect far outside original grid, but createChunks = false
            grid.ChunksInRect(20f, 30f, 20f, 30f, result, createChunks: false);

            Assert.AreEqual(0, result.Count);
            // Grid should be unchanged
            Assert.AreEqual(4, grid.ChunkCount);
        }

        [Test]
        public void ChunksInRect_PartialOutOfBounds_NoCreate_ReturnsExistingOnly()
        {
            var grid = new ChunkGrid(5f, chunkSize: 5f);
            var result = new List<int>();

            // Rect overlapping both existing and out-of-bounds regions
            grid.ChunksInRect(0f, 25f, 0f, 25f, result, createChunks: false);

            // Should return only existing chunks, not create new ones
            Assert.Greater(result.Count, 0);
            Assert.AreEqual(4, grid.ChunkCount); // no new chunks created
        }
    }
}
