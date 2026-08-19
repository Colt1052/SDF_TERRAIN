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

            // Chunk 0: col=0, row=0 -> minX=-5, maxX=0, minY=-5, maxY=0
            TerrainChunk chunk0 = grid.GetChunk(0);
            Assert.AreEqual(0, chunk0.Col);
            Assert.AreEqual(0, chunk0.Row);
            Assert.AreEqual(-5f, chunk0.MinX);
            Assert.AreEqual(0f, chunk0.MaxX);
            Assert.AreEqual(-5f, chunk0.MinY);
            Assert.AreEqual(0f, chunk0.MaxY);

            // Chunk 1: col=1, row=0 -> minX=0, maxX=5, minY=-5, maxY=0
            TerrainChunk chunk1 = grid.GetChunk(1);
            Assert.AreEqual(1, chunk1.Col);
            Assert.AreEqual(0, chunk1.Row);
            Assert.AreEqual(0f, chunk1.MinX);
            Assert.AreEqual(5f, chunk1.MaxX);
            Assert.AreEqual(-5f, chunk1.MinY);
            Assert.AreEqual(0f, chunk1.MaxY);

            // Chunk 2: col=0, row=1 -> minX=-5, maxX=0, minY=0, maxY=5
            TerrainChunk chunk2 = grid.GetChunk(2);
            Assert.AreEqual(0, chunk2.Col);
            Assert.AreEqual(1, chunk2.Row);
            Assert.AreEqual(-5f, chunk2.MinX);
            Assert.AreEqual(0f, chunk2.MaxX);
            Assert.AreEqual(0f, chunk2.MinY);
            Assert.AreEqual(5f, chunk2.MaxY);

            // Chunk 3: col=1, row=1 -> minX=0, maxX=5, minY=0, maxY=5
            TerrainChunk chunk3 = grid.GetChunk(3);
            Assert.AreEqual(1, chunk3.Col);
            Assert.AreEqual(1, chunk3.Row);
            Assert.AreEqual(0f, chunk3.MinX);
            Assert.AreEqual(5f, chunk3.MaxX);
            Assert.AreEqual(0f, chunk3.MinY);
            Assert.AreEqual(5f, chunk3.MaxY);
        }

        [Test]
        public void GetChunk_OutOfRange_Throws()
        {
            var grid = new ChunkGrid(10f, chunkSize: 5f);
            Assert.Throws<ArgumentOutOfRangeException>(() => grid.GetChunk(grid.ChunkCount));
            Assert.Throws<ArgumentOutOfRangeException>(() => grid.GetChunk(-1));
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

            // Position (3, 3) is in col=1, row=1 (chunk 3) for grid -10..+10
            TerrainChunk chunk = grid.GetChunkAt(new Vector2(3f, 3f));

            Assert.AreEqual(1, chunk.Col);
            Assert.AreEqual(1, chunk.Row);
            Assert.AreEqual(3, chunk.Index);
        }

        [Test]
        public void GetChunkAt_OutOfBounds_ClampToEdgeChunk()
        {
            var grid = new ChunkGrid(5f, chunkSize: 5f);

            // Far positive position -> clamped to bottom-right corner
            TerrainChunk chunk = grid.GetChunkAt(new Vector2(100f, 100f));

            Assert.AreEqual(grid.Cols - 1, chunk.Col);
            Assert.AreEqual(grid.Rows - 1, chunk.Row);

            // Far negative position -> clamped to top-left corner
            TerrainChunk chunkNeg = grid.GetChunkAt(new Vector2(-100f, -100f));

            Assert.AreEqual(0, chunkNeg.Col);
            Assert.AreEqual(0, chunkNeg.Row);
        }

        [Test]
        public void GetChunkAtGrid_ValidCoordinates_ReturnsChunk()
        {
            var grid = new ChunkGrid(10f, chunkSize: 5f);

            TerrainChunk chunk = grid.GetChunkAtGrid(2, 1);

            Assert.AreEqual(2, chunk.Col);
            Assert.AreEqual(1, chunk.Row);
            Assert.AreEqual(1 * 4 + 2, chunk.Index); // row * cols + col
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
        public void ChunksInRect_SingleChunk()
        {
            var grid = new ChunkGrid(10f, chunkSize: 5f);
            var result = new List<int>();

            // Rect fully inside chunk at col=2, row=1 (X=[0,5], Y=[-5,0])
            grid.ChunksInRect(1f, 4f, -4f, -1f, result);

            Assert.AreEqual(1, result.Count);
            Assert.AreEqual(6, result[0]); // row=1, col=2 -> 1*4+2 = 6
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

            for (int row = 0; row < grid.Rows; row++)
            {
                for (int col = 0; col < grid.Cols; col++)
                {
                    TerrainChunk chunk = grid.GetChunkAtGrid(col, row);

                    // Chunk right edge should equal next chunk's left edge
                    if (col < grid.Cols - 1)
                    {
                        TerrainChunk rightNeighbor = grid.GetChunkAtGrid(col + 1, row);
                        Assert.AreEqual(chunk.MaxX, rightNeighbor.MinX, 1e-5f);
                    }

                    // Chunk top edge should equal row above's bottom edge
                    if (row < grid.Rows - 1)
                    {
                        TerrainChunk topNeighbor = grid.GetChunkAtGrid(col, row + 1);
                        Assert.AreEqual(chunk.MaxY, topNeighbor.MinY, 1e-5f);
                    }
                }
            }
        }
    }
}
