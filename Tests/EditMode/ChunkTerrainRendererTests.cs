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
            var grid = new ChunkGrid(6);

            renderer.Initialize(field, grid, maxRadius: 15f);

            Assert.AreEqual(6, _gameObject.transform.childCount);
        }

        [Test]
        public void RebuildDirtyChunks_FirstCall_BuildsMeshForEveryChunk()
        {
            var renderer = _gameObject.AddComponent<ChunkTerrainRenderer>();
            var field = new TerrainField(10f);
            var grid = new ChunkGrid(6);
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
            var grid = new ChunkGrid(6);
            renderer.Initialize(field, grid, maxRadius: 15f);

            renderer.RebuildDirtyChunks();

            Assert.AreEqual(0, System.Linq.Enumerable.Count(grid.DirtyChunks()));
        }

        [Test]
        public void RebuildDirtyChunks_OnlyRebuildsChunksMarkedDirty()
        {
            var renderer = _gameObject.AddComponent<ChunkTerrainRenderer>();
            var field = new TerrainField(10f);
            var grid = new ChunkGrid(6);
            renderer.Initialize(field, grid, maxRadius: 15f);
            renderer.RebuildDirtyChunks();

            Mesh untouchedMeshBefore = _gameObject.transform.GetChild(2).GetComponent<MeshFilter>().sharedMesh;

            // Dig near chunk 0's angular range only, then mark just that chunk dirty.
            field.ApplyEdit(new TerrainEdit(new Vector2(10f, 0f), radius: 6f, isAdditive: true));
            grid.MarkDirtyAt(0f);
            renderer.RebuildDirtyChunks();

            Mesh untouchedMeshAfter = _gameObject.transform.GetChild(2).GetComponent<MeshFilter>().sharedMesh;

            // Chunk 2's mesh instance is untouched by a rebuild scoped to chunk 0.
            Assert.AreSame(untouchedMeshBefore, untouchedMeshAfter);
        }

        [Test]
        public void RebuildDirtyChunks_DirtyChunkMeshInstanceIsReused()
        {
            var renderer = _gameObject.AddComponent<ChunkTerrainRenderer>();
            var field = new TerrainField(10f);
            var grid = new ChunkGrid(6);
            renderer.Initialize(field, grid, maxRadius: 15f);
            renderer.RebuildDirtyChunks();

            Mesh firstMesh = _gameObject.transform.GetChild(0).GetComponent<MeshFilter>().sharedMesh;

            grid.MarkDirtyAt(0f);
            renderer.RebuildDirtyChunks();

            Mesh secondMesh = _gameObject.transform.GetChild(0).GetComponent<MeshFilter>().sharedMesh;

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
            var grid = new ChunkGrid(6);
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
            var grid = new ChunkGrid(6);
            renderer.Initialize(field, grid, maxRadius: 15f);
            renderer.RebuildDirtyChunks();

            var brush = new TerrainBrush(BrushMode.Add, radius: 2f);
            renderer.ApplyBrush(brush, new Vector2(10f, 0f));

            Assert.AreEqual(1, field.Edits.Count);
            Assert.IsFalse(field.Edits[0].IsAdditive);
        }

        [Test]
        public void ApplyBrush_OnlyRebuildsOverlappingChunk()
        {
            var renderer = _gameObject.AddComponent<ChunkTerrainRenderer>();
            var field = new TerrainField(10f);
            var grid = new ChunkGrid(6);
            renderer.Initialize(field, grid, maxRadius: 15f);
            renderer.RebuildDirtyChunks();

            Mesh untouchedMeshBefore = _gameObject.transform.GetChild(2).GetComponent<MeshFilter>().sharedMesh;

            // Brush near angle 0 (chunk 0), well away from chunk 2's angular range.
            var brush = new TerrainBrush(BrushMode.Remove, radius: 1f);
            renderer.ApplyBrush(brush, new Vector2(10f, 0f));

            Mesh untouchedMeshAfter = _gameObject.transform.GetChild(2).GetComponent<MeshFilter>().sharedMesh;
            Assert.AreSame(untouchedMeshBefore, untouchedMeshAfter);
            Assert.AreEqual(0, System.Linq.Enumerable.Count(grid.DirtyChunks()));
        }

        [Test]
        public void ApplyBrush_StraddlingChunkBoundary_MarksBothNeighborChunks()
        {
            var renderer = _gameObject.AddComponent<ChunkTerrainRenderer>();
            var field = new TerrainField(10f);
            var grid = new ChunkGrid(6);
            renderer.Initialize(field, grid, maxRadius: 15f);
            renderer.RebuildDirtyChunks();

            // Chunk boundary between chunk 0 and chunk 1 sits at angle 2*PI/6. Center the brush
            // right on it with a radius wide enough to reach into both chunks' angular ranges.
            float boundaryAngle = (2f * Mathf.PI) / 6f;
            Vector2 position = Core.RadialMath.PositionAt(boundaryAngle, 10f);

            Mesh chunk0Before = _gameObject.transform.GetChild(0).GetComponent<MeshFilter>().sharedMesh;
            Mesh chunk1Before = _gameObject.transform.GetChild(1).GetComponent<MeshFilter>().sharedMesh;

            var brush = new TerrainBrush(BrushMode.Remove, radius: 3f);
            renderer.ApplyBrush(brush, position);

            Mesh chunk0After = _gameObject.transform.GetChild(0).GetComponent<MeshFilter>().sharedMesh;
            Mesh chunk1After = _gameObject.transform.GetChild(1).GetComponent<MeshFilter>().sharedMesh;

            // Both chunks' mesh instances are reused (rebuilt in place) rather than untouched.
            Assert.AreSame(chunk0Before, chunk0After);
            Assert.AreSame(chunk1Before, chunk1After);
            Assert.Greater(chunk0After.vertexCount, 0);
            Assert.Greater(chunk1After.vertexCount, 0);
        }

    }
}
