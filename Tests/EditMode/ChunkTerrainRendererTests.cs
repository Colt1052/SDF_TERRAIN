using System;
using NUnit.Framework;
using UnityEngine;
using SDFTerrain.Terrain;

namespace SDFTerrain.Tests
{
    public class ChunkTerrainRendererTests
    {
        private GameObject _gameObject;

        [SetUp]
        public void SetUp()
        {
            _gameObject = new GameObject("TestChunkTerrainRenderer");
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(_gameObject);
        }

        [Test]
        public void RebuildDirtyChunks_WithoutInitialize_Throws()
        {
            var renderer = _gameObject.AddComponent<ChunkTerrainRenderer>();
            Assert.Throws<InvalidOperationException>(() => renderer.RebuildDirtyChunks());
        }

        [Test]
        public void Initialize_CreatesOneChildPerChunk()
        {
            var renderer = _gameObject.AddComponent<ChunkTerrainRenderer>();
            var field = new TerrainField(10f);
            var grid = new ChunkGrid(10f, chunkSize: 5f);

            renderer.Initialize(field, grid, maxRadius: 15f);

            Assert.AreEqual(grid.ChunkCount, _gameObject.transform.childCount);
        }

        [Test]
        public void RebuildDirtyChunks_FirstCall_BuildsMeshForEveryChunk()
        {
            var renderer = _gameObject.AddComponent<ChunkTerrainRenderer>();
            var field = new TerrainField(10f);
            var grid = new ChunkGrid(10f, chunkSize: 5f);
            renderer.Initialize(field, grid, maxRadius: 15f);

            renderer.RebuildDirtyChunks();

            foreach (TerrainChunk chunk in grid.AllChunks)
            {
                // Find the child GameObject for this chunk
                foreach (Transform child in _gameObject.transform)
                {
                    if (child.name == $"Chunk_{chunk.Index}")
                    {
                        Mesh mesh = child.GetComponent<MeshFilter>().sharedMesh;
                        Assert.IsNotNull(mesh);
                        break;
                    }
                }
            }
        }

        [Test]
        public void RebuildDirtyChunks_ClearsDirtyFlags()
        {
            var renderer = _gameObject.AddComponent<ChunkTerrainRenderer>();
            var field = new TerrainField(10f);
            var grid = new ChunkGrid(10f, chunkSize: 5f);
            renderer.Initialize(field, grid, maxRadius: 15f);

            renderer.RebuildDirtyChunks();

            Assert.AreEqual(0, System.Linq.Enumerable.Count(grid.DirtyChunks()));
        }

        [Test]
        public void RebuildDirtyChunks_OnlyRebuildsChunksMarkedDirty()
        {
            var renderer = _gameObject.AddComponent<ChunkTerrainRenderer>();
            var field = new TerrainField(10f);
            var grid = new ChunkGrid(10f, chunkSize: 5f);
            renderer.Initialize(field, grid, maxRadius: 15f);
            renderer.RebuildDirtyChunks();

            // Pick a chunk away from the dig location
            TerrainChunk untouchedChunk = grid.GetChunkAt(new Vector2(-10f, -10f));
            Mesh untouchedMeshBefore = GetChunkMesh(renderer, untouchedChunk);

            // Dig near a specific position, then mark only that chunk dirty.
            field.ApplyEdit(new TerrainEdit(new Vector2(10f, 0f), radius: 6f, isAdditive: true));
            grid.MarkDirtyAt(new Vector2(10f, 0f));
            renderer.RebuildDirtyChunks();

            Mesh untouchedMeshAfter = GetChunkMesh(renderer, untouchedChunk);

            Assert.AreSame(untouchedMeshBefore, untouchedMeshAfter);
        }

