using UnityEngine;

namespace SDFTerrain.Core
{
    /// <summary>
    /// Pure utility functions for 2D radial (polar) math around a planet-local origin.
    /// No state, no allocations.
    /// </summary>
    public static class RadialMath
    {
        /// <summary>Wraps an angle in radians to the range [0, 2*PI).</summary>
        public static float WrapAngle(float angleRadians)
        {
            float wrapped = angleRadians % (2f * Mathf.PI);
            if (wrapped < 0f)
            {
                wrapped += 2f * Mathf.PI;
            }

            return wrapped;
        }

        /// <summary>Angle in radians (0 = +X axis, counter-clockwise) of a planet-local position.</summary>
        public static float AngleOf(Vector2 localPosition)
        {
            return WrapAngle(Mathf.Atan2(localPosition.y, localPosition.x));
        }

        /// <summary>Unit direction vector pointing outward from the planet center at the given angle.</summary>
        public static Vector2 DirectionAt(float angleRadians)
        {
            return new Vector2(Mathf.Cos(angleRadians), Mathf.Sin(angleRadians));
        }

        /// <summary>Planet-local position at the given angle and radius.</summary>
        public static Vector2 PositionAt(float angleRadians, float radius)
        {
            return DirectionAt(angleRadians) * radius;
        }

        /// <summary>
        /// Outward-facing surface normal at the given angle. On a circular planet this is
        /// identical to the radial direction, but kept as a distinct API so future non-circular
        /// deformation can override it without changing callers.
        /// </summary>
        public static Vector2 SurfaceNormalAt(float angleRadians)
        {
            return DirectionAt(angleRadians);
        }
    }
}
