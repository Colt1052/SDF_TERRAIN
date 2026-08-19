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
            var grid = new ChunkGrid(4);
            Assert.Throws<ArgumentNullException>(() => CartesianChunkFieldSampler.Sample(null, grid.GetChunk(0), 15f, 1f));
        }

        [Test]
        public void Sample_NullChunk_Throws()
        {
            var field = new TerrainField(10f);
            Assert.Throws<ArgumentNullException>(() => CartesianChunkFieldSampler.Sample(field, null, 15f, 1f));
        }

        [Test]
        public void Sample_ProducesPositionsAndSamplesWithMatchingDimensions()
        {
            var field = new TerrainField(10f);
            var grid = new ChunkGrid(4);

            CartesianChunkFieldSampler.Result result = CartesianChunkFieldSampler.Sample(field, grid.GetChunk(0), 15f, cellSize: 1f);

            Assert.Greater(result.Samples.GetLength(0), 0);
            Assert.Greater(result.Samples.GetLength(1), 0);
            Assert.AreEqual(result.Samples.GetLength(0), result.Positions.GetLength(0));
            Assert.AreEqual(result.Samples.GetLength(1), result.Positions.GetLength(1));
        }

        [Test]
        public void Sample_PositionsAreExactMultiplesOfCellSize()
        {
            var field = new TerrainField(10f);
            var grid = new ChunkGrid(4);
            const float cellSize = 0.5f;

            CartesianChunkFieldSampler.Result result = CartesianChunkFieldSampler.Sample(field, grid.GetChunk(0), 15f, cellSize);

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
            var grid = new ChunkGrid(4);
            const float cellSize = 1f;

            CartesianChunkFieldSampler.Result result = CartesianChunkFieldSampler.Sample(field, grid.GetChunk(0), 15f, cellSize);

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
        public void Sample_PointOutsideWedge_ReadsAsAirEvenThoughTerrainIsSolidThere()
        {
            // Chunk 0 of a 4-chunk grid spans [0, PI/2). A point at angle PI (well outside that
            // wedge, but well inside the solid base sphere) should read positive (air) because it
            // is clipped away by the wedge mask, not because the raw terrain field is air there.
            var field = new TerrainField(10f);
            var grid = new ChunkGrid(4);
            TerrainChunk chunk = grid.GetChunk(0);

            Vector2 outsideWedgePoint = Core.RadialMath.PositionAt(Mathf.PI, 5f);
            float rawTerrainValue = field.Sample(outsideWedgePoint);
            Assert.Less(rawTerrainValue, 0f, "Precondition: this point should be solid ground in the raw field.");

            CartesianChunkFieldSampler.Result result = CartesianChunkFieldSampler.Sample(field, chunk, 15f, cellSize: 1f);

            int width = result.Positions.GetLength(0);
            int height = result.Positions.GetLength(1);
            bool found = false;

            for (int i = 0; i < width; i++)
            {
                for (int j = 0; j < height; j++)
                {
                    if (Vector2.Distance(result.Positions[i, j], outsideWedgePoint) < 1e-3f)
                    {
                        found = true;
                        Assert.Greater(result.Samples[i, j], 0f);
                    }
                }
            }

            // The bounding box for a quarter-circle wedge should not extend out to angle PI, so
            // this lattice point is not expected to even appear in chunk 0's grid.
            Assert.IsFalse(found, "A point diametrically outside the wedge should fall outside the chunk's lattice bounds entirely.");
        }

        [Test]
        public void Sample_AdjacentChunks_ShareIdenticalSamplesAtSharedLatticePoints()
        {
            var field = new TerrainField(10f);
            var grid = new ChunkGrid(4);
            var seamCache = new ChunkSeamCache(grid);
            TerrainChunk chunkA = grid.GetChunk(0);
            TerrainChunk chunkB = grid.GetChunk(1);
            const float cellSize = 0.5f;

            CartesianChunkFieldSampler.Result resultA = CartesianChunkFieldSampler.Sample(field, chunkA, 15f, cellSize, seamCache);
            CartesianChunkFieldSampler.Result resultB = CartesianChunkFieldSampler.Sample(field, chunkB, 15f, cellSize, seamCache);

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
                                // Exact (0-tolerance) equality: with the shared ChunkSeamCache
                                // feeding both chunks the same boundary direction values, the two
                                // sides' wedge-mask computations are bit-identical, not merely
                                // close within a float epsilon.
                                Assert.AreEqual(resultA.Samples[i, j], resultB.Samples[bi, bj]);
                                sharedPointsChecked++;
                            }
                        }
                    }
                }
            }

            Assert.Greater(sharedPointsChecked, 0, "Expected adjacent chunks' lattices to overlap at the shared boundary ray.");
        }

        [Test]
        public void Sample_AdjacentChunks_NearSeamPointsUseTerrainNotMask()
        {
            // A lattice point just inside chunk A's wedge but very close to the shared boundary
            // should use the raw terrain SDF (not the steep wedge mask), so that chunk B — which
            // considers the same point to be outside its wedge — sees the identical value.
            // Previously this point had a large positive mask value from one side and a negative
            // terrain value from the other, causing Marching Squares to place contour vertices at
            // different positions on the shared cell edge.
            var field = new TerrainField(10f);
            var grid = new ChunkGrid(4);
            var seamCache = new ChunkSeamCache(grid);
            TerrainChunk chunkA = grid.GetChunk(0); // [0, PI/2]
            TerrainChunk chunkB = grid.GetChunk(1); // [PI/2, PI]
            const float cellSize = 0.5f;

            CartesianChunkFieldSampler.Result resultA = CartesianChunkFieldSampler.Sample(field, chunkA, 15f, cellSize, seamCache);
            CartesianChunkFieldSampler.Result resultB = CartesianChunkFieldSampler.Sample(field, chunkB, 15f, cellSize, seamCache);

            // The boundary between chunks 0 and 1 is at PI/2 (positive y-axis).
            // Lattice point (0.5, 5) is on A's side of the boundary (angle < PI/2) but within
            // the seam margin (perpendicular distance = 0.5, margin = 2*cellSize = 1.0).
            // Both chunks should sample it with the raw terrain value.
            float rawTerrain = field.Sample(new Vector2(0.5f, 5f));

            int widthA = resultA.Samples.GetLength(0);
            int heightA = resultA.Samples.GetLength(1);

            for (int i = 0; i < widthA; i++)
            {
                for (int j = 0; j < heightA; j++)
                {
                    if (Mathf.Abs(resultA.Positions[i, j].x - 0.5f) < 1e-4f && Mathf.Abs(resultA.Positions[i, j].y - 5f) < 1e-4f)
                    {
                        // Should use terrain value, not a large positive mask value.
                        Assert.AreEqual(rawTerrain, resultA.Samples[i, j], 1e-6f,
                            "Chunk A near-seam point should use raw terrain SDF, not the wedge mask.");
                    }
                }
            }

            int widthB = resultB.Samples.GetLength(0);
            int heightB = resultB.Samples.GetLength(1);

            for (int i = 0; i < widthB; i++)
            {
                for (int j = 0; j < heightB; j++)
                {
                    if (Mathf.Abs(resultB.Positions[i, j].x - 0.5f) < 1e-4f && Mathf.Abs(resultB.Positions[i, j].y - 5f) < 1e-4f)
                    {
                        Assert.AreEqual(rawTerrain, resultB.Samples[i, j], 1e-6f,
                            "Chunk B near-seam point should use raw terrain SDF, not the wedge mask.");
                    }
                }
            }
        }

        [Test]
        public void Sample_FarFromSeam_StillClippedByWedgeMask()
        {
            // Lattice points far from the boundary should still be clipped by the wedge mask.
            // The seam margin only applies within 2 cells of the boundary ray.
            var field = new TerrainField(10f);
            var grid = new ChunkGrid(4);
            TerrainChunk chunk = grid.GetChunk(0); // [0, PI/2)
            const float cellSize = 0.5f;

            // No seam cache — tests that the wedge mask still works for non-seam points.
            CartesianChunkFieldSampler.Result result = CartesianChunkFieldSampler.Sample(field, chunk, 15f, cellSize);

            int width = result.Samples.GetLength(0);
            int height = result.Samples.GetLength(1);

            for (int i = 0; i < width; i++)
            {
                for (int j = 0; j < height; j++)
                {
                    Vector2 pos = result.Positions[i, j];

                    // Points well inside the wedge should have negative (solid) samples
                    // near the planet center.
                    if (pos.magnitude < 5f && pos.x > 1f && pos.y > 1f && pos.y < pos.x * 2f)
                    {
                        Assert.Less(result.Samples[i, j], 0f,
                            $"Point {pos} well inside wedge should be solid.");
                    }
                }
            }
        }
    }
}