        [Test]
        public void RebuildDirtyChunks_DirtyChunkMeshInstanceIsReused()
        {
            var renderer = _gameObject.AddComponent<ChunkTerrainRenderer>();
            var field = new TerrainField(10f);
            var grid = new ChunkGrid(10f, chunkSize: 5f);
            renderer.Initialize(field, grid, maxRadius: 15f);
            renderer.RebuildDirtyChunks();

            TerrainChunk targetChunk = grid.GetChunkAt(new Vector2(0f, 0f));
            Mesh firstMesh = GetChunkMesh(renderer, targetChunk);

            grid.MarkDirtyAt(new Vector2(0f, 0f));
            renderer.RebuildDirtyChunks();

            Mesh secondMesh = GetChunkMesh(renderer, targetChunk);

            Assert.AreSame(firstMesh, secondMesh);
        }

        [Test]
        public void ApplyBrush_WithoutInitialize_Throws()
        {
            var renderer = _gameObject.AddComponent<ChunkTerrainRenderer>();
            var brush = new TerrainBrush(BrushMode.Remove, radius: 2f);

            Assert.Throws<InvalidOperationException>(() => renderer.ApplyBrush(brush, Vector2.zero));
        }

        [Test]
        public void ApplyBrush_Remove_PersistsAdditiveEditOnField()
        {
            var renderer = _gameObject.AddComponent<ChunkTerrainRenderer>();
            var field = new TerrainField(10f);
            var grid = new ChunkGrid(10f, chunkSize: 5f);
            renderer.Initialize(field, grid, maxRadius: 15f);
            renderer.RebuildDirtyChunks();

            var brush = new TerrainBrush(BrushMode.Remove, radius: 2f);
            renderer.ApplyBrush(brush, new Vector2(10f, 0f));

            Assert.AreEqual(1, field.Edits.Count);
            Assert.IsTrue(field.Edits[0].IsAdditive);
        }

        [Test]
        public void ApplyBrush_Add_PersistsNonAdditiveEditOnField()
        {
            var renderer = _gameObject.AddComponent<ChunkTerrainRenderer>();
            var field = new TerrainField(10f);
            var grid = new ChunkGrid(10f, chunkSize: 5f);
            renderer.Initialize(field, grid, maxRadius: 15f);
            renderer.RebuildDirtyChunks();

            var brush = new TerrainBrush(BrushMode.Add, radius: 2f);
            renderer.ApplyBrush(brush, new Vector2(10f, 0f));

            Assert.AreEqual(1, field.Edits.Count);
            Assert.IsFalse(field.Edits[0].IsAdditive);
        }

        [Test]
        public void ApplyBrush_OnlyRebuildsOverlappingChunks()
        {
            var renderer = _gameObject.AddComponent<ChunkTerrainRenderer>();
            var field = new TerrainField(10f);
            var grid = new ChunkGrid(10f, chunkSize: 5f);
            renderer.Initialize(field, grid, maxRadius: 15f);
            renderer.RebuildDirtyChunks();

            // Pick a corner chunk far from the brush
            TerrainChunk farChunk = grid.GetChunkAt(new Vector2(-10f, -10f));
            Mesh untouchedMeshBefore = GetChunkMesh(renderer, farChunk);

            // Small brush near a surface position
            var brush = new TerrainBrush(BrushMode.Remove, radius: 1f);
            renderer.ApplyBrush(brush, new Vector2(10f, 0f));

            Mesh untouchedMeshAfter = GetChunkMesh(renderer, farChunk);
            Assert.AreSame(untouchedMeshBefore, untouchedMeshAfter);
            Assert.AreEqual(0, System.Linq.Enumerable.Count(grid.DirtyChunks()));
        }

