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

        public TerrainEdit(Vector2 localPosition, float radius, bool isAdditive)
        {
            LocalPosition = localPosition;
            Radius = radius;
            IsAdditive = isAdditive;
        }

        /// <summary>
        /// Signed contribution of this edit at the given local position: positive values dig
        /// (increase distance / remove material), negative values build (decrease distance /
        /// add material). This is exactly the signed-distance cone to the brush's circular
        /// boundary — no curve, no plateau, and crucially *unbounded* (not clamped to zero
        /// beyond the brush radius), matching how the base planet sphere's own signed-distance
        /// formula is valid and continuous everywhere. Clamping this to 0 outside the radius
        /// (an earlier version did) creates a discontinuity right at the brush boundary once
        /// combined via Max/Min in TerrainField.Sample: approaching the boundary from inside the
        /// contribution goes to 0, but from outside a clamped value reverts sharply to whatever
        /// the base field is there, which is a very different value deep underground. That jump
        /// forces MarchingSquaresMesher's edge interpolation to land almost exactly on a grid
        /// corner instead of partway across the cell, which reads as "no interpolation at all."
        /// Linearity matters: MarchingSquaresMesher only linearly interpolates the zero-crossing
        /// between grid samples, so only a linear field with gradient everywhere reconstructs an
        /// exact circle.
        /// </summary>
        public float SampleContribution(Vector2 localPosition)
        {
            float distanceFromBrush = Vector2.Distance(localPosition, LocalPosition);
            float magnitude = Radius - distanceFromBrush;
            return IsAdditive ? magnitude : -magnitude;
        }
    }
}
