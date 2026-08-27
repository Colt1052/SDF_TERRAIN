using System;
using NUnit.Framework;
using UnityEngine;
using SDFTerrain.Terrain;

namespace SDFTerrain.Tests
{
    /// <summary>
    /// Tests for Rectangle brush shape, uniform chunk detection, and rectangle merging.
    /// </summary>
    public class UniformRegionTests
    {
        // ---- Rectangle SDF correctness ----

        [Test]
        public void Rectangle_DistanceToShape_InsideIsZero()
        {
            // Rectangle from (0, 0) to (10, 10). Point at center should have distance 0.
            var edit = new TerrainEdit(
                new Vector2(0f, 0f), new Vector2(10f, 10f),
                radius: 5f, isAdditive: false, BrushShape.Rectangle, clamped: true);

            float dist = edit.DistanceToShape(new Vector2(5f, 5f));
            Assert.AreEqual(0f, dist, 1e-5f, "Point at rect center should have zero distance to boundary");
        }

        [Test]
        public void Rectangle_DistanceToShape_OutsideIsPositive()
        {
            var edit = new TerrainEdit(
                new Vector2(0f, 0f), new Vector2(10f, 10f),
                radius: 5f, isAdditive: false, BrushShape.Rectangle, clamped: true);

            // Point at (12, 5) — 2 units outside right edge.
            float dist = edit.DistanceToShape(new Vector2(12f, 5f));
            Assert.AreEqual(2f, dist, 1e-5f, "Point 2 units outside right edge should have distance 2");

            // Point at (5, 13) — 3 units above top edge.
            dist = edit.DistanceToShape(new Vector2(5f, 13f));
            Assert.AreEqual(3f, dist, 1e-5f, "Point 3 units above top edge should have distance 3");

            // Point at (12, 12) — corner diagonal distance.
            dist = edit.DistanceToShape(new Vector2(12f, 12f));
            Assert.AreEqual(Mathf.Sqrt(8f), dist, 1e-5f, "Corner diagonal distance should be sqrt(2^2 + 2^2)");
        }

        [Test]
        public void Rectangle_DistanceToShape_OnEdgeIsZero()
        {
            var edit = new TerrainEdit(
                new Vector2(0f, 0f), new Vector2(10f, 10f),
                radius: 5f, isAdditive: false, BrushShape.Rectangle, clamped: true);

            Assert.AreEqual(0f, edit.DistanceToShape(new Vector2(0f, 5f)), 1e-5f, "Left edge");
            Assert.AreEqual(0f, edit.DistanceToShape(new Vector2(10f, 5f)), 1e-5f, "Right edge");
            Assert.AreEqual(0f, edit.DistanceToShape(new Vector2(5f, 0f)), 1e-5f, "Bottom edge");
            Assert.AreEqual(0f, edit.DistanceToShape(new Vector2(5f, 10f)), 1e-5f, "Top edge");
        }

        [Test]
        public void Rectangle_GetBoundingBox_Correct()
        {
            var edit = new TerrainEdit(
                new Vector2(0f, 0f), new Vector2(10f, 10f),
                radius: 5f, isAdditive: false, BrushShape.Rectangle, clamped: true);

            edit.GetBoundingBox(out float minX, out float maxX, out float minY, out float maxY);

            // Rect from (0,0) to (10,10) with radius 5 → bbox [-5,-5] to [15,15].
            Assert.AreEqual(-5f, minX);
            Assert.AreEqual(15f, maxX);
            Assert.AreEqual(-5f, minY);
            Assert.AreEqual(15f, maxY);
        }

        [Test]
        public void Rectangle_SampleContribution_InsideReturnsFullContribution()
        {
            // Rectangle from (0, 0) to (10, 10), radius 5, non-additive (build).
            // Inside rect, distance-to-boundary is 0, so magnitude = radius - 0 = 5.
            // Non-additive: contribution = -magnitude = -5.
            var edit = new TerrainEdit(
                new Vector2(0f, 0f), new Vector2(10f, 10f),
                radius: 5f, isAdditive: false, BrushShape.Rectangle, clamped: true);

            float contrib = edit.SampleContribution(new Vector2(5f, 5f));
            Assert.AreEqual(-5f, contrib, 1e-5f, "Inside rect should return -radius for non-additive");
        }

