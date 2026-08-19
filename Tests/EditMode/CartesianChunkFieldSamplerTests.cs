using System;
using NUnit.Framework;
using UnityEngine;
using SDFTerrain.Terrain;

namespace SDFTerrain.Tests
{
    public class CartesianChunkFieldSamplerTests
    {
        [Test]
        public void Sample_NullField_Throws()
        {
            var grid = new ChunkGrid(10f, chunkSize: 5f);
            Assert.Throws<ArgumentNullException>(() => CartesianChunkFieldSampler.Sample(null, grid.GetChunk(0), 1f));
        }

        [Test]
        public void Sample_NullChunk_Throws()
        {
            var field = new TerrainField(10f);
            Assert.Throws<ArgumentNullException>(() => CartesianChunkFieldSampler.Sample(field, null, 1f));
        }

        [Test]
        public void Sample_NonPositiveCellSize_Throws()
        {
            var field = new TerrainField(10f);
            var grid = new ChunkGrid(10f, chunkSize: 5f);
            Assert.Throws<ArgumentOutOfRangeException>(() => CartesianChunkFieldSampler.Sample(field, grid.GetChunk(0), 0f));
        }

        [Test]
        public void Sample_ProducesPositionsAndSamplesWithMatchingDimensions()
        {
            var field = new TerrainField(10f);
            var grid = new ChunkGrid(10f, chunkSize: 5f);

            CartesianChunkFieldSampler.Result result = CartesianChunkFieldSampler.Sample(field, grid.GetChunk(0), cellSize: 1f);

            Assert.Greater(result.Samples.GetLength(0), 0);
            Assert.Greater(result.Samples.GetLength(1), 0);
            Assert.AreEqual(result.Samples.GetLength(0), result.Positions.GetLength(0));
            Assert.AreEqual(result.Samples.GetLength(1), result.Positions.GetLength(1));
        }

        [Test]
        public void Sample_PositionsAreExactMultiplesOfCellSize()
        {
            var field = new TerrainField(10f);
            var grid = new ChunkGrid(10f, chunkSize: 5f);
            const float cellSize = 0.5f;

            CartesianChunkFieldSampler.Result result = CartesianChunkFieldSampler.Sample(field, grid.GetChunk(0), cellSize);

            int width = result.Positions.GetLength(0);
            int height = result.Positions.GetLength(1);

            for (int i = 0; i < width; i++)
            {
                for (int j = 0; j < height; j++)
                {
                    Vector2 p = result.Positions[i, j];
                    float xSteps = p.x / cellSize;
                    float ySteps = p.y / cellSize;

                    Assert.AreEqual(Mathf.Round(xSteps), xSteps, 1e-3f);
                    Assert.AreEqual(Mathf.Round(ySteps), ySteps, 1e-3f);
                }
            }
        }

        [Test]
        public void Sample_CenterIsSolid()
        {
            var field = new TerrainField(10f);
            var grid = new ChunkGrid(10f, chunkSize: 5f);
            const float cellSize = 1f;

            // Find the chunk that contains the origin
            TerrainChunk centerChunk = grid.GetChunkAt(Vector2.zero);

            CartesianChunkFieldSampler.Result result = CartesianChunkFieldSampler.Sample(field, centerChunk, cellSize);

            int width = result.Positions.GetLength(0);
            int height = result.Positions.GetLength(1);
            bool foundCenter = false;

            for (int i = 0; i < width; i++)
            {
                for (int j = 0; j < height; j++)
                {
                    if (result.Positions[i, j] == Vector2.zero)
                    {
                        foundCenter = true;
                        Assert.Less(result.Samples[i, j], 0f);
                    }
                }
            }

            Assert.IsTrue(foundCenter, "Expected the lattice to include the planet-local origin.");
        }

