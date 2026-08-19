using UnityEngine;
using SDFTerrain.Meshing;
using SDFTerrain.Planet;

namespace SDFTerrain.Terrain
{
    /// <summary>
    /// Generates a planet's terrain and displays it: samples the TerrainField, builds a mesh via
    /// Marching Squares, and assigns it to a MeshFilter/MeshRenderer/PolygonCollider2D. Never edits
    /// the mesh/collider directly outside of a full rebuild from the SDF, per CLAUDE.md. A thin
    /// MonoBehaviour — all algorithmic work lives in TerrainFieldSampler/MarchingSquaresMesher/
    /// MeshDataConverter/TerrainColliderBuilder, kept testable without a live scene.
    /// </summary>
    [RequireComponent(typeof(MeshFilter))]
    [RequireComponent(typeof(MeshRenderer))]
    [RequireComponent(typeof(PolygonCollider2D))]
    public class TerrainRenderer : MonoBehaviour
    {
        [SerializeField] private int resolution = 128;
        [SerializeField] private float uvScale = 0.1f;
        [SerializeField] private Material material;
        [SerializeField] private bool drawDebugChunkBorders;
        [SerializeField] private bool drawDebugNormals;

        private Mesh _mesh;
        private ChunkGrid _chunkGrid;
        private TerrainField _field;
        private float _boundsRadius;

        /// <summary>Samples the given field and rebuilds the mesh/collider from scratch.</summary>
        public void Rebuild(TerrainField field, float boundsRadius, ChunkGrid chunkGrid = null)
        {
            if (field == null)
            {
                throw new System.ArgumentNullException(nameof(field));
            }

            _field = field;
            _boundsRadius = boundsRadius;
            _chunkGrid = chunkGrid;

            TerrainFieldSampler.Result sampled = TerrainFieldSampler.Sample(field, resolution, boundsRadius);
            MeshData meshData = MarchingSquaresMesher.Generate(sampled.Samples, sampled.CellSize, sampled.Origin, uvScale);

            _mesh = MeshDataConverter.ToUnityMesh(meshData, _mesh);
            GetComponent<MeshFilter>().sharedMesh = _mesh;

            var meshRenderer = GetComponent<MeshRenderer>();
            if (material != null)
            {
                meshRenderer.sharedMaterial = material;
            }

            TerrainColliderBuilder.Apply(meshData, GetComponent<PolygonCollider2D>());
        }

        private void OnDrawGizmos()
        {
            if (drawDebugChunkBorders && _chunkGrid != null)
            {
                DrawChunkBorders();
            }

            if (drawDebugNormals && _mesh != null)
            {
                DrawNormals();
            }
        }

        private void DrawChunkBorders()
        {
            Gizmos.color = Color.yellow;
            Vector3 origin = transform.position;

            float chunkSize = _chunkGrid.ChunkSize;
            float gridMinX = -(_chunkGrid.Cols * chunkSize) / 2f;
            float gridMaxX = (_chunkGrid.Cols * chunkSize) / 2f;
            float gridMinY = -(_chunkGrid.Rows * chunkSize) / 2f;
            float gridMaxY = (_chunkGrid.Rows * chunkSize) / 2f;

            // Draw vertical grid lines (chunk column boundaries)
            for (int col = 0; col <= _chunkGrid.Cols; col++)
            {
                float x = gridMinX + col * chunkSize;
                Gizmos.DrawLine(origin + new Vector3(x, gridMinY, 0f), origin + new Vector3(x, gridMaxY, 0f));
            }

            // Draw horizontal grid lines (chunk row boundaries)
            for (int row = 0; row <= _chunkGrid.Rows; row++)
            {
                float y = gridMinY + row * chunkSize;
                Gizmos.DrawLine(origin + new Vector3(gridMinX, y, 0f), origin + new Vector3(gridMaxX, y, 0f));
            }
        }

        private void DrawNormals()
        {
            Gizmos.color = Color.cyan;
            Vector3[] vertices = _mesh.vertices;
            Vector3[] normals = _mesh.normals;

            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 worldVertex = transform.TransformPoint(vertices[i]);
                Gizmos.DrawLine(worldVertex, worldVertex + normals[i] * 0.5f);
            }
        }
    }
}
