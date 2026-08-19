using System;
using NUnit.Framework;
using UnityEngine;
using SDFTerrain.Terrain;

namespace SDFTerrain.Tests
{
    public class MarchingSquaresGridDebugViewTests
    {
        private GameObject _gameObject;

        [SetUp]
        public void SetUp()
        {
            _gameObject = new GameObject("TestMarchingSquaresGridDebugView");
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(_gameObject);
        }

        [Test]
        public void Initialize_NullField_Throws()
        {
            var view = _gameObject.AddComponent<MarchingSquaresGridDebugView>();
            var grid = new ChunkGrid(10f, chunkSize: 5f);

            Assert.Throws<ArgumentNullException>(() => view.Initialize(null, grid, 1f));
        }

        [Test]
        public void Initialize_NullChunkGrid_Throws()
        {
            var view = _gameObject.AddComponent<MarchingSquaresGridDebugView>();
            var field = new TerrainField(10f);

            Assert.Throws<ArgumentNullException>(() => view.Initialize(field, null, 1f));
        }

        [Test]
        public void Initialize_NonPositiveCellSize_Throws()
        {
            var view = _gameObject.AddComponent<MarchingSquaresGridDebugView>();
            var field = new TerrainField(10f);
            var grid = new ChunkGrid(10f, chunkSize: 5f);

            Assert.Throws<ArgumentOutOfRangeException>(() => view.Initialize(field, grid, 0f));
        }

        [Test]
        public void Refresh_WithoutInitialize_Throws()
        {
            var view = _gameObject.AddComponent<MarchingSquaresGridDebugView>();
            Assert.Throws<InvalidOperationException>(() => view.Refresh());
        }

        [Test]
        public void Initialize_DefaultHiddenState_AllChildRenderersDisabled()
        {
            var view = _gameObject.AddComponent<MarchingSquaresGridDebugView>();
            var field = new TerrainField(10f);
            var grid = new ChunkGrid(10f, chunkSize: 5f);

            view.Initialize(field, grid, 1f);

            foreach (Transform child in _gameObject.transform)
            {
                Assert.IsFalse(child.GetComponent<MeshRenderer>().enabled);
            }
        }

        [Test]
        public void Refresh_PopulatesNonEmptyMeshesForAllChildren()
        {
            var view = _gameObject.AddComponent<MarchingSquaresGridDebugView>();
            var field = new TerrainField(10f);
            var grid = new ChunkGrid(10f, chunkSize: 5f);
            view.Initialize(field, grid, 1f);

            view.Refresh();

            int nonEmptyCount = 0;
            foreach (Transform child in _gameObject.transform)
            {
                Mesh mesh = child.GetComponent<MeshFilter>().sharedMesh;
                Assert.IsNotNull(mesh);
                Assert.Greater(mesh.vertexCount, 0);
                nonEmptyCount++;
            }

            Assert.AreEqual(3, nonEmptyCount);
        }

        [Test]
        public void Initialize_CalledTwice_ReusesChildGameObjects()
        {
            var view = _gameObject.AddComponent<MarchingSquaresGridDebugView>();
            var field = new TerrainField(10f);
            var grid = new ChunkGrid(10f, chunkSize: 5f);

            view.Initialize(field, grid, 1f);
            int childCountAfterFirst = _gameObject.transform.childCount;

            view.Initialize(field, grid, 1f);
            int childCountAfterSecond = _gameObject.transform.childCount;

            Assert.AreEqual(childCountAfterFirst, childCountAfterSecond);
        }
    }
}