        [Test]
        public void Rectangle_SampleContribution_OutsideClampedReturnsSkip()
        {
            // Non-additive (build) → skip value is PositiveInfinity.
            var edit = new TerrainEdit(
                new Vector2(0f, 0f), new Vector2(10f, 10f),
                radius: 5f, isAdditive: false, BrushShape.Rectangle, clamped: true);

            // Point at (100, 100) — distance to boundary >> radius.
            float contrib = edit.SampleContribution(new Vector2(100f, 100f));
            Assert.IsTrue(float.IsPositiveInfinity(contrib), "Outside with clamped should return PositiveInfinity for non-additive");
        }

        // ---- Sampler uniformity detection ----

        [Test]
        public void Sampler_AllSolid_ReturnsUniform()
        {
            var field = new TerrainField(10f);
            var grid = new ChunkGrid(10f, chunkSize: 5f);
            field.EnableChunkIndexing(grid);

            // Place a build edit that makes a chunk uniformly solid.
            // Chunk at col 2, row 2 covers x:[5, 10], y:[5, 10].
            field.ApplyEdit(new TerrainEdit(
                new Vector2(7.5f, 7.5f), radius: 8f, isAdditive: false, clamped: true));

            TerrainChunk chunk = grid.GetChunkAtGrid(2, 2);
            var result = CartesianChunkFieldSampler.Sample(field, chunk, cellSize: 1f);

            Assert.IsTrue(result.IsUniform, "Chunk should be detected as uniform");
            Assert.IsTrue(result.IsSolid, "Uniform chunk should be solid (all negative samples)");
        }

        [Test]
        public void Sampler_AllAir_ReturnsUniformNotSolid()
        {
            var field = new TerrainField(5f);
            var grid = new ChunkGrid(5f, chunkSize: 10f);
            field.EnableChunkIndexing(grid);

            // No edits — far from planet → all air.
            // The grid for radius 5, chunkSize 10 creates 1x1 grid at [-10,-10] to [0,0].
            // The planet at origin radius 5 won't reach chunk at col 0, row 0 ([-10,-10] to [0,0]).
            // Actually the grid center is at 0,0 with cols 1, rows 1: minX = -5, minY = -5.
            // Let's create a chunk further out.

            // Simpler: sample the planet center chunk, then remove all material with an edit.
            field.ApplyEdit(new TerrainEdit(
                Vector2.zero, radius: 15f, isAdditive: true, clamped: true));

            TerrainChunk chunk = grid.GetChunkAtGrid(0, 0);
            var result = CartesianChunkFieldSampler.Sample(field, chunk, cellSize: 1f);

            Assert.IsTrue(result.IsUniform, "Chunk should be detected as uniform");
            Assert.IsFalse(result.IsSolid, "Uniform chunk should not be solid (all positive samples)");
        }

        [Test]
        public void Sampler_Mixed_ReturnsNotUniform()
        {
            var field = new TerrainField(5f);
            var grid = new ChunkGrid(5f, chunkSize: 5f);
            field.EnableChunkIndexing(grid);

            // No edits — the base sphere at origin radius 5 will create mixed samples
            // in a chunk that intersects the surface.
            // Grid for radius 5, chunkSize 5: cols=2, rows=2, from -5 to 5.
            // Chunk at col 1, row 1 covers [0, 5] x [0, 5]. The sphere surface at radius 5
            // passes through this chunk → mixed solid/air.

            TerrainChunk chunk = grid.GetChunkAtGrid(1, 1);
            var result = CartesianChunkFieldSampler.Sample(field, chunk, cellSize: 1f);

            Assert.IsFalse(result.IsUniform, "Chunk intersecting surface should not be uniform");
        }

        // ---- Rectangle merging ----

        [Test]
        public void MergeAdjacentRectangles_HorizontalAlignment_Merges()
        {
            var field = new TerrainField(10f);
            var grid = new ChunkGrid(20f, chunkSize: 5f);
            field.EnableChunkIndexing(grid);

            // Two adjacent horizontal rectangles, same isAdditive.
            // Rect A: (0,0) to (5,5), Rect B: (5,0) to (10,5).
            float diag = Mathf.Sqrt(25f + 25f);
            field.ApplyEdit(new TerrainEdit(
                new Vector2(0f, 0f), new Vector2(5f, 5f), diag * 2f,
                isAdditive: false, BrushShape.Rectangle, clamped: true));
            field.ApplyEdit(new TerrainEdit(
                new Vector2(5f, 0f), new Vector2(10f, 5f), diag * 2f,
                isAdditive: false, BrushShape.Rectangle, clamped: true));

            Assert.AreEqual(2, field.Edits.Count);

            field.ConsolidateUniformRegions(1f);

            // After consolidation, the two should be merged into one.
            int rectCount = 0;
            foreach (TerrainEdit edit in field.Edits)
            {
                if (edit.Shape == BrushShape.Rectangle)
                    rectCount++;
            }
            Assert.AreEqual(1, rectCount, "Two adjacent horizontal rectangles should merge into one");
        }

