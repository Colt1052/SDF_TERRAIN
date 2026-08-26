using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using SDFTerrain.Core;
using SDFTerrain.Terrain;

namespace SDFTerrain.Tests
{
    public class TerrainFieldTests
    {
        [Test]
        public void Constructor_NonPositiveRadius_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new TerrainField(0f));
        }

        [Test]
        public void Sample_AtCenter_IsNegativeRadius()
        {
            var field = new TerrainField(10f);
            Assert.AreEqual(-10f, field.Sample(Vector2.zero), 1e-5f);
        }

        [Test]
        public void Sample_AtSurface_IsZero()
        {
            var field = new TerrainField(10f);
            Assert.AreEqual(0f, field.Sample(new Vector2(10f, 0f)), 1e-5f);
        }

        [Test]
        public void Sample_OutsideSurface_IsPositive()
        {
            var field = new TerrainField(10f);
            Assert.Greater(field.Sample(new Vector2(15f, 0f)), 0f);
        }

        [Test]
        public void Sample_InsideSurface_IsNegative()
        {
            var field = new TerrainField(10f);
            Assert.Less(field.Sample(new Vector2(5f, 0f)), 0f);
        }

        [Test]
        public void ApplyEdit_AdditiveDig_IncreasesDistanceAtCenter()
        {
            var field = new TerrainField(10f);
            Vector2 brushCenter = new Vector2(10f, 0f);
            float before = field.Sample(brushCenter);

            field.ApplyEdit(new TerrainEdit(brushCenter, radius: 3f, isAdditive: true));

            Assert.Greater(field.Sample(brushCenter), before);
        }

        [Test]
        public void ApplyEdit_SubtractiveBuild_DecreasesDistanceAtCenter()
        {
            var field = new TerrainField(10f);
            Vector2 brushCenter = new Vector2(10f, 0f);
            float before = field.Sample(brushCenter);

            field.ApplyEdit(new TerrainEdit(brushCenter, radius: 3f, isAdditive: false));

            Assert.Less(field.Sample(brushCenter), before);
        }

        [Test]
        public void ApplyEdit_FarOutsideBrushRadius_HasNoEffect()
        {
            var field = new TerrainField(10f);
            float before = field.Sample(new Vector2(10f, 0f));

            field.ApplyEdit(new TerrainEdit(new Vector2(-10f, 0f), radius: 1f, isAdditive: true));

            Assert.AreEqual(before, field.Sample(new Vector2(10f, 0f)), 1e-5f);
        }

        [Test]
        public void ApplyEdit_AtBrushBoundary_ContributesZero()
        {
            var field = new TerrainField(10f);
            Vector2 brushCenter = new Vector2(10f, 0f);

            field.ApplyEdit(new TerrainEdit(brushCenter, radius: 3f, isAdditive: true));

            float sampleAtBoundary = field.Sample(brushCenter + new Vector2(3f, 0f));
            float baseAtBoundary = (brushCenter + new Vector2(3f, 0f)).magnitude - 10f;
            Assert.AreEqual(baseAtBoundary, sampleAtBoundary, 1e-4f);
        }

        [Test]
        public void ClearEdits_RemovesAllModifications()
        {
            var field = new TerrainField(10f);
            Vector2 brushCenter = new Vector2(10f, 0f);
            float baseline = field.Sample(brushCenter);
            field.ApplyEdit(new TerrainEdit(brushCenter, radius: 3f, isAdditive: true));

            field.ClearEdits();

            Assert.AreEqual(baseline, field.Sample(brushCenter), 1e-5f);
            Assert.AreEqual(0, field.Edits.Count);
        }

        [Test]
        public void LoadEdits_ReplacesExistingEdits()
        {
            var field = new TerrainField(10f);
            field.ApplyEdit(new TerrainEdit(Vector2.zero, 1f, true));

            var replacement = new List<TerrainEdit>
            {
                new TerrainEdit(new Vector2(1f, 1f), 2f, false),
                new TerrainEdit(new Vector2(2f, 2f), 3f, true),
            };
            field.LoadEdits(replacement);

            Assert.AreEqual(2, field.Edits.Count);
            Assert.AreEqual(replacement[0].LocalPosition, field.Edits[0].LocalPosition);
        }

        [Test]
        public void LoadEdits_Null_Throws()
        {
            var field = new TerrainField(10f);
            Assert.Throws<ArgumentNullException>(() => field.LoadEdits(null));
        }

        [Test]
        public void DistinctNonOverlappingDigEdits_BothApply()
        {
            var field = new TerrainField(10f);
            Vector2 pointA = new Vector2(10f, 0f);
            Vector2 pointB = new Vector2(-10f, 0f);
            float baselineA = field.Sample(pointA);
            float baselineB = field.Sample(pointB);

            field.ApplyEdit(new TerrainEdit(pointA, radius: 1f, isAdditive: true));
            field.ApplyEdit(new TerrainEdit(pointB, radius: 1f, isAdditive: true));

            Assert.Greater(field.Sample(pointA), baselineA);
            Assert.Greater(field.Sample(pointB), baselineB);
        }

        [Test]
        public void OverlappingDigEdits_AtSamePosition_AreIdempotentNotAdditive()
        {
            // Repeatedly digging the same spot (e.g. holding the brush still) should not keep
            // carving deeper — it's a CSG-style carve, not a sum of contributions. This is what
            // gives a "solid eraser" feel instead of terrain melting away the longer a stroke
            // overlaps itself.
            var field = new TerrainField(10f);
            Vector2 brushCenter = new Vector2(10f, 0f);

            field.ApplyEdit(new TerrainEdit(brushCenter, radius: 3f, isAdditive: true));
            float afterOne = field.Sample(brushCenter);
            field.ApplyEdit(new TerrainEdit(brushCenter, radius: 3f, isAdditive: true));
            float afterTwo = field.Sample(brushCenter);

            Assert.AreEqual(afterOne, afterTwo, 1e-5f);
        }

        [Test]
        public void OverlappingBuildEdits_AtSamePosition_AreIdempotentNotAdditive()
        {
            var field = new TerrainField(10f);
            Vector2 brushCenter = new Vector2(10f, 0f);

            field.ApplyEdit(new TerrainEdit(brushCenter, radius: 3f, isAdditive: false));
            float afterOne = field.Sample(brushCenter);
            field.ApplyEdit(new TerrainEdit(brushCenter, radius: 3f, isAdditive: false));
            float afterTwo = field.Sample(brushCenter);

            Assert.AreEqual(afterOne, afterTwo, 1e-5f);
        }

        [Test]
        public void LargerDigEdit_AtSamePosition_CarvesDeeper()
        {
            // Idempotence only blocks re-digging an already-carved spot from carving *further*;
            // a genuinely larger edit (radius is now the only lever for edit magnitude) at the
            // same position should still take effect.
            var field = new TerrainField(10f);
            Vector2 brushCenter = new Vector2(10f, 0f);

            field.ApplyEdit(new TerrainEdit(brushCenter, radius: 3f, isAdditive: true));
            float afterSmall = field.Sample(brushCenter);
            field.ApplyEdit(new TerrainEdit(brushCenter, radius: 8f, isAdditive: true));
            float afterLarge = field.Sample(brushCenter);

            Assert.Greater(afterLarge, afterSmall);
        }

        [Test]
        public void ChunkIndexedSample_MatchesFullScanSample_ForEditsNearChunkBoundary()
        {
            var field = new TerrainField(10f);
            var grid = new ChunkGrid(10f, chunkSize: 5f);
            field.EnableChunkIndexing(grid);

            // Edit near the center of the grid, radius large enough to reach multiple chunks
            field.ApplyEdit(new TerrainEdit(new Vector2(0f, 0f), radius: 3f, isAdditive: true));

            foreach (TerrainChunk chunk in grid.AllChunks)
            {
                // Sample at the chunk's center position
                Vector2 sample = new Vector2((chunk.MinX + chunk.MaxX) * 0.5f, (chunk.MinY + chunk.MaxY) * 0.5f);

                float expected = field.Sample(sample);
                float actual = field.Sample(sample, chunk.Index);
                Assert.AreEqual(expected, actual, 1e-5f, $"Mismatch at chunk {chunk.Index}");
            }
        }

        [Test]
        public void ChunkIndexedSample_MatchesFullScanSample_ForEditReachingCenter()
        {
            var field = new TerrainField(10f);
            var grid = new ChunkGrid(10f, chunkSize: 5f);
            field.EnableChunkIndexing(grid);

            // Large brush near surface — radius reaches the planet's center
            field.ApplyEdit(new TerrainEdit(new Vector2(10f, 0f), radius: 15f, isAdditive: false));

            foreach (TerrainChunk chunk in grid.AllChunks)
            {
                Vector2 sample = new Vector2((chunk.MinX + chunk.MaxX) * 0.5f, (chunk.MinY + chunk.MaxY) * 0.5f);

                float expected = field.Sample(sample);
                float actual = field.Sample(sample, chunk.Index);
                Assert.AreEqual(expected, actual, 1e-5f, $"Mismatch at chunk {chunk.Index}");
            }
        }

        [Test]
        public void ChunkIndexedSample_MatchesFullScanSample_WithMixedAdditiveAndSubtractiveEdits()
        {
            var field = new TerrainField(10f);
            var grid = new ChunkGrid(10f, chunkSize: 5f);
            field.EnableChunkIndexing(grid);

            field.ApplyEdit(new TerrainEdit(new Vector2(10f, 0f), radius: 3f, isAdditive: true));
            field.ApplyEdit(new TerrainEdit(new Vector2(0f, 10f), radius: 3f, isAdditive: false));
            field.ApplyEdit(new TerrainEdit(new Vector2(-10f, 0f), radius: 3f, isAdditive: true));

            foreach (TerrainChunk chunk in grid.AllChunks)
            {
                Vector2 sample = new Vector2((chunk.MinX + chunk.MaxX) * 0.5f, (chunk.MinY + chunk.MaxY) * 0.5f);

                float expected = field.Sample(sample);
                float actual = field.Sample(sample, chunk.Index);
                Assert.AreEqual(expected, actual, 1e-5f, $"Mismatch at chunk {chunk.Index}");
            }
        }

        [Test]
        public void ChunkIndexedSample_WithoutEnableChunkIndexing_Throws()
        {
            var field = new TerrainField(10f);
            Assert.Throws<InvalidOperationException>(() => field.Sample(new Vector2(10f, 0f), 0));
        }

        [Test]
        public void ChunkIndexedSample_EditAppliedAfterEnableChunkIndexing_IsIndexed()
        {
            var field = new TerrainField(10f);
            var grid = new ChunkGrid(10f, chunkSize: 5f);
            field.EnableChunkIndexing(grid);

            Vector2 brushCenter = new Vector2(10f, 0f);
            TerrainChunk targetChunk = grid.GetChunkAt(brushCenter);
            float before = field.Sample(brushCenter, targetChunk.Index);

            field.ApplyEdit(new TerrainEdit(brushCenter, radius: 3f, isAdditive: true));

            Assert.Greater(field.Sample(brushCenter, targetChunk.Index), before);
            Assert.AreEqual(field.Sample(brushCenter), field.Sample(brushCenter, targetChunk.Index), 1e-5f);
        }

        [Test]
        public void PruneDeadEdits_RemovesZeroRadiusEdits()
        {
            var field = new TerrainField(10f);
            field.ApplyEdit(new TerrainEdit(new Vector2(10f, 0f), radius: 3f, isAdditive: true));
            field.ApplyEdit(new TerrainEdit(new Vector2(5f, 5f), radius: 0f, isAdditive: true));
            field.ApplyEdit(new TerrainEdit(new Vector2(-10f, 0f), radius: 2f, isAdditive: false));
            field.ApplyEdit(new TerrainEdit(new Vector2(0f, -10f), radius: 0f, isAdditive: false));

            Assert.AreEqual(4, field.Edits.Count);

            int pruned = field.PruneDeadEdits();

            Assert.AreEqual(2, pruned);
            Assert.AreEqual(2, field.Edits.Count);
        }

        [Test]
        public void PruneDeadEdits_PreservesSamplingCorrectness()
        {
            var field = new TerrainField(10f);
            Vector2 digPos = new Vector2(10f, 0f);

            field.ApplyEdit(new TerrainEdit(digPos, radius: 3f, isAdditive: true));
            field.ApplyEdit(new TerrainEdit(new Vector2(5f, 5f), radius: 0f, isAdditive: true));

            float beforePrune = field.Sample(digPos);

            field.PruneDeadEdits();

            Assert.AreEqual(beforePrune, field.Sample(digPos), 1e-5f);
        }

        [Test]
        public void PruneDeadEdits_SafeWhenNoDeadEdits()
        {
            var field = new TerrainField(10f);
            field.ApplyEdit(new TerrainEdit(new Vector2(10f, 0f), radius: 3f, isAdditive: true));
            field.ApplyEdit(new TerrainEdit(new Vector2(-10f, 0f), radius: 2f, isAdditive: false));

            int pruned = field.PruneDeadEdits();

            Assert.AreEqual(0, pruned);
            Assert.AreEqual(2, field.Edits.Count);
        }

        [Test]
        public void PruneDeadEdits_RemapsChunkIndicesCorrectly()
        {
            var field = new TerrainField(10f);
            var grid = new ChunkGrid(10f, chunkSize: 5f);
            field.EnableChunkIndexing(grid);

            // Add three edits: live, dead, live
            field.ApplyEdit(new TerrainEdit(new Vector2(10f, 0f), radius: 3f, isAdditive: true));
            field.ApplyEdit(new TerrainEdit(new Vector2(0f, 0f), radius: 0f, isAdditive: true));
            field.ApplyEdit(new TerrainEdit(new Vector2(-10f, 0f), radius: 2f, isAdditive: false));

            field.PruneDeadEdits();

            // Verify chunk-indexed sampling still matches full-scan sampling
            foreach (TerrainChunk chunk in grid.AllChunks)
            {
                Vector2 sample = new Vector2((chunk.MinX + chunk.MaxX) * 0.5f, (chunk.MinY + chunk.MaxY) * 0.5f);

                float expected = field.Sample(sample);
                float actual = field.Sample(sample, chunk.Index);
                Assert.AreEqual(expected, actual, 1e-5f, $"Mismatch at chunk {chunk.Index} after pruning");
            }
        }

        [Test]
        public void PruneDeadEdits_IdempotentOnEmptyField()
        {
            var field = new TerrainField(10f);

            int pruned = field.PruneDeadEdits();

            Assert.AreEqual(0, pruned);
            Assert.AreEqual(0, field.Edits.Count);
        }

        [Test]
        public void ClearEdits_AlsoClearsChunkIndexing()
        {
            var field = new TerrainField(10f);
            var grid = new ChunkGrid(10f, chunkSize: 5f);
            field.EnableChunkIndexing(grid);

            field.ApplyEdit(new TerrainEdit(new Vector2(10f, 0f), radius: 3f, isAdditive: true));

            field.ClearEdits();

            Assert.AreEqual(0, field.Edits.Count);
            // Verify chunk-indexed sampling still works (no stale indices)
            foreach (TerrainChunk chunk in grid.AllChunks)
            {
                Vector2 sample = new Vector2((chunk.MinX + chunk.MaxX) * 0.5f, (chunk.MinY + chunk.MaxY) * 0.5f);

                float expected = field.Sample(sample);
                float actual = field.Sample(sample, chunk.Index);
                Assert.AreEqual(expected, actual, 1e-5f, $"Mismatch at chunk {chunk.Index} after clear");
            }
        }

        [Test]
        public void LoadEdits_RebuildsChunkIndexing()
        {
            var field = new TerrainField(10f);
            var grid = new ChunkGrid(10f, chunkSize: 5f);
            field.EnableChunkIndexing(grid);

            // Add an edit, then load a different set
            field.ApplyEdit(new TerrainEdit(new Vector2(10f, 0f), radius: 3f, isAdditive: true));

            var replacement = new List<TerrainEdit>
            {
                new TerrainEdit(new Vector2(-10f, 0f), radius: 2f, isAdditive: false),
            };
            field.LoadEdits(replacement);

            Assert.AreEqual(1, field.Edits.Count);
            // Verify chunk-indexed sampling matches full-scan for the new edit
            Vector2 samplePos = new Vector2(-10f, 0f);
            float expected = field.Sample(samplePos);
            TerrainChunk chunk = grid.GetChunkAt(samplePos);
            float actual = field.Sample(samplePos, chunk.Index);
            Assert.AreEqual(expected, actual, 1e-5f);
        }

        [Test]
        public void EnableChunkIndexing_WithDynamicChunks_IndexCorrectly()
        {
            var field = new TerrainField(10f);
            var grid = new ChunkGrid(10f, chunkSize: 5f);
            field.EnableChunkIndexing(grid);

            // Apply an edit far outside the original grid — indexed by packed key,
            // no chunk is created by IndexEdit (chunk creation happens in the renderer).
            Vector2 farPosition = new Vector2(50f, 50f);
            field.ApplyEdit(new TerrainEdit(farPosition, radius: 3f, isAdditive: true));

            // The edit should be indexed correctly even though no chunk was created.
            // GetChunkAt creates the chunk; the edit should already be indexed by its key.
            TerrainChunk newChunk = grid.GetChunkAt(farPosition);
            float indexed = field.Sample(farPosition, newChunk.Index);
            float full = field.Sample(farPosition);

            Assert.AreEqual(full, indexed, 1e-5f);
        }

        [Test]
        public void ChunkIndexedSample_EditOutsideGrid_IndexedByPackedKey()
        {
            // Verify that edits applied outside the grid are indexed by packed (col, row)
            // so that a later-created chunk sees the edit in its index.
            var field = new TerrainField(10f);
            var grid = new ChunkGrid(10f, chunkSize: 5f);
            field.EnableChunkIndexing(grid);

            int initialChunkCount = grid.ChunkCount;

            // Apply a delete edit far outside the grid — should not create chunks
            Vector2 farPosition = new Vector2(50f, 50f);
            field.ApplyEdit(new TerrainEdit(farPosition, radius: 3f, isAdditive: true));

            // IndexEdit should not create chunks (it indexes by packed key only)
            Assert.AreEqual(initialChunkCount, grid.ChunkCount);

            // Now create a build edit at the same position — creates a chunk
            field.ApplyEdit(new TerrainEdit(farPosition, radius: 3f, isAdditive: false));

            // Get the chunk and verify chunk-indexed sample matches full scan
            TerrainChunk newChunk = grid.GetChunkAt(farPosition);
            float indexed = field.Sample(farPosition, newChunk.Index);
            float full = field.Sample(farPosition);

            Assert.AreEqual(full, indexed, 1e-5f);
        }

        // ---- PruneRedundantEdits ----

        [Test]
        public void PruneRedundantEdits_SmallDigInLargeCavity_Removed()
        {
            // A small dig entirely inside a larger mined cavity should be detected as redundant.
            var field = new TerrainField(10f);

            // First, carve a large cavity at (5, 0) — this removes terrain deeply.
            field.ApplyEdit(new TerrainEdit(new Vector2(5f, 0f), radius: 5f, isAdditive: true));

            // Then, a small dig well inside that cavity.
            field.ApplyEdit(new TerrainEdit(new Vector2(5f, 0f), radius: 1f, isAdditive: true));

            Assert.AreEqual(2, field.Edits.Count);

            int pruned = field.PruneRedundantEdits();

            Assert.AreEqual(1, pruned);
            Assert.AreEqual(1, field.Edits.Count);
            // The large dig should remain.
            Assert.AreEqual(5f, field.Edits[0].Radius);
        }

        [Test]
        public void PruneRedundantEdits_DigInSolid_Kept()
        {
            // A dig that actually carves solid terrain should NOT be removed.
            var field = new TerrainField(10f);

            // Dig at the center of the planet (deep in solid terrain).
            field.ApplyEdit(new TerrainEdit(Vector2.zero, radius: 2f, isAdditive: true));

            int pruned = field.PruneRedundantEdits();

            Assert.AreEqual(0, pruned);
            Assert.AreEqual(1, field.Edits.Count);
        }

        [Test]
        public void PruneRedundantEdits_BuildBuriedByLaterEdit_Removed()
        {
            // A build edit that extends terrain at the surface, then gets covered by a
            // later larger build, should be detected as redundant.
            var field = new TerrainField(10f);

            // Small build at the surface (10, 0) — this extends terrain outward.
            field.ApplyEdit(new TerrainEdit(new Vector2(10f, 0f), radius: 1f, isAdditive: false));

            // Larger build at same position — completely envelops the small one.
            field.ApplyEdit(new TerrainEdit(new Vector2(10f, 0f), radius: 5f, isAdditive: false));

            Assert.AreEqual(2, field.Edits.Count);

            int pruned = field.PruneRedundantEdits();

            Assert.AreEqual(1, pruned);
            Assert.AreEqual(1, field.Edits.Count);
            // The larger build should remain.
            Assert.AreEqual(5f, field.Edits[0].Radius);
        }

        [Test]
        public void PruneRedundantEdits_BuildOnSurface_Kept()
        {
            // A build edit that extends the terrain surface should NOT be removed.
            var field = new TerrainField(10f);

            // Build at the surface (10, 0) — adds terrain outward.
            field.ApplyEdit(new TerrainEdit(new Vector2(10f, 0f), radius: 2f, isAdditive: false));

            int pruned = field.PruneRedundantEdits();

            Assert.AreEqual(0, pruned);
            Assert.AreEqual(1, field.Edits.Count);
        }

        [Test]
        public void PruneRedundantEdits_BuildInEmptySpace_Kept()
        {
            // A standalone build edit in empty space should NOT be removed.
            var field = new TerrainField(10f);

            // Build far from the planet, in empty space.
            field.ApplyEdit(new TerrainEdit(new Vector2(30f, 0f), radius: 2f, isAdditive: false));

            int pruned = field.PruneRedundantEdits();

            Assert.AreEqual(0, pruned);
            Assert.AreEqual(1, field.Edits.Count);
        }

        [Test]
        public void PruneRedundantEdits_MultipleRedundant_RemovedInOneSweep()
        {
            var field = new TerrainField(10f);

            // Large cavity.
            field.ApplyEdit(new TerrainEdit(new Vector2(5f, 0f), radius: 5f, isAdditive: true));

            // Three small digs inside the cavity.
            field.ApplyEdit(new TerrainEdit(new Vector2(4f, 0f), radius: 0.5f, isAdditive: true));
            field.ApplyEdit(new TerrainEdit(new Vector2(6f, 0f), radius: 0.5f, isAdditive: true));
            field.ApplyEdit(new TerrainEdit(new Vector2(5f, 0f), radius: 0.5f, isAdditive: true));

            Assert.AreEqual(4, field.Edits.Count);

            int pruned = field.PruneRedundantEdits();

            Assert.AreEqual(3, pruned);
            Assert.AreEqual(1, field.Edits.Count);
        }

        [Test]
        public void PruneRedundantEdits_SamplingUnchangedAfterPrune()
        {
            var field = new TerrainField(10f);

            // Carve a cavity.
            field.ApplyEdit(new TerrainEdit(new Vector2(5f, 0f), radius: 5f, isAdditive: true));

            // Redundant small dig inside.
            field.ApplyEdit(new TerrainEdit(new Vector2(5f, 0f), radius: 1f, isAdditive: true));

            // Record samples before pruning.
            Vector2[] testPositions =
            {
                new Vector2(0f, 0f),
                new Vector2(5f, 0f),
                new Vector2(10f, 0f),
                new Vector2(-5f, 5f),
                new Vector2(8f, 3f),
            };

            float[] before = new float[testPositions.Length];
            for (int i = 0; i < testPositions.Length; i++)
            {
                before[i] = field.Sample(testPositions[i]);
            }

            field.PruneRedundantEdits();

            for (int i = 0; i < testPositions.Length; i++)
            {
                Assert.AreEqual(before[i], field.Sample(testPositions[i]), 1e-4f,
                    $"Sample mismatch at {testPositions[i]}");
            }
        }

        [Test]
        public void PruneRedundantEdits_ChunkIndexingPreserved()
        {
            var field = new TerrainField(10f);
            var grid = new ChunkGrid(10f, chunkSize: 5f);
            field.EnableChunkIndexing(grid);

            // Carve a cavity, then add redundant dig.
            field.ApplyEdit(new TerrainEdit(new Vector2(5f, 0f), radius: 4f, isAdditive: true));
            field.ApplyEdit(new TerrainEdit(new Vector2(5f, 0f), radius: 1f, isAdditive: true));

            field.PruneRedundantEdits();

            // Chunk-indexed sampling should still match full-scan sampling.
            foreach (TerrainChunk chunk in grid.AllChunks)
            {
                Vector2 sample = new Vector2((chunk.MinX + chunk.MaxX) * 0.5f,
                    (chunk.MinY + chunk.MaxY) * 0.5f);

                float expected = field.Sample(sample);
                float actual = field.Sample(sample, chunk.Index);
                Assert.AreEqual(expected, actual, 1e-5f,
                    $"Chunk-indexed mismatch at chunk {chunk.Index} after prune");
            }
        }

        [Test]
        public void PruneRedundantEdits_EmptyField_Safe()
        {
            var field = new TerrainField(10f);

            int pruned = field.PruneRedundantEdits();

            Assert.AreEqual(0, pruned);
            Assert.AreEqual(0, field.Edits.Count);
        }

        [Test]
        public void PruneRedundantEdits_CapsuleEdit_RedundantRemoved()
        {
            var field = new TerrainField(10f);

            // Large capsule dig.
            var start = new Vector2(5f, 0f);
            var end = new Vector2(10f, 0f);
            field.ApplyEdit(new TerrainEdit(start, end, radius: 4f, isAdditive: true, BrushShape.Capsule));

            // Small capsule dig entirely inside the large one.
            var smallStart = new Vector2(6f, 0f);
            var smallEnd = new Vector2(8f, 0f);
            field.ApplyEdit(new TerrainEdit(smallStart, smallEnd, radius: 1f, isAdditive: true, BrushShape.Capsule));

            Assert.AreEqual(2, field.Edits.Count);

            int pruned = field.PruneRedundantEdits();

            Assert.AreEqual(1, pruned);
            Assert.AreEqual(1, field.Edits.Count);
        }

        [Test]
        public void PruneRedundantEdits_DigThatMovesSurface_Kept()
        {
            // A dig at the surface that shifts the boundary should NOT be removed.
            var field = new TerrainField(10f);

            // Dig centered exactly at the surface.
            field.ApplyEdit(new TerrainEdit(new Vector2(10f, 0f), radius: 2f, isAdditive: true));

            int pruned = field.PruneRedundantEdits();

            Assert.AreEqual(0, pruned);
            Assert.AreEqual(1, field.Edits.Count);
        }

        [Test]
        public void PruneRedundantEdits_Idempotent()
        {
            // Running prune twice should produce the same result as running it once.
            var field = new TerrainField(10f);

            field.ApplyEdit(new TerrainEdit(new Vector2(5f, 0f), radius: 5f, isAdditive: true));
            field.ApplyEdit(new TerrainEdit(new Vector2(5f, 0f), radius: 1f, isAdditive: true));
            field.ApplyEdit(new TerrainEdit(new Vector2(-5f, 0f), radius: 2f, isAdditive: true));

            int firstPrune = field.PruneRedundantEdits();
            int secondPrune = field.PruneRedundantEdits();

            Assert.AreEqual(0, secondPrune, "Second prune should find no additional redundant edits.");
            Assert.Greater(firstPrune, 0, "First prune should have found redundant edits.");
        }
    }
}
