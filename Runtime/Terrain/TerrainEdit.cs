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

        /// <summary>
        /// When true, the contribution is clamped to zero outside the brush radius.
        /// Non-additive (place) edits should be clamped to avoid raising the SDF
        /// globally via Mathf.Min. Additive (remove) edits must remain unbounded
        /// for smooth MarchingSquares interpolation.
        /// </summary>
        public bool Clamped;

        public TerrainEdit(Vector2 localPosition, float radius, bool isAdditive)
            : this(localPosition, localPosition, radius, isAdditive, BrushShape.Circle, clamped: false)
        {
        }

        public TerrainEdit(Vector2 localPosition, float radius, bool isAdditive, bool clamped)
            : this(localPosition, localPosition, radius, isAdditive, BrushShape.Circle, clamped)
        {
        }

        public TerrainEdit(Vector2 localPosition, Vector2 endPosition, float radius, bool isAdditive, BrushShape shape)
            : this(localPosition, endPosition, radius, isAdditive, shape, clamped: false)
        {
        }

        public TerrainEdit(Vector2 localPosition, Vector2 endPosition, float radius, bool isAdditive, BrushShape shape, bool clamped)
        {
            LocalPosition = localPosition;
            EndPosition = endPosition;
            Radius = radius;
            IsAdditive = isAdditive;
            Shape = shape;
            Clamped = clamped;
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
        /// no curve, no plateau.
        ///
        /// For Circle shapes this is the radial cone from a single center. For Capsule shapes
        /// this is the true capsule SDF: the distance to the line segment between the two
        /// anchors (clamped to the segment), producing a smooth 2D pill with semicircular caps
        /// and a filled rectangular middle.
        ///
        /// Edits created with <see cref="Clamped"/> only affect points inside the brush
        /// radius. Outside the brush, the contribution returns a skip value that the CSG
        /// combine (Max for removal, Min for placement) naturally ignores.
        ///
        /// Unclamped edits extend beyond the brush: a removal cone goes to -infinity outside
        /// (Max(baseSDF, -inf) = baseSDF, so no effect) and a placement cone goes to +infinity
        /// (Min(baseSDF, +inf) = baseSDF, so no effect). Unclamped removal edits produce
        /// wider craters because the cone bleeds slightly beyond the brush boundary.
        /// </summary>
        public float SampleContribution(Vector2 localPosition)
        {
            float dist = DistanceToShape(localPosition);
            float magnitude = Radius - dist;
            float contribution = IsAdditive ? magnitude : -magnitude;

            // Clamped edits skip outside the brush circle so both removal and placement
            // affect the exact same area, making them 1:1 reversible.
            if (Clamped && dist > Radius)
            {
                // Return a value that Max/Min naturally ignores:
                // - For additive (Max combine): -infinity is ignored.
                // - For non-additive (Min combine): +infinity is ignored.
                return IsAdditive ? float.NegativeInfinity : float.PositiveInfinity;
            }

            return contribution;
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
