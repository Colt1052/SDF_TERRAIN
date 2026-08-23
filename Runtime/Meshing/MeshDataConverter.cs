using System;
using UnityEngine;

namespace SDFTerrain.Meshing
{
    /// <summary>
    /// Converts plain <see cref="MeshData"/> into an actual <see cref="UnityEngine.Mesh"/>. Kept
    /// separate from <see cref="MarchingSquaresMesher"/> so the meshing algorithm itself stays
    /// free of any Unity asset dependency and remains testable with plain data.
    /// </summary>
    public static class MeshDataConverter
    {
        /// <summary>
        /// Builds a new Mesh from the given data, or overwrites <paramref name="reuse"/> if
        /// provided (avoids an allocation per rebuild when a chunk's mesh is regenerated).
        /// </summary>
        public static Mesh ToUnityMesh(MeshData meshData, Mesh reuse = null)
        {
            if (meshData == null)
            {
                throw new ArgumentNullException(nameof(meshData));
            }

            Mesh mesh = reuse != null ? reuse : new Mesh();
            mesh.Clear();

            // Terrain meshes don't share vertices between triangles, so a fine/large chunk or
            // whole-planet mesh can easily exceed the 16-bit index format's ~65k vertex limit
            // (a fully-solid NxN grid emits up to N*N*6 vertices). Above that limit Unity silently
            // drops/wraps triangles instead of erroring, which looks like a partially-missing mesh.
            mesh.indexFormat = meshData.Vertices.Count > 65535
                ? UnityEngine.Rendering.IndexFormat.UInt32
                : UnityEngine.Rendering.IndexFormat.UInt16;

            mesh.SetVertices(meshData.Vertices);
            mesh.SetTriangles(meshData.Triangles, 0);
            mesh.SetNormals(meshData.Normals);
            mesh.SetUVs(0, meshData.UVs);

            if (meshData.Colors.Count == meshData.Vertices.Count)
            {
                mesh.SetColors(meshData.Colors);
            }

            mesh.RecalculateBounds();

            return mesh;
        }
    }
}
