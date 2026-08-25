using System;
using System.Collections.Generic;
using UnityEngine;
using SDFTerrain.Meshing;
using SDFTerrain.Materials;

namespace SDFTerrain.Terrain
{
    /// <summary>
    /// Renders a planet's terrain as one mesh/collider per chunk, rebuilding only the chunks a
    /// <see cref="ChunkGrid"/> reports dirty (per CLAUDE.md/TASKS.md "never regenerate an entire
    /// planet; only dirty chunks rebuild"). Complements <see cref="TerrainRenderer"/> (Task 11),
    /// which rebuilds the whole planet as one mesh; this is the chunk-local counterpart introduced
    /// for Task 15. Each chunk gets its own child GameObject holding a MeshFilter/MeshRenderer/
    /// PolygonCollider2D so meshing/collider work for one chunk never touches another's mesh data.
    /// Supports dynamic chunk expansion — when an edit targets a region with no existing chunk,
    /// new chunks are created and rendered on demand.
    /// </summary>
    public class ChunkTerrainRenderer : MonoBehaviour
    {
        [SerializeField] private float cellSize = 0.5f;
        [SerializeField] private float uvScale = 0.1f;
        [SerializeField] private Material material;

        /// <summary>
        /// Optional geological profile for vertex-color rendering. When assigned, each vertex is
        /// colored according to the material at that depth (dirt, stone, mantle, etc.). When null,
        /// vertices are white and the material's shader tint provides all color.
        /// </summary>
        [Tooltip("When assigned, terrain vertices are colored by geological layer (dirt, stone, mantle, etc.)")]
        [SerializeField] private GeologicalProfile geologicalProfile;

        /// <summary>
        /// Optional authoritative material layer. When assigned, material colors come from
        /// <see cref="MaterialLayer"/> (which includes player edits + geological fallback).
        /// Takes precedence over <see cref="geologicalProfile"/>.
        /// </summary>
        [Tooltip("When assigned, terrain vertices are colored by material state (edits + geology)")]
        [SerializeField] private MaterialLayer materialLayer;

        public float CellSize => cellSize;

        /// <summary>
        /// Configures the geological profile for vertex-color rendering. Call before
        /// <see cref="RebuildDirtyChunks"/> so the next rebuild samples layer colors.
        /// </summary>
        public void SetGeologicalProfile(GeologicalProfile profile)
        {
            geologicalProfile = profile;
        }

        /// <summary>
        /// Configures the material layer for vertex-color rendering. Takes precedence over
        /// <see cref="geologicalProfile"/> when both are set.
        /// </summary>
        public void SetMaterialLayer(MaterialLayer layer)
        {
            materialLayer = layer;
        }

        /// <summary>
        /// Replaces the MeshRenderer's shared material. Useful for runtime demos that
        /// create a Material from a shader without having a .mat asset on disk.
        /// </summary>
        public void SetMaterial(Material mat)
        {
            material = mat;
        }

        /// <summary>
        /// Raised after dirty chunks are rebuilt (including via <see cref="ApplyBrush"/>), so
        /// debug views sampling the same field (e.g. <see cref="SDFDebugView"/>,
        /// <see cref="MarchingSquaresGridDebugView"/>) know to re-sample rather than keep showing
        /// stale pre-edit data.
        /// </summary>
        public event Action TerrainChanged;

        private TerrainField _field;
        private ChunkGrid _chunkGrid;

        /// <summary>The terrain field this renderer was initialized with. Null before Initialize.</summary>
        public TerrainField Field => _field;

        /// <summary>
        /// Returns the chunk index that contains <paramref name="localPosition"/>.
        /// Requires <see cref="Initialize"/> to have been called.
        /// </summary>
        public int GetChunkIndex(Vector2 localPosition) => _chunkGrid.GetChunkAt(localPosition).Index;