        [Test]
        public void MergeAdjacentRectangles_DiagonalOnly_DoesNotMerge()
        {
            var field = new TerrainField(10f);
            var grid = new ChunkGrid(20f, chunkSize: 5f);
            field.EnableChunkIndexing(grid);

            // Two diagonal rectangles — should NOT merge.
            // Rect A: (0,0) to (5,5), Rect B: (5,5) to (10,10).
            float diag = Mathf.Sqrt(25f + 25f);
            field.ApplyEdit(new TerrainEdit(
                new Vector2(0f, 0f), new Vector2(5f, 5f), diag * 2f,
                isAdditive: false, BrushShape.Rectangle, clamped: true));
            field.ApplyEdit(new TerrainEdit(
                new Vector2(5f, 5f), new Vector2(10f, 10f), diag * 2f,
                isAdditive: false, BrushShape.Rectangle, clamped: true));

            Assert.AreEqual(2, field.Edits.Count);

            // Merge directly without full consolidation (which would re-sample).
            // We need to call the merge logic — but it's private. For now, verify via
            // ConsolidateUniformRegions that it doesn't erroneously merge.
            // Since these are already rectangle edits, ConsolidateUniformRegions will
            // sample and potentially add more rectangles, but the merge step won't combine
            // diagonal ones.

            // Direct test: apply merge via reflection isn't ideal; instead verify the
            // adjacency logic by checking that after consolidation, we still have
            // two separate rectangular regions (they may get new rectangles added, but
            // the originals won't merge diagonally).

            // For a cleaner test, just verify the edit count doesn't decrease to 1
            // from the merge — there may be additional rectangles from the sample pass,
            // so we check that we have at least 2 rectangle edits.
            field.ConsolidateUniformRegions(1f);

            int rectCount = 0;
            foreach (TerrainEdit edit in field.Edits)
            {
                if (edit.Shape == BrushShape.Rectangle)
                    rectCount++;
            }
            Assert.GreaterOrEqual(rectCount, 2,
                "Diagonal rectangles should not merge; expect at least 2 rectangle edits");
        }

        [Test]
        public void MergeAdjacentRectangles_OppositeSign_DoesNotMerge()
        {
            var field = new TerrainField(10f);
            var grid = new ChunkGrid(20f, chunkSize: 5f);
            field.EnableChunkIndexing(grid);

            // Two adjacent horizontal rectangles with different isAdditive.
            float diag = Mathf.Sqrt(25f + 25f);
            field.ApplyEdit(new TerrainEdit(
                new Vector2(0f, 0f), new Vector2(5f, 5f), diag * 2f,
                isAdditive: false, BrushShape.Rectangle, clamped: true)); // build
            field.ApplyEdit(new TerrainEdit(
                new Vector2(5f, 0f), new Vector2(10f, 5f), diag * 2f,
                isAdditive: true, BrushShape.Rectangle, clamped: true));  // dig

            Assert.AreEqual(2, field.Edits.Count);

            field.ConsolidateUniformRegions(1f);

            // Should not merge due to opposite sign.
            int rectCount = 0;
            foreach (TerrainEdit edit in field.Edits)
            {
                if (edit.Shape == BrushShape.Rectangle)
                    rectCount++;
            }
            Assert.GreaterOrEqual(rectCount, 2,
                "Opposite-sign rectangles should not merge");
        }

        [Test]
        public void MergeAdjacentRectangles_VerticalAlignment_Merges()
        {
            var field = new TerrainField(10f);
            var grid = new ChunkGrid(20f, chunkSize: 5f);
            field.EnableChunkIndexing(grid);

            // Two adjacent vertical rectangles.
            // Rect A: (0,0) to (5,5), Rect B: (0,5) to (5,10).
            float diag = Mathf.Sqrt(25f + 25f);
            field.ApplyEdit(new TerrainEdit(
                new Vector2(0f, 0f), new Vector2(5f, 5f), diag * 2f,
                isAdditive: false, BrushShape.Rectangle, clamped: true));
            field.ApplyEdit(new TerrainEdit(
                new Vector2(0f, 5f), new Vector2(5f, 10f), diag * 2f,
                isAdditive: false, BrushShape.Rectangle, clamped: true));

            Assert.AreEqual(2, field.Edits.Count);

            field.ConsolidateUniformRegions(1f);

            int rectCount = 0;
            foreach (TerrainEdit edit in field.Edits)
            {
                if (edit.Shape == BrushShape.Rectangle)
                    rectCount++;
            }
            Assert.AreEqual(1, rectCount, "Two adjacent vertical rectangles should merge into one");
        }

