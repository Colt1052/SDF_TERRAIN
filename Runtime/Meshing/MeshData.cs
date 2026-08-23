using System.Collections.Generic;
using UnityEngine;

namespace SDFTerrain.Meshing
{
    /// <summary>Plain output of a mesher — vertices/triangles/normals/UVs/colors, no Unity Mesh dependency.</summary>
    public class MeshData
    {
        public readonly List<Vector3> Vertices = new List<Vector3>();
        public readonly List<int> Triangles = new List<int>();
        public readonly List<Vector3> Normals = new List<Vector3>();
        public readonly List<Vector2> UVs = new List<Vector2>();
        public readonly List<Color> Colors = new List<Color>();

        private readonly Dictionary<Vector2, int> _vertexIndices = new Dictionary<Vector2, int>();

        public void AddTriangle(Vector2 a, Vector2 b, Vector2 c, float uvScale, Color color = default)
        {
            // Reversed (a, c, b) order: the mesher's case table winds counter-clockwise as seen
            // from +Z looking toward the origin, but Unity's default 2D camera sits at Z = -10
            // looking in the +Z direction — i.e. viewing the mesh from -Z. Swapping the last two
            // vertices flips winding so triangles face the camera (and Vector3.back normals below
            // match), without touching the case table itself.
            Triangles.Add(GetOrAddVertex(a, uvScale, color));
            Triangles.Add(GetOrAddVertex(c, uvScale, color));
            Triangles.Add(GetOrAddVertex(b, uvScale, color));
        }

        /// <summary>
        /// Returns the index of an existing vertex at this position, or adds a new one. Adjacent
        /// Marching Squares cells share corner samples and edge-crossing points exactly (same
        /// float computation for the same shared input), so deduplicating by exact position
        /// collapses what would otherwise be several independent copies of the same vertex —
        /// shrinking both the mesh upload and the edge count ColliderContourBuilder processes,
        /// with no change to triangle topology/winding.
        /// </summary>
        private int GetOrAddVertex(Vector2 position, float uvScale, Color color)
        {
            if (_vertexIndices.TryGetValue(position, out int existingIndex))
            {
                return existingIndex;
            }

            int index = Vertices.Count;
            Vertices.Add(new Vector3(position.x, position.y, 0f));
            Normals.Add(Vector3.back);
            UVs.Add(position * uvScale);
            Colors.Add(color);
            _vertexIndices[position] = index;

            return index;
        }
    }
}
