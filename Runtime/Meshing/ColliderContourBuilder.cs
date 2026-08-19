using System;
using System.Collections.Generic;
using UnityEngine;

namespace SDFTerrain.Meshing
{
    /// <summary>
    /// Extracts closed boundary polygon loops from triangulated <see cref="MeshData"/>, suitable
    /// for feeding directly to <c>PolygonCollider2D.SetPath</c>. A pure function of its input —
    /// no dependency on Planet/TerrainField/PolygonCollider2D — so it is testable against small
    /// synthetic mesh data.
    ///
    /// Works by finding edges that belong to exactly one triangle (boundary edges — interior
    /// edges are shared by two triangles and traversed in opposite directions, so they cancel),
    /// then stitching those directed edges end-to-start into closed loops. Edges are keyed by
    /// vertex index rather than position: MeshData deduplicates vertices by position, so two
    /// triangles sharing an edge always reference the same vertex indices, and integer-tuple
    /// keys are cheaper to hash than float-pair keys.
    /// </summary>
    public static class ColliderContourBuilder
    {
        public static List<Vector2[]> BuildContours(MeshData meshData)
        {
            if (meshData == null)
            {
                throw new ArgumentNullException(nameof(meshData));
            }

            var directedEdgeCounts = new Dictionary<(int from, int to), int>();

            for (int i = 0; i < meshData.Triangles.Count; i += 3)
            {
                int a = meshData.Triangles[i];
                int b = meshData.Triangles[i + 1];
                int c = meshData.Triangles[i + 2];

                AddEdge(directedEdgeCounts, a, b);
                AddEdge(directedEdgeCounts, b, c);
                AddEdge(directedEdgeCounts, c, a);
            }

            // A boundary edge's reverse does not appear (nothing on the other side to cancel it).
            var boundaryEdges = new Dictionary<int, int>();
            foreach (var edge in directedEdgeCounts)
            {
                (int from, int to) = edge.Key;
                if (!directedEdgeCounts.ContainsKey((to, from)))
                {
                    boundaryEdges[from] = to;
                }
            }

            return StitchLoops(boundaryEdges, meshData.Vertices);
        }

        private static void AddEdge(Dictionary<(int, int), int> edges, int from, int to)
        {
            var key = (from, to);
            edges.TryGetValue(key, out int count);
            edges[key] = count + 1;
        }

        private static List<Vector2[]> StitchLoops(Dictionary<int, int> boundaryEdges, IReadOnlyList<Vector3> vertices)
        {
            var loops = new List<Vector2[]>();
            var visited = new HashSet<int>();

            foreach (int start in boundaryEdges.Keys)
            {
                if (visited.Contains(start))
                {
                    continue;
                }

                var loop = new List<Vector2>();
                int current = start;

                while (!visited.Contains(current))
                {
                    visited.Add(current);
                    Vector3 vertex = vertices[current];
                    loop.Add(new Vector2(vertex.x, vertex.y));

                    if (!boundaryEdges.TryGetValue(current, out int next))
                    {
                        break;
                    }

                    current = next;
                }

                if (loop.Count >= 3)
                {
                    loops.Add(loop.ToArray());
                }
            }

            return loops;
        }
    }
}
