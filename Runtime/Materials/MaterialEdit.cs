using System;
using UnityEngine;

namespace SDFTerrain.Materials
{
    /// <summary>
    /// A single persistent material override — the material counterpart to a <see cref="Terrain.TerrainEdit"/>.
    /// When a player places terrain (e.g., builds with stone), a TerrainEdit adds geometry and a
    /// MaterialEdit records that the material at those positions is now explicitly Stone rather than
    /// whatever the procedural geology says.
    ///
    /// MaterialEdits are evaluated in order (last applicable wins). They are indexed spatially by
    /// the same chunk grid so queries don't scan the full history.
    /// </summary>
    [System.Serializable]
    public struct MaterialEdit : System.IEquatable<MaterialEdit>
    {
        /// <summary>Center of the material override in planet-local space.</summary>
        public Vector2 LocalPosition;

        /// <summary>Radius of the circular override region (world units).</summary>
        public float Radius;

        /// <summary>The material ID to assign within this region.</summary>
        public MaterialId MaterialId;

        /// <summary>
        /// Sequential application index. Higher indices override lower indices when multiple
        /// edits overlap at the same position.
        /// </summary>
        public int Order;

        public MaterialEdit(Vector2 localPosition, float radius, MaterialId materialId, int order)
        {
            LocalPosition = localPosition;
            Radius = radius;
            MaterialId = materialId;
            Order = order;
        }

        /// <summary>
        /// Returns true if <paramref name="position"/> falls within this edit's circular region.
        /// </summary>
        public bool Contains(Vector2 position)
        {
            float dx = position.x - LocalPosition.x;
            float dy = position.y - LocalPosition.y;
            return dx * dx + dy * dy <= Radius * Radius;
        }

        /// <summary>
        /// Computes the signed distance from <paramref name="position"/> to this edit's boundary.
        /// Positive = inside the circle, negative = outside. Matches <see cref="Terrain.TerrainEdit"/>
        /// distance semantics.
        /// </summary>
        public float SampleDistance(Vector2 position)
        {
            float distanceFromBrush = Vector2.Distance(position, LocalPosition);
            return Radius - distanceFromBrush;
        }

        public bool Equals(MaterialEdit other)
            => LocalPosition == other.LocalPosition
                && Radius.Equals(other.Radius)
                && MaterialId == other.MaterialId
                && Order == other.Order;

        public override bool Equals(object obj) => obj is MaterialEdit other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + LocalPosition.GetHashCode();
                hash = hash * 31 + Radius.GetHashCode();
                hash = hash * 31 + MaterialId.GetHashCode();
                hash = hash * 31 + Order.GetHashCode();
                return hash;
            }
        }

        public override string ToString() => $"MaterialEdit({MaterialId}, r={Radius:F1}, pos={LocalPosition}, order={Order})";
    }
}