        private readonly Dictionary<int, ChunkView> _chunkViews = new Dictionary<int, ChunkView>();
        private readonly List<int> _rectBuffer = new List<int>();

        private class ChunkView
        {
            public GameObject GameObject;
            public MeshFilter MeshFilter;
            public MeshRenderer MeshRenderer;
            public PolygonCollider2D Collider;
            public Mesh Mesh;
            public float SolidArea;
        }

        /// <summary>
        /// Prepares one renderer child per chunk in the grid. Must be called once before the first
        /// <see cref="RebuildDirtyChunks"/>; all chunks start dirty (see TerrainChunk), so the first
        /// call rebuilds every chunk.
        /// </summary>
        public void Initialize(TerrainField field, ChunkGrid chunkGrid, float maxRadius)
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

            _field = field;
            _chunkGrid = chunkGrid;

            _field.EnableChunkIndexing(_chunkGrid);

            DestroyExistingChunkViews();
            _chunkViews.Clear();

            foreach (TerrainChunk chunk in chunkGrid.AllChunks)
            {
                _chunkViews[chunk.Index] = CreateChunkView(chunk);
            }
        }

        /// <summary>
        /// Rebuilds only the chunks the grid currently reports dirty, then clears their dirty
        /// flags. Chunks not reported dirty keep their existing mesh/collider untouched.
        /// Chunks that produce no geometry are removed entirely (GameObject destroyed, chunk
        /// removed from grid) to keep the scene graph lean.
        /// </summary>
        public void RebuildDirtyChunks()
        {
            if (_chunkViews.Count == 0)
            {
                throw new InvalidOperationException("RebuildDirtyChunks requires Initialize to have been called first.");
            }

            // Collect empty chunk indices during rebuild so we can remove them after
            // (modifying the grid during DirtyChunks() iteration would invalidate the enumerator).
            List<int> emptyChunkIndices = new List<int>();

            foreach (TerrainChunk chunk in _chunkGrid.DirtyChunks())
            {
                if (RebuildChunk(chunk))
                {
                    emptyChunkIndices.Add(chunk.Index);
                }
            }

            // Remove chunks that produced no geometry.
            for (int i = 0; i < emptyChunkIndices.Count; i++)
            {
                RemoveEmptyChunk(emptyChunkIndices[i]);
            }

            _chunkGrid.ClearAllDirty();
            TerrainChanged?.Invoke();
        }

        /// <summary>
        /// Applies a brush stroke to the field this renderer was initialized with: persists the
        /// resulting edit, marks every chunk the stroke's footprint overlaps dirty (creating new
        /// chunks only for build brushes; delete/smooth brushes don't expand into empty space), and
        /// rebuilds them. Per CLAUDE.md's "SDF is the source of truth" rule, the brush never
        /// touches a mesh/collider directly — it mutates the field and triggers a derive of only
        /// the affected chunks.
        /// </summary>
        /// <returns>
        /// A <see cref="BrushAreaDelta"/> describing the solid area change. AreaRemoved > 0 means
        /// terrain was carved (resource reward). AreaAdded > 0 means terrain was built (resource
        /// cost). WasApplied is false if the brush was a no-op (e.g., Electric found no surface).
        /// </returns>
        public BrushAreaDelta ApplyBrush(TerrainBrush brush, Vector2 localPosition)
        {
            return ApplyBrush(brush, localPosition, strikeRadius: 1f, searchRayCount: 36);
        }

