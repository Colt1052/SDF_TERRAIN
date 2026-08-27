using UnityEngine;

namespace SDFTerrain.Terrain
{
    /// <summary>
    /// Gizmo-based debug visualization that draws the outline of every <see cref="TerrainEdit"/>
    /// in the Scene view. Circle edits render as wire circles; capsule edits render as wire pills
    /// (two semicircular caps joined by parallel edges). Additive (dig) edits draw in red,
    /// non-additive (build) edits draw in green.
    ///
    /// Attach to the planet GameObject alongside <see cref="ChunkTerrainRenderer"/> or
    /// <see cref="TerrainRenderer"/>. Call <see cref="Initialize"/> at startup, or leave null
    /// to auto-discover the field from a sibling renderer component.
    /// </summary>
    public class EditDebugView : MonoBehaviour
    {
        [Tooltip("Draw edit outlines in the Scene view")]
        [SerializeField] private bool enabled;

        [Tooltip("Draw wire spheres at each edit's center point")]
        [SerializeField] private bool drawEditCenters;

        [Tooltip("Radius of the center-point marker sphere")]
        [SerializeField] private float centerMarkerRadius = 0.15f;

        [Tooltip("Number of segments per circle/capsule outline (higher = smoother)")]
        [SerializeField] private int outlineSegments = 24;

        private TerrainField _terrainField;

        // Reusable buffer to avoid GC pressure per-frame gizmo draws.
        private readonly Vector3[] _buffer = new Vector3[128];

        /// <summary>
        /// Prepares this view to visualize edits from the given <see cref="TerrainField"/>.
        /// If not called, the component auto-discovers the field from a sibling
        /// <see cref="ChunkTerrainRenderer"/> or <see cref="TerrainRenderer"/> at draw time.
        /// </summary>
        public void Initialize(TerrainField terrainField)
        {
            _terrainField = terrainField;
        }

        private void OnDrawGizmos()
        {
            if (!enabled)
                return;

            // Auto-discover if not explicitly initialized.
            TerrainField field = _terrainField;
            if (field == null)
            {
                var chunkRenderer = GetComponentInSibling<ChunkTerrainRenderer>();
                if (chunkRenderer != null)
                    field = chunkRenderer.Field;

                // TerrainRenderer doesn't expose a public Field property, so we only
                // auto-discover from ChunkTerrainRenderer. For TerrainRenderer users,
                // call Initialize() manually.
            }

            if (field == null)
                return;

            int segments = Mathf.Clamp(outlineSegments, 8, 64);

            foreach (var edit in field.Edits)
            {
                if (edit.Radius <= 0.01f)
                    continue;

                Color color = edit.IsAdditive ? Color.red : Color.green;

                if (edit.Shape == BrushShape.Capsule && edit.LocalPosition != edit.EndPosition)
                {
                    DrawCapsuleOutline(edit.LocalPosition, edit.EndPosition, edit.Radius, segments, color);
                }
                else
                {
                    DrawCircleOutline(edit.LocalPosition, edit.Radius, segments, color);
                }

                if (drawEditCenters)
                {
                    Gizmos.color = color;
                    Gizmos.DrawWireSphere(edit.LocalPosition, centerMarkerRadius);
                }
            }
        }

        /// <summary>
        /// Finds a component of type T on another GameObject that shares this transform's parent
        /// or is this GameObject itself.
        /// </summary>
        T GetComponentInSibling<T>() where T : UnityEngine.Component
        {
            // Check self first, then siblings under the same parent.
            var comp = GetComponent<T>();
            if (comp != null)
                return comp;

            var parent = transform.parent;
            if (parent == null)
                return null;

            for (int i = 0; i < parent.childCount; i++)
            {
                var child = parent.GetChild(i);
                if (child == transform)
                    continue;
                comp = child.GetComponent<T>();
                if (comp != null)
                    return comp;
            }
            return null;
        }

        /// <summary>
        /// Draws a wire circle at the given planet-local position, transformed to world space.
        /// </summary>
        private void DrawCircleOutline(Vector2 center, float radius, int segments, Color color)
        {
            Gizmos.color = color;

            for (int i = 0; i <= segments; i++)
            {
                float angle = (i / (float)segments) * Mathf.PI * 2f;
                Vector2 localPoint = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
                _buffer[i] = transform.TransformPoint(localPoint);
            }

            for (int i = 0; i < segments; i++)
            {
                Gizmos.DrawLine(_buffer[i], _buffer[i + 1]);
            }
        }

        /// <summary>
        /// Draws a wire capsule (2D pill) outline between two planet-local anchor points.
        /// The outline consists of two semicircular caps and two parallel edge lines,
        /// all transformed to world space via the GameObject's transform.
        /// </summary>
        private void DrawCapsuleOutline(Vector2 start, Vector2 end, float radius, int segments, Color color)
        {
            Gizmos.color = color;

            Vector2 direction = end - start;
            float length = direction.magnitude;
            if (length < 0.001f)
            {
                DrawCircleOutline(start, radius, segments, color);
                return;
            }

            float alpha = Mathf.Atan2(direction.y, direction.x);
            int half = Mathf.Max(4, segments / 2);

            // First cap: semicircle around start, swept from alpha+PI to alpha+2PI.
            for (int i = 0; i <= half; i++)
            {
                float phi = alpha + Mathf.PI + (i / (float)half) * Mathf.PI;
                Vector2 pt = start + new Vector2(Mathf.Cos(phi), Mathf.Sin(phi)) * radius;
                _buffer[i] = transform.TransformPoint(pt);
            }

            // Last cap: semicircle around end, swept from alpha to alpha+PI.
            int offset = half + 1;
            for (int i = 0; i <= half; i++)
            {
                float phi = alpha + (i / (float)half) * Mathf.PI;
                Vector2 pt = end + new Vector2(Mathf.Cos(phi), Mathf.Sin(phi)) * radius;
                _buffer[offset + i] = transform.TransformPoint(pt);
            }

            // Connect points along the arc of each cap (skip the seam at the edge).
            int total = offset + half;
            for (int i = 0; i < total; i++)
            {
                int next = i + 1;
                // Skip the two seam points — we draw the straight edges separately.
                if (i == half - 1 || i == total - 1)
                    continue;
                Gizmos.DrawLine(_buffer[i], _buffer[next]);
            }

            // Parallel edge lines between the two caps.
            Vector2 perp = new Vector2(-direction.x / length, -direction.y / length);
            Vector3 topA = transform.TransformPoint(start + perp * radius);
            Vector3 topB = transform.TransformPoint(end + perp * radius);
            Gizmos.DrawLine(topA, topB);

            Vector3 botA = transform.TransformPoint(start - perp * radius);
            Vector3 botB = transform.TransformPoint(end - perp * radius);
            Gizmos.DrawLine(botA, botB);
        }
    }
}
