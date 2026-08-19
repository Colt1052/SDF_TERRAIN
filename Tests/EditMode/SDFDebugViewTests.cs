using System;
using NUnit.Framework;
using UnityEngine;
using SDFTerrain.Terrain;

namespace SDFTerrain.Tests
{
    public class SDFDebugViewTests
    {
        private GameObject _gameObject;

        [SetUp]
        public void SetUp()
        {
            _gameObject = new GameObject("TestSDFDebugView");
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(_gameObject);
        }

        [Test]
        public void Initialize_NullField_Throws()
        {
            var view = _gameObject.AddComponent<SDFDebugView>();
            Assert.Throws<ArgumentNullException>(() => view.Initialize(null, 10f));
        }

        [Test]
        public void Initialize_NonPositiveMaxRadius_Throws()
        {
            var view = _gameObject.AddComponent<SDFDebugView>();
            var field = new TerrainField(10f);
            Assert.Throws<ArgumentOutOfRangeException>(() => view.Initialize(field, 0f));
        }

        [Test]
        public void Refresh_WithoutInitialize_Throws()
        {
            var view = _gameObject.AddComponent<SDFDebugView>();
            Assert.Throws<InvalidOperationException>(() => view.Refresh());
        }

        [Test]
        public void Initialize_DefaultHiddenState_MeshRendererDisabled()
        {
            var view = _gameObject.AddComponent<SDFDebugView>();
            var field = new TerrainField(10f);

            view.Initialize(field, 15f);

            Assert.IsFalse(_gameObject.GetComponent<MeshRenderer>().enabled);
        }

        [Test]
        public void Refresh_PopulatesMeshRendererMaterialTexture()
        {
            var view = _gameObject.AddComponent<SDFDebugView>();
            var field = new TerrainField(10f);
            view.Initialize(field, 15f);

            view.Refresh();

            Assert.IsNotNull(_gameObject.GetComponent<MeshRenderer>().sharedMaterial.mainTexture);
        }

        [Test]
        public void Initialize_BuildsNonEmptyQuadMesh()
        {
            var view = _gameObject.AddComponent<SDFDebugView>();
            var field = new TerrainField(10f);

            view.Initialize(field, 15f);

            Mesh mesh = _gameObject.GetComponent<MeshFilter>().sharedMesh;
            Assert.IsNotNull(mesh);
            Assert.Greater(mesh.vertexCount, 0);
        }
    }
}