        [Test]
        public void Sample_AdjacentChunks_ShareBoundaryPoints()
        {
            var field = new TerrainField(10f);
            var grid = new ChunkGrid(10f, chunkSize: 5f);
            const float cellSize = 0.5f;

            // Get two horizontally adjacent chunks
            TerrainChunk chunkA = grid.GetChunkAtGrid(2, 2);
            TerrainChunk chunkB = grid.GetChunkAtGrid(3, 2);

            CartesianChunkFieldSampler.Result resultA = CartesianChunkFieldSampler.Sample(field, chunkA, cellSize);
            CartesianChunkFieldSampler.Result resultB = CartesianChunkFieldSampler.Sample(field, chunkB, cellSize);

            int widthA = resultA.Positions.GetLength(0);
            int heightA = resultA.Positions.GetLength(1);
            int widthB = resultB.Positions.GetLength(0);
            int heightB = resultB.Positions.GetLength(1);

            int sharedPointsChecked = 0;

            for (int i = 0; i < widthA; i++)
            {
                for (int j = 0; j < heightA; j++)
                {
                    Vector2 posA = resultA.Positions[i, j];

                    for (int bi = 0; bi < widthB; bi++)
                    {
                        for (int bj = 0; bj < heightB; bj++)
                        {
                            if (Vector2.Distance(posA, resultB.Positions[bi, bj]) < 1e-4f)
                            {
                                // With square chunks and no mask, both chunks sample the same
                                // field at the same position — values must be bit-identical.
                                Assert.AreEqual(resultA.Samples[i, j], resultB.Samples[bi, bj]);
                                sharedPointsChecked++;
                            }
                        }
                    }
                }
            }

            Assert.Greater(sharedPointsChecked, 0, "Expected adjacent chunks' lattices to overlap at the shared boundary.");
        }

        [Test]
        public void Sample_EdgeChunks_ProduceAirAtPlanetEdge()
        {
            var field = new TerrainField(10f);
            // Grid: radius 10, chunkSize 15 -> cols=2, rows=2, grid spans -30..+30
            var grid = new ChunkGrid(10f, chunkSize: 15f);
            const float cellSize = 5f;

            // Top-left chunk covers -30..-15 in X and -30..-15 in Y.
            // The planet (radius 10) doesn't reach anywhere near this chunk.
            // All samples should be positive (air).
            TerrainChunk edgeChunk = grid.GetChunkAtGrid(0, 0);

            CartesianChunkFieldSampler.Result result = CartesianChunkFieldSampler.Sample(field, edgeChunk, cellSize);

            int width = result.Samples.GetLength(0);
            int height = result.Samples.GetLength(1);

            for (int i = 0; i < width; i++)
            {
                for (int j = 0; j < height; j++)
                {
                    Assert.Greater(result.Samples[i, j], 0f,
                        $"Expected air at {result.Positions[i, j]} in edge chunk far from planet.");
                }
            }
        }

        [Test]
        public void Sample_ChunkCoversEntireBoundingBox()
        {
            var field = new TerrainField(10f);
            var grid = new ChunkGrid(10f, chunkSize: 5f);
            const float cellSize = 1f;

            TerrainChunk chunk = grid.GetChunk(0);
            CartesianChunkFieldSampler.Result result = CartesianChunkFieldSampler.Sample(field, chunk, cellSize);

            int width = result.Positions.GetLength(0);
            int height = result.Positions.GetLength(1);

            float minX = float.MaxValue, maxX = float.MinValue;
            float minY = float.MaxValue, maxY = float.MinValue;

            for (int i = 0; i < width; i++)
            {
                for (int j = 0; j < height; j++)
                {
                    Vector2 p = result.Positions[i, j];
                    minX = Mathf.Min(minX, p.x);
                    maxX = Mathf.Max(maxX, p.x);
                    minY = Mathf.Min(minY, p.y);
                    maxY = Mathf.Max(maxY, p.y);
                }
            }

            // Lattice should extend beyond the chunk bounds (1-cell margin)
            Assert.LessOrEqual(minX, chunk.MinX);
            Assert.GreaterOrEqual(maxX, chunk.MaxX);
            Assert.LessOrEqual(minY, chunk.MinY);
            Assert.GreaterOrEqual(maxY, chunk.MaxY);
        }
    }
}
