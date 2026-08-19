using UnityEngine;

namespace SDFTerrain.Core
{
    /// <summary>
    /// Conversions between world space, planet-local space, and radial (angle, radius) space.
    /// Planet-local space is world position minus the planet's center, so generation and
    /// terrain math never depend on where the planet sits in the world.
    /// </summary>
    public static class PlanetCoordinates
    {
        public static Vector2 WorldToLocal(Vector2 worldPosition, Vector2 planetCenter)
        {
            return worldPosition - planetCenter;
        }

        public static Vector2 LocalToWorld(Vector2 localPosition, Vector2 planetCenter)
        {
            return localPosition + planetCenter;
        }

        /// <summary>Converts a planet-local position to (angle in radians, radius).</summary>
        public static void LocalToRadial(Vector2 localPosition, out float angleRadians, out float radius)
        {
            angleRadians = RadialMath.AngleOf(localPosition);
            radius = localPosition.magnitude;
        }

        /// <summary>Converts (angle in radians, radius) to a planet-local position.</summary>
        public static Vector2 RadialToLocal(float angleRadians, float radius)
        {
            return RadialMath.PositionAt(angleRadians, radius);
        }

        /// <summary>Outward-facing surface normal in world space at the given angle.</summary>
        public static Vector2 SurfaceNormal(float angleRadians)
        {
            return RadialMath.SurfaceNormalAt(angleRadians);
        }

        /// <summary>
        /// Gravity direction at a world position: always points from the position toward the
        /// planet center. There is no global "up" in this coordinate system.
        /// </summary>
        public static Vector2 GravityDirection(Vector2 worldPosition, Vector2 planetCenter)
        {
            Vector2 toCenter = planetCenter - worldPosition;
            return toCenter.sqrMagnitude > 0f ? toCenter.normalized : Vector2.zero;
        }
    }
}