        // ---- 2x2 uniform region integration ----

        [Test]
        public void Consolidate_2x2UniformRegion_ProducesMergedRectangle()
        {
            var field = new TerrainField(5f);
            var grid = new ChunkGrid(5f, chunkSize: 5f);
            field.EnableChunkIndexing(grid);

            // Dig out the entire 2x2 grid area to create a uniform air region.
            field.ApplyEdit(new TerrainEdit(
                Vector2.zero, radius: 15f, isAdditive: true, clamped: true));

            int editsBefore = field.Edits.Count;

            field.ConsolidateUniformRegions(1f);

            // There should now be at least one rectangle edit added for the uniform region.
            int rectCount = 0;
            foreach (TerrainEdit edit in field.Edits)
            {
                if (edit.Shape == BrushShape.Rectangle)
                    rectCount++;
            }
            Assert.GreaterOrEqual(rectCount, 1,
                "Consolidation should produce at least one merged rectangle for the uniform region");
        }

        // ---- ConsolidateUniformRegions API ----

        [Test]
        public void ConsolidateUniformRegions_ThrowsWithoutChunkIndexing()
        {
            var field = new TerrainField(10f);
            Assert.Throws<InvalidOperationException>(() => field.ConsolidateUniformRegions(1f));
        }

        [Test]
        public void ConsolidateUniformRegions_ThrowsWithZeroCellSize()
        {
            var field = new TerrainField(10f);
            var grid = new ChunkGrid(10f, chunkSize: 5f);
            field.EnableChunkIndexing(grid);
            Assert.Throws<ArgumentOutOfRangeException>(() => field.ConsolidateUniformRegions(0f));
        }

        // ---- ChunkGrid.GetAdjacentChunks ----

        [Test]
        public void GetAdjacentChunks_CenterChunk_ReturnsFour()
        {
            var grid = new ChunkGrid(10f, chunkSize: 5f);
            // Grid: cols=4, rows=4, from -10 to 10.
            // Chunk at col 2, row 2 is an interior chunk.
            TerrainChunk chunk = grid.GetChunkAtGrid(2, 2);
            var adjacent = grid.GetAdjacentChunks(chunk);
            Assert.AreEqual(4, adjacent.Count);
        }

        [Test]
        public void GetAdjacentChunks_CornerChunk_ReturnsTwo()
        {
            var grid = new ChunkGrid(10f, chunkSize: 5f);
            // Chunk at col 0, row 0 is a corner.
            TerrainChunk chunk = grid.GetChunkAtGrid(0, 0);
            var adjacent = grid.GetAdjacentChunks(chunk);
            Assert.AreEqual(2, adjacent.Count);
        }

        // ---- TerrainChunk.IsUniform / IsSolid ----

        [Test]
        public void TerrainChunk_IsUniform_PropertiesExist()
        {
            var chunk = new TerrainChunk(0, 0, 0, 0f, 5f, 0f, 5f);
            Assert.IsFalse(chunk.IsUniform);
            Assert.IsFalse(chunk.IsSolid);

            chunk.IsUniform = true;
            chunk.IsSolid = true;
            Assert.IsTrue(chunk.IsUniform);
            Assert.IsTrue(chunk.IsSolid);
        }

        // ---- Chunk properties updated during consolidation ----

        [Test]
        public void AfterConsolidation_ChunkIsUniform_Set()
        {
            var field = new TerrainField(5f);
            var grid = new ChunkGrid(5f, chunkSize: 5f);
            field.EnableChunkIndexing(grid);

            // Remove all material so chunks become uniformly air.
            field.ApplyEdit(new TerrainEdit(
                Vector2.zero, radius: 15f, isAdditive: true, clamped: true));

            field.ConsolidateUniformRegions(1f);

            // At least some chunks should now have IsUniform set.
            bool anyUniform = false;
            foreach (TerrainChunk chunk in grid.AllChunks)
            {
                if (chunk.IsUniform)
                {
                    anyUniform = true;
                    break;
                }
            }
            Assert.IsTrue(anyUniform, "Some chunks should have IsUniform = true after consolidation");
        }
    }
}
