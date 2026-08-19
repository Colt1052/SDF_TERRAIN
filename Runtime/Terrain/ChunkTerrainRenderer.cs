using System;
using System.Collections.Generic;
using UnityEngine;
using SDFTerrain.Meshing;

namespace SDFTerrain.Terrain
{
    /// <summary>
    /// Renders a planet's terrain as one mesh/collider per chunk, rebuilding only the chunks a
    /// <see cref="ChunkGrid"/> reports dirty (per CLAUDE.md/TASKS.md "never regenerate an entire
    /// planet; only dirty chunks rebuild"). Complements <see cref="TerrainRenderer"/> (Task 11),
    /// which rebuilds the whole planet as one mesh; this is the chunk-local counterpart introduced
    /// for Task 15. Each chunk gets its own child GameObject holding a MeshFilter/MeshRenderer/
    /// PolygonCollider2D so meshing/collider work for one chunk never touches another's mesh data.
    /// </summary>
    public class ChunkTerrainRenderer : MonoBehaviour
    {
        [SerializeField] private float cellSize = 0.5f;
        [SerializeField] private float uvScale = 0.1f;
        [SerializeField] private Material material;

        public float CellSize => cellSize;

        /// <summary>
        /// Raised after dirty chunks are rebuilt (including via <see cref="ApplyBrush"/>), so
        /// debug views sampling the same field (e.g. <see cref="SDFDebugView"/>,
        /// <see cref="MarchingSquaresGridDebugView"/>) know to re-sample rather than keep showing
        /// stale pre-edit data.
        /// </summary>
        public event Action TerrainChanged;

        private TerrainField _field;
        private ChunkGrid _chunkGrid;
        private ChunkSeamCache _seamCache;
        private float _maxRadius;
        private ChunkView[] _chunkViews;
        private readonly List<int> _dirtyRangeBuffer = new List<int>();

        private class ChunkView
        {
            public GameObject GameObject;
            public MeshFilter MeshFilter;
            public MeshRenderer MeshRenderer;
            public PolygonCollider2D Collider;
            public Mesh Mesh;
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
            _seamCache = new ChunkSeamCache(chunkGrid);
            _maxRadius = maxRadius;

            _field.EnableChunkIndexing(_chunkGrid);

            DestroyExistingChunkViews();
            _chunkViews = new ChunkView[chunkGrid.ChunkCount];
            for (int i = 0; i < chunkGrid.ChunkCount; i++)
            {
                _chunkViews[i] = CreateChunkView(i);
            }
        }

        /// <summary>
        /// Rebuilds only the chunks the grid currently reports dirty, then clears their dirty
        /// flags. Chunks not reported dirty keep their existing mesh/collider untouched.
        /// </summary>
        public void RebuildDirtyChunks()
        {
            if (_chunkViews == null)
            {
                throw new InvalidOperationException("RebuildDirtyChunks requires Initialize to have been called first.");
            }

            foreach (TerrainChunk chunk in _chunkGrid.DirtyChunks())
            {
                RebuildChunk(chunk);
            }

            _chunkGrid.ClearAllDirty();
            TerrainChanged?.Invoke();
        }

        /// <summary>
        /// Applies a brush stroke to the field this renderer was initialized with: persists the
        /// resulting edit, marks every chunk the stroke's footprint overlaps dirty, and rebuilds
        /// them. Per CLAUDE.md's "SDF is the source of truth" rule, the brush never touches a
        /// mesh/collider directly — it mutates the field and triggers a derive of only the
        /// affected chunks.
        /// </summary>
        public void ApplyBrush(TerrainBrush brush, Vector2 localPosition)
        {
            if (_chunkViews == null)
            {
                throw new InvalidOperationException("ApplyBrush requires Initialize to have been called first.");
            }

            TerrainEdit edit = brush.ToEdit(localPosition);
            _field.ApplyEdit(edit);

            (float minAngle, float maxAngle) = _field.AffectedAngleRange(localPosition, brush.Radius);
            MarkDirtyRange(minAngle, maxAngle);

            RebuildDirtyChunks();
        }

        /// <summary>
        /// Marks every chunk overlapping the angular range [minAngle, maxAngle] dirty, plus the
        /// chunk immediately outside each end of that span. Walks chunk boundaries rather than an
        /// arbitrary angle stride so it can never skip a chunk regardless of chunk count or brush
        /// radius. A full-circle range (minAngle == 0 and maxAngle == 2*PI, as returned by
        /// AffectedAngleRange when the brush reaches the planet's center) marks every chunk.
        ///
        /// The one-chunk-further-out padding matters because CartesianChunkFieldSampler samples
        /// each chunk's lattice a cell beyond its own strict [StartAngle, EndAngle) wedge (so its
        /// boundary cells have a neighbor sample to interpolate against) — an edit whose angular
        /// footprint never crosses a neighbor's strict wedge can still land inside that neighbor's
        /// padded margin and change what it would render there. Without also rebuilding that
        /// neighbor, its mesh goes stale right at the shared edge: a brush stroke that stays fully
        /// inside one chunk but close to the border would visibly seam against the next chunk's
        /// unrebuilt boundary.
        /// </summary>
        private void MarkDirtyRange(float minAngle, float maxAngle)
        {
            _chunkGrid.ChunksInRange(minAngle, maxAngle, _dirtyRangeBuffer);
            for (int i = 0; i < _dirtyRangeBuffer.Count; i++)
            {
                _chunkGrid.GetChunk(_dirtyRangeBuffer[i]).MarkDirty();
            }

            if (_dirtyRangeBuffer.Count > 0 && _dirtyRangeBuffer.Count < _chunkGrid.ChunkCount)
            {
                int first = _dirtyRangeBuffer[0];
                int last = _dirtyRangeBuffer[_dirtyRangeBuffer.Count - 1];
                _chunkGrid.GetPreviousChunk(first).MarkDirty();
                _chunkGrid.GetNextChunk(last).MarkDirty();
            }
        }

        private void RebuildChunk(TerrainChunk chunk)
        {
            ChunkView view = _chunkViews[chunk.Index];

            CartesianChunkFieldSampler.Result sampled = CartesianChunkFieldSampler.Sample(_field, chunk, _maxRadius, cellSize, _seamCache);
            MeshData meshData = MarchingSquaresMesher.Generate(sampled.Samples, sampled.Positions, uvScale);

            view.Mesh = MeshDataConverter.ToUnityMesh(meshData, view.Mesh);
            view.MeshFilter.sharedMesh = view.Mesh;

            if (material != null)
            {
                view.MeshRenderer.sharedMaterial = material;
            }

            TerrainColliderBuilder.Apply(meshData, view.Collider);
        }

        private ChunkView CreateChunkView(int index)
        {
            var chunkObject = new GameObject($"Chunk_{index}");
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
            if (_chunkViews == null)
            {
                return;
            }

            foreach (ChunkView view in _chunkViews)
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

        private void OnDestroy()
        {
            DestroyExistingChunkViews();
        }
    }
}
