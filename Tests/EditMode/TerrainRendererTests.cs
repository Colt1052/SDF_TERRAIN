using NUnit.Framework;
using UnityEngine;
using SDFTerrain.Terrain;

namespace SDFTerrain.Tests
{
    public class TerrainRendererTests
    {
        private GameObject _gameObject;

        [SetUp]
        public void SetUp()
        {
            _gameObject = new GameObject("TestTerrainRenderer");
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(_gameObject);
        }

        [Test]
        public void Rebuild_ProducesNonEmptyMeshAndValidCollider()
        {
            var renderer = _gameObject.AddComponent<TerrainRenderer>();
            var field = new TerrainField(10f);

            renderer.Rebuild(field, boundsRadius: 15f);

            Mesh mesh = _gameObject.GetComponent<MeshFilter>().sharedMesh;
            PolygonCollider2D collider = _gameObject.GetComponent<PolygonCollider2D>();

            Assert.IsNotNull(mesh);
            Assert.Greater(mesh.vertexCount, 0);
            Assert.Greater(collider.pathCount, 0);
        }

        [Test]
        public void Rebuild_CalledTwice_ReusesMeshInstance()
        {
            var renderer = _gameObject.AddComponent<TerrainRenderer>();
            var field = new TerrainField(10f);

            renderer.Rebuild(field, boundsRadius: 15f);
            Mesh firstMesh = _gameObject.GetComponent<MeshFilter>().sharedMesh;

            renderer.Rebuild(field, boundsRadius: 15f);
            Mesh secondMesh = _gameObject.GetComponent<MeshFilter>().sharedMesh;

            Assert.AreSame(firstMesh, secondMesh);
        }
    }
}
