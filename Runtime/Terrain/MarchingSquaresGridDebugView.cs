using System;
using System.Collections.Generic;
using UnityEngine;
using SDFTerrain.Meshing;

namespace SDFTerrain.Terrain
{
    /// <summary>
    /// Runtime-toggleable visualization of the actual Marching Squares sampling grid used to build
    /// terrain meshes: lattice grid lines, corner samples colored solid/air, and the edge
    /// zero-crossing points the mesher places mesh vertices at. Samples via
    /// <see cref="CartesianChunkFieldSampler"/> and computes crossings via
    /// <see cref="MarchingSquaresMesher.FindEdgeCrossing"/> — the same calls
    /// <see cref="ChunkTerrainRenderer"/> itself makes — so this is a genuine visual check of the
    /// real algorithm, not a re-derived approximation of it (per CLAUDE.md's "never build black
    /// box systems").
    /// </summary>
    public class MarchingSquaresGridDebugView : MonoBehaviour
    {
        [SerializeField] private bool visible;
        [SerializeField] private Color gridColor = Color.white;
        [SerializeField] private Color solidCornerColor = new Color(0.2f, 0.4f, 1f);
        [SerializeField] private Color airCornerColor = new Color(1f, 0.3f, 0.3f);
        [SerializeField] private Color crossingColor = Color.yellow;

        private TerrainField _field;
        private ChunkGrid _chunkGrid;
        private ChunkSeamCache _seamCache;
        private float _maxRadius;
        private float _cellSize;
        private bool _initialized;

        private ChildView _gridLines;
        private ChildView _cornerMarkers;
        private ChildView _crossingMarkers;

        private class ChildView
        {
            public GameObject GameObject;
            public MeshFilter MeshFilter;
            public MeshRenderer MeshRenderer;
            public Mesh Mesh;
        }

        public void Initialize(TerrainField field, ChunkGrid chunkGrid, float maxRadius, float cellSize)
        {
            if (field == null)
            {
                throw new ArgumentNullException(nameof(field));
            }

            if (chunkGrid == null)
            {
                throw new ArgumentNullException(nameof(chunkGrid));
            }

            if (maxRadius <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(maxRadius), maxRadius, "Max radius must be positive.");
            }

            if (cellSize <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(cellSize), cellSize, "Cell size must be positive.");
            }

            _field = field;
            _chunkGrid = chunkGrid;
            _seamCache = new ChunkSeamCache(chunkGrid);
            _maxRadius = maxRadius;
            _cellSize = cellSize;
            _initialized = true;

            _gridLines = _gridLines ?? CreateChildView("GridLines");
            _cornerMarkers = _cornerMarkers ?? CreateChildView("CornerMarkers");
            _crossingMarkers = _crossingMarkers ?? CreateChildView("CrossingMarkers");

