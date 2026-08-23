using NUnit.Framework;
using UnityEngine;
using SDFTerrain.Terrain;

namespace SDFTerrain.Tests
{
    public class TerrainFieldFindNearestSurfaceTests
    {
        [Test]
        public void FindNearestSurface_OutsidePlanet_FindsSurface()
        {
            var field = new TerrainField(10f);
            // Position outside the planet (radius 10), at distance 15 from center.
            Vector2 position = new Vector2(15f, 0f);
            float searchRadius = 10f;

            bool found = field.FindNearestSurface(position, searchRadius, 36, out Vector2 nearest);

            Assert.IsTrue(found);
            // The nearest surface should be near the planet surface (radius ~10).
            Assert.AreEqual(10f, nearest.magnitude, 1f);
        }

        [Test]
        public void FindNearestSurface_InsidePlanet_FindsSurface()
        {
            var field = new TerrainField(10f);
            // Position inside the planet, at distance 5 from center.
            Vector2 position = new Vector2(5f, 0f);
            float searchRadius = 10f;

            bool found = field.FindNearestSurface(position, searchRadius, 36, out Vector2 nearest);

            Assert.IsTrue(found);
            // The nearest surface should be near the planet surface (radius ~10).
            Assert.AreEqual(10f, nearest.magnitude, 1f);
        }

        [Test]
        public void FindNearestSurface_OnSurface_FindsNearestSurfacePoint()
        {
            var field = new TerrainField(10f);
            // Position exactly on the surface.
            Vector2 position = new Vector2(10f, 0f);
            float searchRadius = 5f;

            bool found = field.FindNearestSurface(position, searchRadius, 36, out Vector2 nearest);

            Assert.IsTrue(found);
            // The result should be a surface point (magnitude ~10), not necessarily
            // the input position — the search always finds actual surface crossings.
            Assert.AreEqual(10f, nearest.magnitude, 1f);
        }

        [Test]
        public void FindNearestSurface_FarFromPlanet_ReturnsFalse()
        {
            var field = new TerrainField(10f);
            // Position very far from the planet.
            Vector2 position = new Vector2(100f, 0f);
            float searchRadius = 5f;

            bool found = field.FindNearestSurface(position, searchRadius, 36, out Vector2 nearest);

            Assert.IsFalse(found);
            // When not found, should return the input position.
            Assert.AreEqual(position, nearest);
        }

        [Test]
        public void FindNearestSurface_ZeroRadius_ReturnsFalse()
        {
            var field = new TerrainField(10f);
            Vector2 position = new Vector2(10f, 0f);

            bool found = field.FindNearestSurface(position, 0f, 36, out Vector2 nearest);

            Assert.IsFalse(found);
            Assert.AreEqual(position, nearest);
        }

        [Test]
        public void FindNearestSurface_NegativeRadius_ReturnsFalse()
        {
            var field = new TerrainField(10f);
            Vector2 position = new Vector2(10f, 0f);

            bool found = field.FindNearestSurface(position, -1f, 36, out Vector2 nearest);

            Assert.IsFalse(found);
            Assert.AreEqual(position, nearest);
        }

        [Test]
        public void FindNearestSurface_DifferentAngles_FindsCorrectSurface()
        {
            var field = new TerrainField(10f);

            // Test from multiple directions around the planet.
            for (int i = 0; i < 8; i++)
            {
                float angle = (i / 8f) * Mathf.PI * 2f;
                Vector2 position = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * 15f;

                bool found = field.FindNearestSurface(position, 10f, 36, out Vector2 nearest);

                Assert.IsTrue(found, $"Should find surface at angle {angle}");
                Assert.AreEqual(10f, nearest.magnitude, 1f, $"Surface radius at angle {angle}");
            }
        }

        [Test]
        public void FindNearestSurface_WithEdit_FindsEditedSurface()
        {
            var field = new TerrainField(10f);
            // Dig a hole at (10, 0) with radius 4.
            field.ApplyEdit(new TerrainEdit(new Vector2(10f, 0f), radius: 4f, isAdditive: true));

            // Search from a position near the dug surface.
            Vector2 position = new Vector2(14f, 0f);

            bool found = field.FindNearestSurface(position, 10f, 36, out Vector2 nearest);

            Assert.IsTrue(found);
            // The dig pushes the surface inward, so the nearest surface
            // should be closer to center than the original 10f radius.
            Assert.Less(nearest.magnitude, 10f);
        }

        [Test]
        public void FindNearestSurface_BuildEdit_FindsBuiltSurface()
        {
            var field = new TerrainField(10f);
            // Build terrain at (10, 0) with radius 4.
            field.ApplyEdit(new TerrainEdit(new Vector2(10f, 0f), radius: 4f, isAdditive: false));

            // Search from a position beyond the built terrain.
            Vector2 position = new Vector2(16f, 0f);

            bool found = field.FindNearestSurface(position, 10f, 36, out Vector2 nearest);

            Assert.IsTrue(found);
            // The build pushes the surface outward, so the nearest surface
            // should be farther from center than the original 10f radius.
            Assert.Greater(nearest.magnitude, 10f);
        }
    }
}
