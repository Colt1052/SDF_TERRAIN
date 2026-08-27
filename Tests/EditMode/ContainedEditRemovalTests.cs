using System;
using NUnit.Framework;
using UnityEngine;
using SDFTerrain.Terrain;

namespace SDFTerrain.Tests
{
    /// <summary>
    /// Tests for <see cref="TerrainField.RemoveContainedEdits"/> — shape-agnostic
    /// bounding-box containment removal when a new edit is applied.
    /// </summary>
    public class ContainedEditRemovalTests
    {
        // ---- Circle containment ----

        [Test]
        public void Circle_ContainedCircleSameSign_Removed()
        {
            var field = new TerrainField(10f);
            var grid = new ChunkGrid(10f, chunkSize: 5f);
            field.EnableChunkIndexing(grid);

            // Small dig at center (radius 1).
            field.ApplyEdit(new TerrainEdit(Vector2.zero, radius: 1f, isAdditive: true, clamped: true));

            Assert.AreEqual(1, field.Edits.Count);

            // Large dig at center (radius 3) should subsume the small one.
            field.ApplyEdit(new TerrainEdit(Vector2.zero, radius: 3f, isAdditive: true, clamped: true));

            Assert.AreEqual(1, field.Edits.Count);
            Assert.AreEqual(3f, field.Edits[0].Radius);
        }

        [Test]
        public void Circle_ContainedCircleOppositeSign_Kept()
        {
            var field = new TerrainField(10f);
            var grid = new ChunkGrid(10f, chunkSize: 5f);
            field.EnableChunkIndexing(grid);

            // Small dig.
            field.ApplyEdit(new TerrainEdit(Vector2.zero, radius: 1f, isAdditive: true, clamped: true));

            // Large build (opposite sign) — should NOT remove the dig.
            field.ApplyEdit(new TerrainEdit(Vector2.zero, radius: 3f, isAdditive: false, clamped: true));

            Assert.AreEqual(2, field.Edits.Count);
        }

        [Test]
        public void Circle_ContainedCircleDifferentClamped_Kept()
        {
            var field = new TerrainField(10f);
            var grid = new ChunkGrid(10f, chunkSize: 5f);
            field.EnableChunkIndexing(grid);

            // Clamped dig.
            field.ApplyEdit(new TerrainEdit(Vector2.zero, radius: 1f, isAdditive: true, clamped: true));

            // Unclamped large dig — same sign but different clamped flag.
            field.ApplyEdit(new TerrainEdit(Vector2.zero, radius: 3f, isAdditive: true, clamped: false));

            // Different clamped flags prevent removal (conservative).
            Assert.AreEqual(2, field.Edits.Count);
        }

        [Test]
        public void Circle_OffsetContainedCircle_Removed()
        {
            var field = new TerrainField(10f);
            var grid = new ChunkGrid(20f, chunkSize: 5f);
            field.EnableChunkIndexing(grid);

            // Small dig offset from center.
            field.ApplyEdit(new TerrainEdit(new Vector2(1f, 1f), radius: 1f, isAdditive: true, clamped: true));

            Assert.AreEqual(1, field.Edits.Count);

            // Large dig at center whose bounding box contains the small one.
            // Large: center (0,0), radius 4 → bbox [-4,-4] to [4,4]
            // Small: center (1,1), radius 1 → bbox [0,0] to [2,2]
            field.ApplyEdit(new TerrainEdit(Vector2.zero, radius: 4f, isAdditive: true, clamped: true));

            Assert.AreEqual(1, field.Edits.Count);
        }

        // ---- Capsule containment ----

        [Test]
        public void Capsule_ContainedCapsuleSameSign_Removed()
        {
            var field = new TerrainField(10f);
            var grid = new ChunkGrid(20f, chunkSize: 5f);
            field.EnableChunkIndexing(grid);

            // Small capsule.
            var smallStart = new Vector2(1f, 0f);
            var smallEnd = new Vector2(3f, 0f);
            field.ApplyEdit(new TerrainEdit(smallStart, smallEnd, radius: 1f, isAdditive: true, BrushShape.Capsule) { Clamped = true });

            Assert.AreEqual(1, field.Edits.Count);

            // Large capsule whose bbox contains the small one.
            var largeStart = new Vector2(-5f, 0f);
            var largeEnd = new Vector2(5f, 0f);
            field.ApplyEdit(new TerrainEdit(largeStart, largeEnd, radius: 3f, isAdditive: true, BrushShape.Capsule) { Clamped = true });

            Assert.AreEqual(1, field.Edits.Count);
        }

