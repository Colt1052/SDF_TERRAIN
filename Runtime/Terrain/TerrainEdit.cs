using UnityEngine;

namespace SDFTerrain.Terrain
{
    /// <summary>
    /// Shape primitive used by a <see cref="TerrainEdit"/>. Circle is a single point stamp;
    /// Capsule is a true 2D pill (semicircular caps + filled rectangular middle) defined by
    /// the segment from <see cref="TerrainEdit.LocalPosition"/> to
    /// <see cref="TerrainEdit.EndPosition"/>. Extend this enum for future brush shapes
    /// (rectangle, diamond, etc.).
    /// </summary>
    public enum BrushShape
    {
        Circle = 0,
        Capsule = 1,
    }

    /// <summary>
    /// A single persisted modification to a TerrainField: a shaped brush stamp at a
    /// planet-local position. Additive edits carve away material (raise the distance value,
    /// pushing the surface inward); non-additive edits add material (lower the distance value,
    /// pushing the surface outward). This is the only data a save file needs per edit — the
    /// base terrain is always regenerated from the planet seed.
    ///
    /// The <see cref="Shape"/> field controls the geometry: Circle is a single-point stamp,
    /// Capsule is a true 2D pill whose SDF is the distance to the segment between
    /// <see cref="LocalPosition"/> and <see cref="EndPosition"/>.
    /// </summary>
    [System.Serializable]
    public struct TerrainEdit
    {
        public Vector2 LocalPosition;
        public Vector2 EndPosition;
        public float Radius;
        public bool IsAdditive;
        public BrushShape Shape;

        public TerrainEdit(Vector2 localPosition, float radius, bool isAdditive)
            : this(localPosition, localPosition, radius, isAdditive, BrushShape.Circle)
        {
        }

        public TerrainEdit(Vector2 localPosition, Vector2 endPosition, float radius, bool isAdditive, BrushShape shape)
        {
            LocalPosition = localPosition;
            EndPosition = endPosition;
            Radius = radius;
            IsAdditive = isAdditive;
            Shape = shape;
        }

        /// <summary>
        /// Gets the bounding rectangle of this edit's footprint for chunk indexing.
        /// For circles this is the square around <see cref="LocalPosition"/>; for capsules
        /// it spans both anchors.
        /// </summary>
        public void GetBoundingBox(out float minX, out float maxX, out float minY, out float maxY)
        {
            if (Shape == BrushShape.Capsule)
            {
                minX = Mathf.Min(LocalPosition.x, EndPosition.x) - Radius;
                maxX = Mathf.Max(LocalPosition.x, EndPosition.x) + Radius;
                minY = Mathf.Min(LocalPosition.y, EndPosition.y) - Radius;
                maxY = Mathf.Max(LocalPosition.y, EndPosition.y) + Radius;
            }
            else
            {
                minX = LocalPosition.x - Radius;
                maxX = LocalPosition.x + Radius;
                minY = LocalPosition.y - Radius;
                maxY = LocalPosition.y + Radius;
            }
        }

        /// <summary>
        /// Raw geometric distance from the given point to this edit's shape boundary
        /// (circle center or capsule segment). Used by smoothing and any caller that needs
        /// proximity without the sign/radius transform of <see cref="SampleContribution"/>.
        /// </summary>
        public float DistanceToShape(Vector2 localPosition)
        {
            return Shape == BrushShape.Capsule
                ? DistanceToSegment(localPosition, LocalPosition, EndPosition)
                : Vector2.Distance(localPosition, LocalPosition);
        }

        /// <summary>
        /// Signed contribution of this edit at the given local position: positive values dig
        /// (increase distance / remove material), negative values build (decrease distance /
        /// add material). This is exactly the signed-distance cone to the brush's boundary —
        /// no curve, no plateau, and crucially *unbounded* (not clamped to zero beyond the
        /// brush radius), matching how the base planet sphere's own signed-distance formula is
        /// valid and continuous everywhere.
        ///
        /// For Circle shapes this is the radial cone from a single center. For Capsule shapes
        /// this is the true capsule SDF: the distance to the line segment between the two
        /// anchors (clamped to the segment), producing a smooth 2D pill with semicircular caps
        /// and a filled rectangular middle. Linearity is preserved along the segment so
        /// MarchingSquaresMesher reconstructs exact arcs at the caps.
        /// </summary>
        public float SampleContribution(Vector2 localPosition)
        {
            float distance = Radius - DistanceToShape(localPosition);
            return IsAdditive ? distance : -distance;
        }

        /// <summary>
        /// Euclidean distance from <paramref name="point"/> to the line segment
        /// [<paramref name="a"/> .. <paramref name="b"/>].
        /// Uses the standard projection-and-clamp approach.
        /// </summary>
        internal static float DistanceToSegment(Vector2 point, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float lenSq = ab.x * ab.x + ab.y * ab.y;

            // Degenerate: segment is a point.
            if (lenSq == 0f)
            {
                return Vector2.Distance(point, a);
            }

            // Parameter t of the projection of point onto the line through a-b, clamped to [0,1].
            float t = Mathf.Clamp01(Vector2.Dot(point - a, ab) / lenSq);
            Vector2 closest = a + ab * t;
            return Vector2.Distance(point, closest);
        }
    }
}