        /// <summary>
        /// Overload of <see cref="ApplyBrush"/> that accepts parameters for <see cref="BrushMode.Electric"/>:
        /// <paramref name="strikeRadius"/> controls crater size, <paramref name="searchRayCount"/> controls
        /// how many rays are cast when searching for terrain.
        /// </summary>
        /// <returns>A <see cref="BrushAreaDelta"/> describing the solid area change.</returns>
        public BrushAreaDelta ApplyBrush(TerrainBrush brush, Vector2 localPosition, float strikeRadius, int searchRayCount = 36)
        {
            if (_chunkViews.Count == 0)
            {
                throw new InvalidOperationException("ApplyBrush requires Initialize to have been called first.");
            }

            if (strikeRadius <= 0f)
            {
                strikeRadius = brush.Radius;
            }

            // Measure total world area before the edit.
            // Using total area (not per-chunk) guarantees correctness regardless of chunk
            // creation (Add mode) or removal (empty chunks destroyed after Remove).
            float solidAreaBefore = GetTotalSolidArea();

            // Determine which chunks will be affected and their bounds rectangle.
            // For Electric mode, pre-finds the strike point to know which chunks to mark dirty.
            bool wasApplied = false;
            GetBrushBounds(brush, localPosition, strikeRadius, searchRayCount,
                out float brushMinX, out float brushMaxX, out float brushMinY, out float brushMaxY,
                ref wasApplied);

            // Apply the edit and mark chunks dirty.
            ApplyEditAndMarkDirty(brush, localPosition, strikeRadius, searchRayCount,
                brushMinX, brushMaxX, brushMinY, brushMaxY, ref wasApplied);

            // Rebuild dirty chunks (fires TerrainChanged, sets ChunkView.SolidArea).
            RebuildDirtyChunks();

            // Measure total world area after the edit.
            float solidAreaAfter = GetTotalSolidArea();

            float delta = solidAreaBefore - solidAreaAfter;
            if (delta > 0f)
                return new BrushAreaDelta(areaRemoved: delta, areaAdded: 0f, wasApplied: wasApplied);
            if (delta < 0f)
                return new BrushAreaDelta(areaRemoved: 0f, areaAdded: -delta, wasApplied: wasApplied);
            return new BrushAreaDelta(areaRemoved: 0f, areaAdded: 0f, wasApplied: wasApplied);
        }

        /// <summary>
        /// Determines the bounding rectangle of the brush footprint so that
        /// <see cref="MarkDirtyRect"/> knows which chunks to mark dirty.
        /// For Electric mode, pre-finds the surface strike point.
        /// </summary>
        private void GetBrushBounds(TerrainBrush brush, Vector2 localPosition,
            float strikeRadius, int searchRayCount,
            out float minX, out float maxX, out float minY, out float maxY,
            ref bool wasApplied)
        {
            if (brush.Mode == BrushMode.Electric)
            {
                if (_field.FindNearestSurface(localPosition, brush.Radius, searchRayCount, out Vector2 strikePoint))
                {
                    wasApplied = true;
                    minX = strikePoint.x - strikeRadius;
                    maxX = strikePoint.x + strikeRadius;
                    minY = strikePoint.y - strikeRadius;
                    maxY = strikePoint.y + strikeRadius;
                }
                else
                {
                    wasApplied = false;
                    minX = maxX = localPosition.x;
                    minY = maxY = localPosition.y;
                }
            }
            else
            {
                wasApplied = true;
                minX = localPosition.x - brush.Radius;
                maxX = localPosition.x + brush.Radius;
                minY = localPosition.y - brush.Radius;
                maxY = localPosition.y + brush.Radius;
            }
        }