        [Test]
        public void Capsule_ContainedCapsuleOppositeSign_Kept()
        {
            var field = new TerrainField(10f);
            var grid = new ChunkGrid(20f, chunkSize: 5f);
            field.EnableChunkIndexing(grid);

            var smallStart = new Vector2(1f, 0f);
            var smallEnd = new Vector2(3f, 0f);
            field.ApplyEdit(new TerrainEdit(smallStart, smallEnd, radius: 1f, isAdditive: true, BrushShape.Capsule) { Clamped = true });

            // Opposite sign — should not remove.
            var largeStart = new Vector2(-5f, 0f);
            var largeEnd = new Vector2(5f, 0f);
            field.ApplyEdit(new TerrainEdit(largeStart, largeEnd, radius: 3f, isAdditive: false, BrushShape.Capsule) { Clamped = true });

            Assert.AreEqual(2, field.Edits.Count);
        }

        // ---- Mixed shape containment ----

        [Test]
        public void MixedShape_CircleContainedInCapsule_Kept()
        {
            // A circle whose bbox is inside a capsule's bbox. Mixed shapes are
            // not removed because bbox containment does not imply shape dominance.
            var field = new TerrainField(10f);
            var grid = new ChunkGrid(20f, chunkSize: 5f);
            field.EnableChunkIndexing(grid);

            // Small circle at (2, 0), radius 1 → bbox [1,-1] to [3,1]
            field.ApplyEdit(new TerrainEdit(new Vector2(2f, 0f), radius: 1f, isAdditive: true, clamped: true));

            // Large capsule from (-5,0) to (5,0), radius 2 → bbox [-7,-2] to [7,2]
            // Capsule bbox contains circle bbox, but shapes differ.
            var capStart = new Vector2(-5f, 0f);
            var capEnd = new Vector2(5f, 0f);
            field.ApplyEdit(new TerrainEdit(capStart, capEnd, radius: 2f, isAdditive: true, BrushShape.Capsule) { Clamped = true });

            // Same shape check prevents removal for mixed shapes.
            Assert.AreEqual(2, field.Edits.Count);
        }

        [Test]
        public void MixedShape_CapsuleContainedInCircle_Kept()
        {
            var field = new TerrainField(10f);
            var grid = new ChunkGrid(20f, chunkSize: 5f);
            field.EnableChunkIndexing(grid);

            // Small capsule → bbox contained in large circle's bbox.
            var capStart = new Vector2(1f, 0f);
            var capEnd = new Vector2(2f, 0f);
            field.ApplyEdit(new TerrainEdit(capStart, capEnd, radius: 0.5f, isAdditive: true, BrushShape.Capsule) { Clamped = true });

            // Large circle at center, radius 5 → bbox [-5,-5] to [5,5]
            field.ApplyEdit(new TerrainEdit(Vector2.zero, radius: 5f, isAdditive: true, clamped: true));

            Assert.AreEqual(2, field.Edits.Count);
        }

        // ---- Multiple containment ----

        [Test]
        public void MultipleContainedEdits_AllRemoved()
        {
            var field = new TerrainField(10f);
            var grid = new ChunkGrid(20f, chunkSize: 5f);
            field.EnableChunkIndexing(grid);

            // Several small digs within a large area.
            field.ApplyEdit(new TerrainEdit(new Vector2(1f, 0f), radius: 0.5f, isAdditive: true, clamped: true));
            field.ApplyEdit(new TerrainEdit(new Vector2(-1f, 0f), radius: 0.5f, isAdditive: true, clamped: true));
            field.ApplyEdit(new TerrainEdit(new Vector2(0f, 1f), radius: 0.5f, isAdditive: true, clamped: true));

            Assert.AreEqual(3, field.Edits.Count);

            // Large dig subsumes all three.
            field.ApplyEdit(new TerrainEdit(Vector2.zero, radius: 3f, isAdditive: true, clamped: true));

            Assert.AreEqual(1, field.Edits.Count);
        }

        [Test]
        public void PartiallyContained_Kept()
        {
            // An edit whose bbox partially overlaps the new edit should NOT be removed.
            var field = new TerrainField(10f);
            var grid = new ChunkGrid(20f, chunkSize: 5f);
            field.EnableChunkIndexing(grid);

            // Circle at (3, 0), radius 2 → bbox [1,-2] to [5,2]
            field.ApplyEdit(new TerrainEdit(new Vector2(3f, 0f), radius: 2f, isAdditive: true, clamped: true));

            // Circle at (0, 0), radius 2 → bbox [-2,-2] to [2,2]
            // Overlaps but does not fully contain.
            field.ApplyEdit(new TerrainEdit(Vector2.zero, radius: 2f, isAdditive: true, clamped: true));

            Assert.AreEqual(2, field.Edits.Count);
        }

        // ---- Chunk indexing integrity ----