        [Test]
        public void ApplyBrush_StraddlingChunkBoundary_MarksBothNeighborChunks()
        {
            var renderer = _gameObject.AddComponent<ChunkTerrainRenderer>();
            var field = new TerrainField(10f);
            var grid = new ChunkGrid(10f, chunkSize: 5f);
            renderer.Initialize(field, grid, maxRadius: 15f);
            renderer.RebuildDirtyChunks();

            // Place brush near a chunk boundary (at x=0, which is between col boundaries)
            Vector2 position = new Vector2(0f, 10f);

            TerrainChunk chunkA = grid.GetChunkAt(new Vector2(-0.5f, 10f));
            TerrainChunk chunkB = grid.GetChunkAt(new Vector2(0.5f, 10f));

            Mesh chunkAMeshBefore = GetChunkMesh(renderer, chunkA);
            Mesh chunkBMeshBefore = GetChunkMesh(renderer, chunkB);

            var brush = new TerrainBrush(BrushMode.Remove, radius: 3f);
            renderer.ApplyBrush(brush, position);

            Mesh chunkAMeshAfter = GetChunkMesh(renderer, chunkA);
            Mesh chunkBMeshAfter = GetChunkMesh(renderer, chunkB);

            // Both chunks' mesh instances are reused (rebuilt in place) rather than untouched.
            Assert.AreSame(chunkAMeshBefore, chunkAMeshAfter);
            Assert.AreSame(chunkBMeshBefore, chunkBMeshAfter);
            Assert.Greater(chunkAMeshAfter.vertexCount, 0);
            Assert.Greater(chunkBMeshAfter.vertexCount, 0);
        }

        [Test]
        public void ApplyBrush_OutsideOriginalGrid_CreatesChunkAndRenders()
        {
            var renderer = _gameObject.AddComponent<ChunkTerrainRenderer>();
            var field = new TerrainField(10f);
            var grid = new ChunkGrid(10f, chunkSize: 5f);
            renderer.Initialize(field, grid, maxRadius: 15f);
            renderer.RebuildDirtyChunks();

            int initialChunkCount = grid.ChunkCount;

            // Brush far outside the planet's bounding box
            Vector2 farPosition = new Vector2(50f, 50f);
            var brush = new TerrainBrush(BrushMode.Add, radius: 3f);
            renderer.ApplyBrush(brush, farPosition);

            // New chunk(s) should have been created
            Assert.Greater(grid.ChunkCount, initialChunkCount);

            // The edit should be visible in the field
            Assert.AreEqual(1, field.Edits.Count);

            // The new chunk should have a mesh
            TerrainChunk newChunk = grid.GetChunkAt(farPosition);
            Mesh mesh = GetChunkMesh(renderer, newChunk);
            Assert.IsNotNull(mesh);
        }

        [Test]
        public void ApplyBrush_OutsideOriginalGrid_EditPersisted()
        {
            var renderer = _gameObject.AddComponent<ChunkTerrainRenderer>();
            var field = new TerrainField(10f);
            var grid = new ChunkGrid(10f, chunkSize: 5f);
            renderer.Initialize(field, grid, maxRadius: 15f);
            renderer.RebuildDirtyChunks();

            Vector2 farPosition = new Vector2(50f, 50f);
            var brush = new TerrainBrush(BrushMode.Add, radius: 3f);
            renderer.ApplyBrush(brush, farPosition);

            // Sampling at the brush position should reflect the edit
            float sampled = field.Sample(farPosition);
            // A build edit at (50,50) should push the distance toward solid (more negative)
            Assert.Less(sampled, field.BaseRadius);

            // The chunk-indexed sample should also reflect it
            TerrainChunk newChunk = grid.GetChunkAt(farPosition);
            float indexedSample = field.Sample(farPosition, newChunk.Index);
            Assert.AreEqual(sampled, indexedSample, 1e-5f);
        }

        [Test]
        public void ApplyBrush_Remove_OutsideOriginalGrid_NoChunksCreated()
        {
            var renderer = _gameObject.AddComponent<ChunkTerrainRenderer>();
            var field = new TerrainField(10f);
            var grid = new ChunkGrid(10f, chunkSize: 5f);
            renderer.Initialize(field, grid, maxRadius: 15f);
            renderer.RebuildDirtyChunks();

            int initialChunkCount = grid.ChunkCount;

            // Delete brush far outside the planet's bounding box
            Vector2 farPosition = new Vector2(50f, 50f);
            var brush = new TerrainBrush(BrushMode.Remove, radius: 3f);
            renderer.ApplyBrush(brush, farPosition);

            // No new chunks should have been created
            Assert.AreEqual(initialChunkCount, grid.ChunkCount);

            // The edit should still be persisted on the field
            Assert.AreEqual(1, field.Edits.Count);
        }