        /// <summary>
        /// Applies the brush edit to the field and marks affected chunks dirty.
        /// Does NOT rebuild — caller is responsible for calling RebuildDirtyChunks.
        /// </summary>
        private void ApplyEditAndMarkDirty(TerrainBrush brush, Vector2 localPosition,
            float strikeRadius, int searchRayCount,
            float brushMinX, float brushMaxX, float brushMinY, float brushMaxY,
            ref bool wasApplied)
        {
            switch (brush.Mode)
            {
                case BrushMode.Add:
                    {
                        TerrainEdit edit = brush.ToEdit(localPosition);
                        _field.ApplyEdit(edit);
                        MarkDirtyRect(brushMinX, brushMaxX, brushMinY, brushMaxY, createChunks: true);
                        break;
                    }
                case BrushMode.Remove:
                    {
                        TerrainEdit edit = brush.ToEdit(localPosition);
                        _field.ApplyEdit(edit);
                        MarkDirtyRect(brushMinX, brushMaxX, brushMinY, brushMaxY, createChunks: false);
                        break;
                    }
                case BrushMode.Smooth:
                    {
                        _field.SmoothEdits(localPosition, brush.Radius);
                        MarkDirtyRect(brushMinX, brushMaxX, brushMinY, brushMaxY, createChunks: false);
                        break;
                    }
                case BrushMode.Electric:
                    {
                        if (_field.FindNearestSurface(localPosition, brush.Radius, searchRayCount, out Vector2 strikePoint))
                        {
                            TerrainEdit edit = new TerrainEdit(strikePoint, strikeRadius, isAdditive: true);
                            _field.ApplyEdit(edit);

                            float strikeMinX = strikePoint.x - strikeRadius;
                            float strikeMaxX = strikePoint.x + strikeRadius;
                            float strikeMinY = strikePoint.y - strikeRadius;
                            float strikeMaxY = strikePoint.y + strikeRadius;
                            MarkDirtyRect(strikeMinX, strikeMaxX, strikeMinY, strikeMaxY, createChunks: false);
                        }
                        else
                        {
                            wasApplied = false;
                        }
                        break;
                    }
            }
        }

        /// <summary>
        /// Marks every chunk whose bounding box overlaps the given rectangle dirty and rebuilds them.
        /// Optionally creates new chunks if the rectangle extends beyond existing coverage.
        /// Public so that custom <see cref="BrushBehavior"/> implementations (e.g. <see cref="Brush.SmoothBrushBehavior"/>)
        /// can mark regions dirty and trigger a rebuild without adding a <see cref="TerrainEdit"/>.
        /// </summary>
        public void MarkDirtyRectAndRebuild(float minX, float maxX, float minY, float maxY, bool createChunks)
        {
            MarkDirtyRect(minX, maxX, minY, maxY, createChunks);
            RebuildDirtyChunks();
        }

        /// <summary>
        /// Marks every chunk whose bounding box overlaps the given rectangle dirty.
        /// Optionally creates new chunks if the rectangle extends beyond existing coverage.
        /// </summary>
        private void MarkDirtyRect(float minX, float maxX, float minY, float maxY, bool createChunks)
        {
            _chunkGrid.ChunksInRect(minX, maxX, minY, maxY, _rectBuffer, createChunks);
            for (int i = 0; i < _rectBuffer.Count; i++)
            {
                int chunkIndex = _rectBuffer[i];
                _chunkGrid.GetChunk(chunkIndex).MarkDirty();
            }
        }

