using System;
using UnityEngine;

namespace SDFTerrain.Terrain
{
    /// <summary>
    /// Immutable description of a circular terrain edit: what to do (<see cref="BrushMode"/>) and
    /// how far it reaches. The single translation point from gameplay-facing Add/Remove language
    /// to <see cref="TerrainEdit"/>'s IsAdditive (dig) convention, so no other code needs to know
    /// that mapping.
    /// </summary>
    public readonly struct TerrainBrush
    {
        public BrushMode Mode { get; }
        public float Radius { get; }

        public TerrainBrush(BrushMode mode, float radius)
        {
            if (radius <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(radius), radius, "Brush radius must be positive.");
            }

            Mode = mode;
            Radius = radius;
        }

        /// <summary>Builds the persisted edit this brush stroke produces at the given planet-local position.</summary>
        /// <remarks>Throws for <see cref="BrushMode.Smooth"/> — smooth operates via <see cref="TerrainField.SmoothEdits"/>, not <see cref="TerrainEdit"/>.</remarks>
        public TerrainEdit ToEdit(Vector2 localPosition)
        {
            if (Mode == BrushMode.Smooth)
            {
                throw new InvalidOperationException("Smooth mode does not produce a TerrainEdit; use TerrainField.SmoothEdits instead.");
            }

            // Remove and Electric both carve terrain (additive = dig).
            bool isAdditive = Mode == BrushMode.Remove || Mode == BrushMode.Electric;
            return new TerrainEdit(localPosition, Radius, isAdditive);
        }
    }
}