        [Test]
        public void ApplyBrush_RemoveThenAdd_OutsideOriginalGrid_RenderCorrectly()
        {
            var renderer = _gameObject.AddComponent<ChunkTerrainRenderer>();
            var field = new TerrainField(10f);
            var grid = new ChunkGrid(10f, chunkSize: 5f);
            renderer.Initialize(field, grid, maxRadius: 15f);
            renderer.RebuildDirtyChunks();

            Vector2 farPosition = new Vector2(50f, 50f);

            // First, delete in empty space — should not create chunks
            var deleteBrush = new TerrainBrush(BrushMode.Remove, radius: 3f);
            renderer.ApplyBrush(deleteBrush, farPosition);
            Assert.AreEqual(4, grid.ChunkCount); // original grid only

            // Then, build at the same position — should create chunks and render
            var buildBrush = new TerrainBrush(BrushMode.Add, radius: 3f);
            renderer.ApplyBrush(buildBrush, farPosition);

            // New chunk(s) should exist
            Assert.Greater(grid.ChunkCount, 4);

            // Full-scan sample and chunk-indexed sample should agree
            float fullSample = field.Sample(farPosition);
            TerrainChunk newChunk = grid.GetChunkAt(farPosition);
            float indexedSample = field.Sample(farPosition, newChunk.Index);
            Assert.AreEqual(fullSample, indexedSample, 1e-5f);
        }

        [Test]
        public void ApplyBrush_BuildThenRemove_OutsideOriginalGrid_RemovesChunk()
        {
            var renderer = _gameObject.AddComponent<ChunkTerrainRenderer>();
            var field = new TerrainField(10f);
            var grid = new ChunkGrid(10f, chunkSize: 5f);
            renderer.Initialize(field, grid, maxRadius: 15f);
            renderer.RebuildDirtyChunks();

            Vector2 farPosition = new Vector2(50f, 50f);

            // Build terrain far from the planet
            var buildBrush = new TerrainBrush(BrushMode.Add, radius: 3f);
            renderer.ApplyBrush(buildBrush, farPosition);
            Assert.Greater(grid.ChunkCount, 16); // new chunks created

            // Delete the terrain we just built
            var deleteBrush = new TerrainBrush(BrushMode.Remove, radius: 5f);
            renderer.ApplyBrush(deleteBrush, farPosition);

            // Chunks should have been removed (back to original grid count or fewer)
            Assert.AreEqual(16, grid.ChunkCount);
        }

        [Test]
        public void ApplyBrush_EmptyChunk_RecreatedOnBuild()
        {
            var renderer = _gameObject.AddComponent<ChunkTerrainRenderer>();
            var field = new TerrainField(10f);
            var grid = new ChunkGrid(10f, chunkSize: 5f);
            renderer.Initialize(field, grid, maxRadius: 15f);
            renderer.RebuildDirtyChunks();

            Vector2 farPosition = new Vector2(50f, 50f);

            // Build → creates chunk
            var buildBrush = new TerrainBrush(BrushMode.Add, radius: 3f);
            renderer.ApplyBrush(buildBrush, farPosition);
            int afterBuild = grid.ChunkCount;
            Assert.Greater(afterBuild, 16);

            // Delete → removes chunk
            var deleteBrush = new TerrainBrush(BrushMode.Remove, radius: 5f);
            renderer.ApplyBrush(deleteBrush, farPosition);
            Assert.AreEqual(16, grid.ChunkCount);

            // Build again → recreates chunk
            renderer.ApplyBrush(buildBrush, farPosition);
            Assert.Greater(grid.ChunkCount, 16);

            // The rebuilt terrain should be samplable
            TerrainChunk chunk = grid.GetChunkAt(farPosition);
            float sample = field.Sample(farPosition, chunk.Index);
            Assert.Less(sample, field.BaseRadius);
        }

        [Test]
        public void RebuildDirtyChunks_PlanetChunksNotRemoved()
        {
            // Verify that normal planet chunks (with terrain) are not removed
            var renderer = _gameObject.AddComponent<ChunkTerrainRenderer>();
            var field = new TerrainField(10f);
            var grid = new ChunkGrid(10f, chunkSize: 5f);
            renderer.Initialize(field, grid, maxRadius: 15f);

            int initialCount = grid.ChunkCount;
            renderer.RebuildDirtyChunks();

            // Planet chunks have terrain — none should be removed
            Assert.AreEqual(initialCount, grid.ChunkCount);
        }