            ApplyVisibility();
        }

        /// <summary>Re-samples every chunk and rebuilds the grid/corner/crossing meshes.</summary>
        public void Refresh()
        {
            if (!_initialized)
            {
                throw new InvalidOperationException("Refresh requires Initialize to have been called first.");
            }

            var gridVertices = new List<Vector3>();
            var cornerVertices = new List<Vector3>();
            var cornerColors = new List<Color>();
            var crossingVertices = new List<Vector3>();

            float markerSize = _cellSize * 0.15f;

            for (int i = 0; i < _chunkGrid.ChunkCount; i++)
            {
                TerrainChunk chunk = _chunkGrid.GetChunk(i);
                CartesianChunkFieldSampler.Result sampled = CartesianChunkFieldSampler.Sample(_field, chunk, _maxRadius, _cellSize, _seamCache);

                CollectChunkGeometry(sampled, markerSize, solidCornerColor, airCornerColor, gridVertices, cornerVertices, cornerColors, crossingVertices);
            }

            BuildLineMesh(_gridLines.Mesh, gridVertices, gridColor);
            BuildQuadMesh(_cornerMarkers.Mesh, cornerVertices, cornerColors);
            BuildFlatQuadMesh(_crossingMarkers.Mesh, crossingVertices, crossingColor);

            _gridLines.MeshFilter.sharedMesh = _gridLines.Mesh;
            _cornerMarkers.MeshFilter.sharedMesh = _cornerMarkers.Mesh;
            _crossingMarkers.MeshFilter.sharedMesh = _crossingMarkers.Mesh;

            EnsureMaterial(_gridLines);
            EnsureMaterial(_cornerMarkers);
            EnsureMaterial(_crossingMarkers);
        }

        private static void CollectChunkGeometry(
            CartesianChunkFieldSampler.Result sampled,
            float markerSize,
            Color solidCornerColor,
            Color airCornerColor,
            List<Vector3> gridVertices,
            List<Vector3> cornerVertices,
            List<Color> cornerColors,
            List<Vector3> crossingVertices)
        {
            float[,] samples = sampled.Samples;
            Vector2[,] positions = sampled.Positions;
            int width = samples.GetLength(0);
            int height = samples.GetLength(1);

            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    Vector2 p = positions[x, y];
                    float v = samples[x, y];

                    AddQuad(cornerVertices, p, markerSize);
                    Color color = v < 0f ? solidCornerColor : airCornerColor;
                    for (int c = 0; c < 6; c++)
                    {
                        cornerColors.Add(color);
                    }
                }
            }

            for (int x = 0; x < width - 1; x++)
            {
                for (int y = 0; y < height - 1; y++)
                {
                    Vector2 p0 = positions[x, y];
                    Vector2 p1 = positions[x + 1, y];
                    Vector2 p2 = positions[x + 1, y + 1];
                    Vector2 p3 = positions[x, y + 1];
                    float v0 = samples[x, y];
                    float v1 = samples[x + 1, y];
                    float v2 = samples[x + 1, y + 1];
                    float v3 = samples[x, y + 1];

                    AddLine(gridVertices, p0, p1);
                    AddLine(gridVertices, p1, p2);
                    AddLine(gridVertices, p2, p3);
                    AddLine(gridVertices, p3, p0);

                    AddCrossingIfPresent(crossingVertices, p0, p1, v0, v1, markerSize);
                    AddCrossingIfPresent(crossingVertices, p1, p2, v1, v2, markerSize);
                    AddCrossingIfPresent(crossingVertices, p2, p3, v2, v3, markerSize);
                    AddCrossingIfPresent(crossingVertices, p3, p0, v3, v0, markerSize);
                }
            }
        }

        private static void AddCrossingIfPresent(List<Vector3> crossingVertices, Vector2 a, Vector2 b, float valueA, float valueB, float markerSize)
        {
            Vector2? crossing = MarchingSquaresMesher.FindEdgeCrossing(a, b, valueA, valueB);
            if (crossing.HasValue)
            {
                AddQuad(crossingVertices, crossing.Value, markerSize);
            }
        }

        private static void AddLine(List<Vector3> vertices, Vector2 a, Vector2 b)
        {
            vertices.Add(new Vector3(a.x, a.y, 0f));
            vertices.Add(new Vector3(b.x, b.y, 0f));
        }

        private static void AddQuad(List<Vector3> vertices, Vector2 center, float halfSize)
        {
            Vector3 a = new Vector3(center.x - halfSize, center.y - halfSize, 0f);
            Vector3 b = new Vector3(center.x + halfSize, center.y - halfSize, 0f);
            Vector3 c = new Vector3(center.x + halfSize, center.y + halfSize, 0f);
            Vector3 d = new Vector3(center.x - halfSize, center.y + halfSize, 0f);

            vertices.Add(a);
            vertices.Add(b);
            vertices.Add(c);
            vertices.Add(a);
            vertices.Add(c);
            vertices.Add(d);
        }

        private static void BuildLineMesh(Mesh mesh, List<Vector3> vertices, Color color)
        {
            mesh.Clear();
            mesh.SetVertices(vertices);

            var colors = new Color[vertices.Count];
            for (int i = 0; i < colors.Length; i++)
            {
                colors[i] = color;
            }
            mesh.SetColors(colors);

            var indices = new int[vertices.Count];
            for (int i = 0; i < indices.Length; i++)
            {
                indices[i] = i;
            }
            mesh.SetIndices(indices, MeshTopology.Lines, 0);
            mesh.RecalculateBounds();
        }

        private static void BuildQuadMesh(Mesh mesh, List<Vector3> vertices, List<Color> colors)
        {
            mesh.Clear();
            mesh.SetVertices(vertices);
            mesh.SetColors(colors);

            var triangles = new int[vertices.Count];
            for (int i = 0; i < triangles.Length; i++)
            {
                triangles[i] = i;
            }
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
        }

        private static void BuildFlatQuadMesh(Mesh mesh, List<Vector3> vertices, Color color)
        {
            var colors = new List<Color>(vertices.Count);
            for (int i = 0; i < vertices.Count; i++)
            {
                colors.Add(color);
            }

            BuildQuadMesh(mesh, vertices, colors);
        }

        /// <summary>Re-samples if currently visible; no-op otherwise. Call after the field changes.</summary>
        public void NotifyTerrainChanged()
        {
            if (visible)
            {
                Refresh();
            }
        }

        private void OnValidate()
        {
            if (_initialized)
            {
                ApplyVisibility();
            }
        }

        private void ApplyVisibility()
        {
            if (visible)
            {
                Refresh();
            }

            _gridLines.MeshRenderer.enabled = visible;
            _cornerMarkers.MeshRenderer.enabled = visible;
            _crossingMarkers.MeshRenderer.enabled = visible;
        }

        private void EnsureMaterial(ChildView view)
        {
            if (view.MeshRenderer.sharedMaterial != null)
            {
                return;
            }

            Shader shader = Shader.Find("Sprites/Default");
            view.MeshRenderer.material = new Material(shader);
        }

        private ChildView CreateChildView(string name)
        {
            var childObject = new GameObject(name);
            childObject.transform.SetParent(transform, worldPositionStays: false);

            var mesh = new Mesh { name = $"{name}Mesh" };
            // A single mesh here aggregates every chunk's geometry (unlike ChunkTerrainRenderer,
            // which keeps one small mesh per chunk), so vertex counts routinely exceed the 16-bit
            // index format's 65535 limit and would otherwise silently corrupt/truncate later chunks.
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;

            return new ChildView
            {
                GameObject = childObject,
                MeshFilter = childObject.AddComponent<MeshFilter>(),
                MeshRenderer = childObject.AddComponent<MeshRenderer>(),
                Mesh = mesh,
            };
        }

        private void OnDestroy()
        {
            DestroyChildView(_gridLines);
            DestroyChildView(_cornerMarkers);
            DestroyChildView(_crossingMarkers);
        }

        private void DestroyChildView(ChildView view)
        {
            if (view?.GameObject == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(view.GameObject);
            }
            else
            {
                DestroyImmediate(view.GameObject);
            }
        }
    }
}
