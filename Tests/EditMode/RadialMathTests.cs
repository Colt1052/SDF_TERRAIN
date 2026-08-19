using NUnit.Framework;
using UnityEngine;
using SDFTerrain.Core;

namespace SDFTerrain.Tests
{
    public class RadialMathTests
    {
        [Test]
        public void WrapAngle_ZeroStaysZero()
        {
            Assert.AreEqual(0f, RadialMath.WrapAngle(0f), 1e-5f);
        }

        [Test]
        public void WrapAngle_NegativeWrapsToPositiveRange()
        {
            float result = RadialMath.WrapAngle(-Mathf.PI / 2f);
            Assert.AreEqual(3f * Mathf.PI / 2f, result, 1e-5f);
        }

        [Test]
        public void WrapAngle_GreaterThanTwoPiWrapsDown()
        {
            float result = RadialMath.WrapAngle(2.5f * Mathf.PI);
            Assert.AreEqual(0.5f * Mathf.PI, result, 1e-5f);
        }

        [Test]
        public void AngleOf_PositiveXAxisIsZero()
        {
            Assert.AreEqual(0f, RadialMath.AngleOf(new Vector2(5f, 0f)), 1e-5f);
        }

        [Test]
        public void AngleOf_PositiveYAxisIsHalfPi()
        {
            Assert.AreEqual(Mathf.PI / 2f, RadialMath.AngleOf(new Vector2(0f, 5f)), 1e-5f);
        }

        [Test]
        public void AngleOf_NegativeYAxisWrapsToThreeHalvesPi()
        {
            Assert.AreEqual(3f * Mathf.PI / 2f, RadialMath.AngleOf(new Vector2(0f, -5f)), 1e-5f);
        }

        [Test]
        public void PositionAt_RoundTripsWithAngleOfAndMagnitude()
        {
            const float angle = 1.234f;
            const float radius = 7.5f;

            Vector2 position = RadialMath.PositionAt(angle, radius);

            Assert.AreEqual(radius, position.magnitude, 1e-4f);
            Assert.AreEqual(angle, RadialMath.AngleOf(position), 1e-4f);
        }

        [Test]
        public void SurfaceNormalAt_IsUnitLength()
        {
            Vector2 normal = RadialMath.SurfaceNormalAt(0.77f);
            Assert.AreEqual(1f, normal.magnitude, 1e-5f);
        }

        [Test]
        public void PositionAt_ZeroRadiusIsOrigin()
        {
            Vector2 position = RadialMath.PositionAt(1.9f, 0f);
            Assert.AreEqual(Vector2.zero, position);
        }
    }
}