        [Test]
        public void ApplyBrush_Smooth_DoesNotAddEdits()
        {
            var renderer = _gameObject.AddComponent<ChunkTerrainRenderer>();
            var field = new TerrainField(10f);
            var grid = new ChunkGrid(10f, chunkSize: 5f);
            renderer.Initialize(field, grid, maxRadius: 15f);
            renderer.RebuildDirtyChunks();

            // Add an edit first so there's something to smooth.
            field.ApplyEdit(new TerrainEdit(new Vector2(10f, 0f), radius: 3f, isAdditive: true));

            var brush = new TerrainBrush(BrushMode.Smooth, radius: 2f);
            renderer.ApplyBrush(brush, new Vector2(10f, 0f));

            // Smooth should not add a new edit; it modifies existing edits in-place.
            Assert.AreEqual(1, field.Edits.Count);
        }

        [Test]
        public void ApplyBrush_Smooth_ReducesNearbyEditRadius()
        {
            var renderer = _gameObject.AddComponent<ChunkTerrainRenderer>();
            var field = new TerrainField(10f);
            var grid = new ChunkGrid(10f, chunkSize: 5f);
            renderer.Initialize(field, grid, maxRadius: 15f);
            renderer.RebuildDirtyChunks();

            float originalRadius = 5f;
            field.ApplyEdit(new TerrainEdit(new Vector2(10f, 0f), radius: originalRadius, isAdditive: true));

            var brush = new TerrainBrush(BrushMode.Smooth, radius: 2f);
            renderer.ApplyBrush(brush, new Vector2(10f, 0f));

            // The edit's radius should be smaller after smoothing at its center.
            Assert.Less(field.Edits[0].Radius, originalRadius);
        }

        [Test]
        public void ApplyBrush_Smooth_OutsideOriginalGrid_NoChunksCreated()
        {
            var renderer = _gameObject.AddComponent<ChunkTerrainRenderer>();
            var field = new TerrainField(10f);
            var grid = new ChunkGrid(10f, chunkSize: 5f);
            renderer.Initialize(field, grid, maxRadius: 15f);
            renderer.RebuildDirtyChunks();

            int initialChunkCount = grid.ChunkCount;

            Vector2 farPosition = new Vector2(50f, 50f);
            var brush = new TerrainBrush(BrushMode.Smooth, radius: 3f);
            renderer.ApplyBrush(brush, farPosition);

            // Smooth should not create new chunks in empty space.
            Assert.AreEqual(initialChunkCount, grid.ChunkCount);
        }

        [Test]
        public void ApplyBrush_Electric_NearSurface_PersistsAdditiveEdit()
        {
            var renderer = _gameObject.AddComponent<ChunkTerrainRenderer>();
            var field = new TerrainField(10f);
            var grid = new ChunkGrid(10f, chunkSize: 5f);
            renderer.Initialize(field, grid, maxRadius: 15f);
            renderer.RebuildDirtyChunks();

            // Brush near the planet surface (radius 10), from outside.
            // Search radius = 5f (how far to look for surface), strike radius = 2f (crater size).
            Vector2 position = new Vector2(14f, 0f);
            var brush = new TerrainBrush(BrushMode.Electric, radius: 5f);
            renderer.ApplyBrush(brush, position, strikeRadius: 2f);

            Assert.AreEqual(1, field.Edits.Count);
            // Electric produces a dig (additive) edit.
            Assert.IsTrue(field.Edits[0].IsAdditive);
            // The edit should be near the surface, not at the click position.
            Assert.Greater(field.Edits[0].LocalPosition.magnitude, 8f);
            Assert.Less(field.Edits[0].LocalPosition.magnitude, 12f);
            // The edit radius should be the strike radius, not the search radius.
            Assert.AreEqual(2f, field.Edits[0].Radius);
        }