        /// <summary>
        /// Rebuilds the mesh and collider for a chunk.
        /// </summary>
        /// <returns>True if the chunk produced no geometry (empty); false if it has content.</returns>
        private bool RebuildChunk(TerrainChunk chunk)
        {
            // Ensure a ChunkView exists (may be a dynamically created chunk).
            if (!_chunkViews.TryGetValue(chunk.Index, out ChunkView view))
            {
                view = CreateChunkView(chunk);
                _chunkViews[chunk.Index] = view;
            }

            CartesianChunkFieldSampler.Result sampled = CartesianChunkFieldSampler.Sample(_field, chunk, cellSize);

            // If a material layer is assigned, use it (takes precedence over geological profile).
            // If only geological profile is assigned, fall back to legacy geological sampling.
            Color[,] vertexColors = null;
            if (materialLayer != null)
            {
                int width = sampled.Samples.GetLength(0);
                int height = sampled.Samples.GetLength(1);
                vertexColors = new Color[width, height];

                for (int x = 0; x < width; x++)
                {
                    for (int y = 0; y < height; y++)
                    {
                        Vector2 pos = sampled.Positions[x, y];
                        float sdf = sampled.Samples[x, y];
                        if (sdf > 0f)
                        {
                            vertexColors[x, y] = MaterialColorMap.GetColor(MaterialId.Air);
                        }
                        else
                        {
                            var sample = materialLayer.Sample(_field, pos, chunk.Index);
                            vertexColors[x, y] = MaterialColorMap.GetColor(sample.MaterialId, materialLayer.GetDatabase());
                        }
                    }
                }
            }
            else if (geologicalProfile != null)
            {
                int width = sampled.Samples.GetLength(0);
                int height = sampled.Samples.GetLength(1);
                vertexColors = new Color[width, height];

                for (int x = 0; x < width; x++)
                {
                    for (int y = 0; y < height; y++)
                    {
                        string matId = GeologicalLayerGenerator.SampleId(_field, sampled.Positions[x, y], geologicalProfile);
                        vertexColors[x, y] = MaterialColorMap.GetColor(matId);
                    }
                }
            }

            MeshData meshData = MarchingSquaresMesher.Generate(sampled.Samples, sampled.Positions, vertexColors, uvScale);

            // Check if the chunk is empty (no geometry produced).
            bool isEmpty = meshData.Vertices.Count == 0;

            if (!isEmpty)
            {
                view.SolidArea = MarchingSquaresMesher.ComputeSolidArea(meshData);
                view.Mesh = MeshDataConverter.ToUnityMesh(meshData, view.Mesh);
                view.MeshFilter.sharedMesh = view.Mesh;

                if (material != null)
                {
                    view.MeshRenderer.sharedMaterial = material;
                }

                TerrainColliderBuilder.Apply(meshData, view.Collider);
            }
            else
            {
                view.SolidArea = 0f;
            }

            return isEmpty;
        }

        private ChunkView CreateChunkView(TerrainChunk chunk)
        {
            var chunkObject = new GameObject($"Chunk_{chunk.Index}");
            chunkObject.transform.SetParent(transform, worldPositionStays: false);

            return new ChunkView
            {
                GameObject = chunkObject,
                MeshFilter = chunkObject.AddComponent<MeshFilter>(),
                MeshRenderer = chunkObject.AddComponent<MeshRenderer>(),
                Collider = chunkObject.AddComponent<PolygonCollider2D>(),
            };
        }

        private void DestroyExistingChunkViews()
        {
            foreach (ChunkView view in _chunkViews.Values)
            {
                if (view.GameObject == null)
                {
                    continue;
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

        /// <summary>
        /// Removes an empty chunk: destroys its GameObject, removes it from the view dictionary,
        /// and removes it from the chunk grid.
        /// </summary>
        private void RemoveEmptyChunk(int chunkIndex)
        {
            if (!_chunkViews.TryGetValue(chunkIndex, out ChunkView view))
            {
                return;
            }

            // Destroy the GameObject (mesh, renderer, collider).
            if (view.GameObject != null)
            {
                if (Application.isPlaying)
                {
                    Destroy(view.GameObject);
                }
                else
                {
                    DestroyImmediate(view.GameObject);
                }
            }

            // Get chunk coordinates before removing from view dictionary.
            TerrainChunk chunk = _chunkGrid.GetChunk(chunkIndex);

            // Remove from tracking structures.
            _chunkViews.Remove(chunkIndex);
            _chunkGrid.RemoveChunkAtGrid(chunk.Col, chunk.Row);
        }

        /// <summary>
        /// Returns the total solid area across all active chunk meshes.
        /// Useful for UI overlays that display world-state statistics.
        /// </summary>
        public float GetTotalSolidArea()
        {
            float total = 0f;
            foreach (ChunkView view in _chunkViews.Values)
            {
                total += view.SolidArea;
            }
            return total;
        }

        private void OnDestroy()
        {
            DestroyExistingChunkViews();
        }
    }
}
