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

            for (int i = 0; i < _gameObject.transform.childCount; i++)
            {
                Mesh mesh = _gameObject.transform.GetChild(i).GetComponent<MeshFilter>().sharedMesh;
                Assert.IsNotNull(mesh);
                Assert.Greater(mesh.vertexCount, 0);
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
            TerrainChunk untouchedChunk = grid.GetChunkAtGrid(grid.Cols - 1, grid.Rows - 1);
            Mesh untouchedMeshBefore = _gameObject.transform.GetChild(untouchedChunk.Index).GetComponent<MeshFilter>().sharedMesh;

            // Dig near a specific position, then mark only that chunk dirty.
            field.ApplyEdit(new TerrainEdit(new Vector2(10f, 0f), radius: 6f, isAdditive: true));
            grid.MarkDirtyAt(new Vector2(10f, 0f));
            renderer.RebuildDirtyChunks();

            Mesh untouchedMeshAfter = _gameObject.transform.GetChild(untouchedChunk.Index).GetComponent<MeshFilter>().sharedMesh;

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
            Mesh firstMesh = _gameObject.transform.GetChild(targetChunk.Index).GetComponent<MeshFilter>().sharedMesh;

            grid.MarkDirtyAt(new Vector2(0f, 0f));
            renderer.RebuildDirtyChunks();

            Mesh secondMesh = _gameObject.transform.GetChild(targetChunk.Index).GetComponent<MeshFilter>().sharedMesh;

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
            TerrainChunk farChunk = grid.GetChunkAtGrid(grid.Cols - 1, grid.Rows - 1);
            Mesh untouchedMeshBefore = _gameObject.transform.GetChild(farChunk.Index).GetComponent<MeshFilter>().sharedMesh;

            // Small brush near a surface position
            var brush = new TerrainBrush(BrushMode.Remove, radius: 1f);
            renderer.ApplyBrush(brush, new Vector2(10f, 0f));

            Mesh untouchedMeshAfter = _gameObject.transform.GetChild(farChunk.Index).GetComponent<MeshFilter>().sharedMesh;
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

            Mesh chunkAMeshBefore = _gameObject.transform.GetChild(chunkA.Index).GetComponent<MeshFilter>().sharedMesh;
            Mesh chunkBMeshBefore = _gameObject.transform.GetChild(chunkB.Index).GetComponent<MeshFilter>().sharedMesh;

            var brush = new TerrainBrush(BrushMode.Remove, radius: 3f);
            renderer.ApplyBrush(brush, position);

            Mesh chunkAMeshAfter = _gameObject.transform.GetChild(chunkA.Index).GetComponent<MeshFilter>().sharedMesh;
            Mesh chunkBMeshAfter = _gameObject.transform.GetChild(chunkB.Index).GetComponent<MeshFilter>().sharedMesh;

            // Both chunks' mesh instances are reused (rebuilt in place) rather than untouched.
            Assert.AreSame(chunkAMeshBefore, chunkAMeshAfter);
            Assert.AreSame(chunkBMeshBefore, chunkBMeshAfter);
            Assert.Greater(chunkAMeshAfter.vertexCount, 0);
            Assert.Greater(chunkBMeshAfter.vertexCount, 0);
        }
    }
}