        [Test]
        public void ApplyBrush_Electric_FarFromSurface_NoEdit()
        {
            var renderer = _gameObject.AddComponent<ChunkTerrainRenderer>();
            var field = new TerrainField(10f);
            var grid = new ChunkGrid(10f, chunkSize: 5f);
            renderer.Initialize(field, grid, maxRadius: 15f);
            renderer.RebuildDirtyChunks();

            // Brush far from the planet with a small search radius that can't reach the surface.
            Vector2 farPosition = new Vector2(50f, 50f);
            var brush = new TerrainBrush(BrushMode.Electric, radius: 3f);
            renderer.ApplyBrush(brush, farPosition, strikeRadius: 2f);

            // No surface within search radius — no edit should be created.
            Assert.AreEqual(0, field.Edits.Count);
        }

        [Test]
        public void ApplyBrush_Electric_OutsideOriginalGrid_NoChunksCreated()
        {
            var renderer = _gameObject.AddComponent<ChunkTerrainRenderer>();
            var field = new TerrainField(10f);
            var grid = new ChunkGrid(10f, chunkSize: 5f);
            renderer.Initialize(field, grid, maxRadius: 15f);
            renderer.RebuildDirtyChunks();

            int initialChunkCount = grid.ChunkCount;

            Vector2 farPosition = new Vector2(50f, 50f);
            var brush = new TerrainBrush(BrushMode.Electric, radius: 3f);
            renderer.ApplyBrush(brush, farPosition, strikeRadius: 2f);

            // Electric should not create new chunks (it only carves existing terrain).
            Assert.AreEqual(initialChunkCount, grid.ChunkCount);
        }

        [Test]
        public void ApplyBrush_Electric_EditPlacedNearSurface()
        {
            var renderer = _gameObject.AddComponent<ChunkTerrainRenderer>();
            var field = new TerrainField(10f);
            var grid = new ChunkGrid(10f, chunkSize: 5f);
            renderer.Initialize(field, grid, maxRadius: 15f);
            renderer.RebuildDirtyChunks();

            // Brush from just outside the planet surface.
            Vector2 position = new Vector2(12f, 0f);
            var brush = new TerrainBrush(BrushMode.Electric, radius: 5f);
            renderer.ApplyBrush(brush, position, strikeRadius: 3f);

            Assert.AreEqual(1, field.Edits.Count);
            // The edit position should be near the surface (radius ~10), not at the click point.
            Vector2 editPos = field.Edits[0].LocalPosition;
            Assert.AreEqual(10f, editPos.magnitude, 1.5f);
            // The edit radius should be the strike radius.
            Assert.AreEqual(3f, field.Edits[0].Radius);
        }

        [Test]
        public void ApplyBrush_Electric_StrikeRadiusIndependentOfSearchRadius()
        {
            var renderer = _gameObject.AddComponent<ChunkTerrainRenderer>();
            var field = new TerrainField(10f);
            var grid = new ChunkGrid(10f, chunkSize: 5f);
            renderer.Initialize(field, grid, maxRadius: 15f);
            renderer.RebuildDirtyChunks();

            // Large search radius (8f) to find the surface, small strike radius (0.5f) for a tiny crater.
            Vector2 position = new Vector2(15f, 0f);
            var brush = new TerrainBrush(BrushMode.Electric, radius: 8f);
            renderer.ApplyBrush(brush, position, strikeRadius: 0.5f);

            Assert.AreEqual(1, field.Edits.Count);
            // The edit radius should be the small strike radius, not the large search radius.
            Assert.AreEqual(0.5f, field.Edits[0].Radius);
            // The edit should be near the surface.
            Vector2 editPos = field.Edits[0].LocalPosition;
            Assert.AreEqual(10f, editPos.magnitude, 1.5f);
        }

        Mesh GetChunkMesh(ChunkTerrainRenderer renderer, TerrainChunk chunk)
        {
            foreach (Transform child in renderer.transform)
            {
                if (child.name == $"Chunk_{chunk.Index}")
                {
                    return child.GetComponent<MeshFilter>().sharedMesh;
                }
            }
            return null;
        }
    }
}
