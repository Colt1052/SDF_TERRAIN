using System;
using UnityEngine;

namespace SDFTerrain.Meshing
{
    /// <summary>
    /// Applies boundary contours extracted from <see cref="MeshData"/> to a
    /// <see cref="PolygonCollider2D"/>. Thin glue between the pure <see cref="ColliderContourBuilder"/>
    /// and the Unity physics component — kept separate so the contour extraction itself stays
    /// testable without a live PolygonCollider2D/scene.
    /// </summary>
    public static class TerrainColliderBuilder
    {
        public static void Apply(MeshData meshData, PolygonCollider2D collider)
        {
            if (meshData == null)
            {
                throw new ArgumentNullException(nameof(meshData));
            }

            if (collider == null)
            {
                throw new ArgumentNullException(nameof(collider));
            }

            var contours = ColliderContourBuilder.BuildContours(meshData);

            collider.pathCount = contours.Count;
            for (int i = 0; i < contours.Count; i++)
            {
                collider.SetPath(i, contours[i]);
            }
        }
    }
}
