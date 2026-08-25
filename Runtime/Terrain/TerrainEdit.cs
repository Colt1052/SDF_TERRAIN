using UnityEngine;

namespace SDFTerrain.Terrain
{
    /// <summary>
    /// A single persisted modification to a TerrainField: a circular brush stamp at a
    /// planet-local position. Additive edits carve away material (raise the distance value,
    /// pushing the surface inward); non-additive edits add material (lower the distance value,
    /// pushing the surface outward). This is the only data a save file needs per edit — the
    /// base terrain is always regenerated from the planet seed.
    /// </summary>
    [System.Serializable]
    public struct TerrainEdit
    {
        public Vector2 LocalPosition;
        public float Radius;
        public bool IsAdditive;

        /// <summary>
        /// When true, the contribution is clamped to zero outside the brush radius.
        /// Non-additive (place) edits should be clamped to avoid raising the SDF
        /// globally via Mathf.Min. Additive (remove) edits must remain unbounded
        /// for smooth MarchingSquares interpolation.
        /// </summary>
        public bool Clamped;

        public TerrainEdit(Vector2 localPosition, float radius, bool isAdditive, bool clamped = false)
        {
            LocalPosition = localPosition;
            Radius = radius;
            IsAdditive = isAdditive;
            Clamped = clamped;
        }

        /// <summary>
        /// Signed contribution of this edit at the given local position: positive values dig
        /// (increase distance / remove material), negative values build (decrease distance /
        /// add material). This is exactly the signed-distance cone to the brush's circular
        /// boundary — no curve, no plateau.
        ///
        /// Edits created with <see cref="Clamped"/> only affect points inside the brush circle.
        /// Outside the brush, the contribution returns a skip value that the CSG combine
        /// (Max for removal, Min for placement) naturally ignores.
        ///
        /// Unclamped edits extend beyond the brush: a removal cone goes to -infinity outside
        /// (Max(baseSDF, -inf) = baseSDF, so no effect) and a placement cone goes to +infinity
        /// (Min(baseSDF, +inf) = baseSDF, so no effect). Unclamped removal edits produce
        /// wider craters because the cone bleeds slightly beyond the brush boundary.
        /// </summary>
        public float SampleContribution(Vector2 localPosition)
        {
            float distanceFromBrush = Vector2.Distance(localPosition, LocalPosition);
            float magnitude = Radius - distanceFromBrush;
            float contribution = IsAdditive ? magnitude : -magnitude;

            // Clamped edits skip outside the brush circle so both removal and placement
            // affect the exact same area, making them 1:1 reversible.
            if (Clamped && distanceFromBrush > Radius)
            {
                // Additive (remove) edits use Max — skip with MinValue so Max(baseSDF, MinValue) = baseSDF.
                // Non-additive (place) edits use Min — skip with MaxValue so Min(baseSDF, MaxValue) = baseSDF.
                return IsAdditive ? float.MinValue : float.MaxValue;
            }

            return contribution;
        }
    }
}