        [Test]
        public void AfterRemoval_ChunkIndexedSamplingMatches()
        {
            var field = new TerrainField(10f);
            var grid = new ChunkGrid(10f, chunkSize: 5f);
            field.EnableChunkIndexing(grid);

            field.ApplyEdit(new TerrainEdit(Vector2.zero, radius: 1f, isAdditive: false, clamped: true));

            // Large build subsumes the small one.
            field.ApplyEdit(new TerrainEdit(Vector2.zero, radius: 3f, isAdditive: false, clamped: true));

            Assert.AreEqual(1, field.Edits.Count);

            // Chunk-indexed sampling should match full-scan sampling.
            foreach (TerrainChunk chunk in grid.AllChunks)
            {
                Vector2 sample = new Vector2(
                    (chunk.MinX + chunk.MaxX) * 0.5f,
                    (chunk.MinY + chunk.MaxY) * 0.5f);

                float expected = field.Sample(sample);
                float actual = field.Sample(sample, chunk.Index);

                Assert.AreEqual(expected, actual, 1e-5f,
                    $"Chunk-indexed mismatch at chunk {chunk.Index} after containment removal");
            }
        }

        [Test]
        public void WithoutChunkIndexing_RemovalStillWorks()
        {
            var field = new TerrainField(10f);

            // No chunk indexing — removal should still work, just without index updates.
            field.ApplyEdit(new TerrainEdit(Vector2.zero, radius: 1f, isAdditive: true, clamped: true));
            field.ApplyEdit(new TerrainEdit(Vector2.zero, radius: 3f, isAdditive: true, clamped: true));

            Assert.AreEqual(1, field.Edits.Count);
            Assert.AreEqual(3f, field.Edits[0].Radius);
        }

        // ---- Direct API ----

        [Test]
        public void RemoveContainedEdits_ReturnsCount()
        {
            var field = new TerrainField(10f);
            var grid = new ChunkGrid(20f, chunkSize: 5f);
            field.EnableChunkIndexing(grid);

            field.ApplyEdit(new TerrainEdit(new Vector2(1f, 0f), radius: 0.5f, isAdditive: true, clamped: true));
            field.ApplyEdit(new TerrainEdit(new Vector2(-1f, 0f), radius: 0.5f, isAdditive: true, clamped: true));

            Assert.AreEqual(2, field.Edits.Count);

            var largeEdit = new TerrainEdit(Vector2.zero, radius: 3f, isAdditive: true, clamped: true);
            int removed = field.RemoveContainedEdits(largeEdit);

            Assert.AreEqual(2, removed);
            Assert.AreEqual(0, field.Edits.Count);
        }

        [Test]
        public void RemoveContainedEdits_NothingToRemove_ReturnsZero()
        {
            var field = new TerrainField(10f);

            field.ApplyEdit(new TerrainEdit(Vector2.zero, radius: 5f, isAdditive: true, clamped: true));

            // Smaller edit cannot contain the existing large edit.
            var smallEdit = new TerrainEdit(Vector2.zero, radius: 2f, isAdditive: true, clamped: true);
            int removed = field.RemoveContainedEdits(smallEdit);

            Assert.AreEqual(0, removed);
            Assert.AreEqual(1, field.Edits.Count);
        }

        // ---- Build (non-additive) edits ----

        [Test]
        public void Build_ContainedBuildSameSign_Removed()
        {
            var field = new TerrainField(10f);
            var grid = new ChunkGrid(20f, chunkSize: 5f);
            field.EnableChunkIndexing(grid);

            // Small build.
            field.ApplyEdit(new TerrainEdit(Vector2.zero, radius: 1f, isAdditive: false, clamped: true));

            // Large build (same sign = non-additive) should subsume.
            field.ApplyEdit(new TerrainEdit(Vector2.zero, radius: 3f, isAdditive: false, clamped: true));

            Assert.AreEqual(1, field.Edits.Count);
            Assert.AreEqual(3f, field.Edits[0].Radius);
        }

        // ---- Edge cases ----

        [Test]
        public void EqualRadiusSameCenter_Removed()
        {
            // Two identical edits — the second subsumes the first.
            var field = new TerrainField(10f);

            field.ApplyEdit(new TerrainEdit(Vector2.zero, radius: 2f, isAdditive: true, clamped: true));
            field.ApplyEdit(new TerrainEdit(Vector2.zero, radius: 2f, isAdditive: true, clamped: true));

            Assert.AreEqual(1, field.Edits.Count);
        }

        [Test]
        public void DegenerateCapsule_TreatedAsCircle()
        {
            // A capsule with start == end is a degenerate capsule (circle-like).
            // Its bbox is the same as a circle at that position.
            var field = new TerrainField(10f);
            var grid = new ChunkGrid(20f, chunkSize: 5f);
            field.EnableChunkIndexing(grid);

            // Degenerate capsule at center, radius 1 → bbox [-1,-1] to [1,1]
            var degStart = Vector2.zero;
            var degEnd = Vector2.zero;
            field.ApplyEdit(new TerrainEdit(degStart, degEnd, radius: 1f, isAdditive: true, BrushShape.Capsule) { Clamped = true });

            // Large circle at center, radius 3 → bbox [-3,-3] to [3,3]
            // Contains the degenerate capsule's bbox, but different shapes → kept.
            field.ApplyEdit(new TerrainEdit(Vector2.zero, radius: 3f, isAdditive: true, clamped: true));

            Assert.AreEqual(2, field.Edits.Count);
        }
    }
}
