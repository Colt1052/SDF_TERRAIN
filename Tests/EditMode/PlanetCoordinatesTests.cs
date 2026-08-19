using NUnit.Framework;
using UnityEngine;
using SDFTerrain.Core;

namespace SDFTerrain.Tests
{
    public class PlanetCoordinatesTests
    {
        [Test]
        public void WorldToLocal_SubtractsPlanetCenter()
        {
            Vector2 result = PlanetCoordinates.WorldToLocal(new Vector2(10f, 5f), new Vector2(3f, 2f));
            Assert.AreEqual(new Vector2(7f, 3f), result);
        }

        [Test]
        public void LocalToWorld_IsInverseOfWorldToLocal()
        {
            Vector2 world = new Vector2(10f, 5f);
            Vector2 center = new Vector2(3f, 2f);

            Vector2 local = PlanetCoordinates.WorldToLocal(world, center);
            Vector2 roundTrip = PlanetCoordinates.LocalToWorld(local, center);

            Assert.AreEqual(world, roundTrip);
        }

        [Test]
        public void LocalToRadial_ThenRadialToLocal_RoundTrips()
        {
            Vector2 local = new Vector2(3f, 4f);

            PlanetCoordinates.LocalToRadial(local, out float angle, out float radius);
            Vector2 roundTrip = PlanetCoordinates.RadialToLocal(angle, radius);

            Assert.AreEqual(5f, radius, 1e-5f);
            Assert.AreEqual(local.x, roundTrip.x, 1e-4f);
            Assert.AreEqual(local.y, roundTrip.y, 1e-4f);
        }

        [Test]
        public void LocalToRadial_OriginHasZeroRadius()
        {
            PlanetCoordinates.LocalToRadial(Vector2.zero, out float angle, out float radius);
            Assert.AreEqual(0f, radius, 1e-5f);
        }

        [Test]
        public void SurfaceNormal_IsUnitLength()
        {
            Vector2 normal = PlanetCoordinates.SurfaceNormal(2.1f);
            Assert.AreEqual(1f, normal.magnitude, 1e-5f);
        }

        [Test]
        public void GravityDirection_PointsFromPositionTowardCenter()
        {
            Vector2 direction = PlanetCoordinates.GravityDirection(new Vector2(10f, 0f), Vector2.zero);
            Assert.AreEqual(new Vector2(-1f, 0f), direction);
        }

        [Test]
        public void GravityDirection_AtCenterIsZero()
        {
            Vector2 direction = PlanetCoordinates.GravityDirection(Vector2.zero, Vector2.zero);
            Assert.AreEqual(Vector2.zero, direction);
        }
    }
}
