using System;
using System.Collections.Generic;
using UnityEngine;

namespace SDFTerrain.Terrain
{
    /// <summary>
    /// The authoritative signed distance field for a planet's terrain. Sampling returns negative
    /// distance inside solid ground, positive in open air, and zero at the surface. The base
    /// field is a sphere optionally perturbed by deterministic height noise as a function of
    /// angle (large-scale shape + terrain height, per generation order — layers/caves are later
    /// tasks); everything else this struct knows about is a sparse list of player edits.
    /// Meshes, colliders, and rendering all derive from Sample() — nothing else may define
    /// terrain shape.
    /// </summary>
    public class TerrainField
    {
        private readonly float _baseRadius;
        private readonly int _seed;
        private readonly TerrainNoiseSettings _noiseSettings;
        private readonly TerrainNoiseCache _noiseCache;
        private readonly List<TerrainEdit> _edits = new List<TerrainEdit>();

        private ChunkGrid _chunkGrid;
        private List<int>[] _editsByChunk;
        private readonly List<int> _chunkMembershipBuffer = new List<int>();

        public TerrainField(float baseRadius)
            : this(baseRadius, seed: 0, TerrainNoiseSettings.None)
        {
        }

        public TerrainField(float baseRadius, int seed, TerrainNoiseSettings noiseSettings)
        {
            if (baseRadius <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(baseRadius), baseRadius, "Base radius must be positive.");
            }

            _baseRadius = baseRadius;
            _seed = seed;
            _noiseSettings = noiseSettings;
            _noiseCache = TerrainNoiseCache.Build(seed, noiseSettings);
        }

        public float BaseRadius => _baseRadius;

        public IReadOnlyList<TerrainEdit> Edits => _edits;

        /// <summary>
        /// Enables per-chunk edit membership tracking against the given grid, so
        /// <see cref="Sample(Vector2, int)"/> can scan only edits that can actually affect a
        /// chunk instead of every edit ever applied. Existing edits (if any) are indexed
        /// immediately; every edit applied afterward is indexed as it's added. Optional — callers
        /// without chunk context (e.g. debug views) can keep using the chunk-agnostic
        /// <see cref="Sample(Vector2)"/> overload, which always scans every edit.
        /// </summary>
        public void EnableChunkIndexing(ChunkGrid chunkGrid)
        {
            if (chunkGrid == null)
            {
                throw new ArgumentNullException(nameof(chunkGrid));
            }

            _chunkGrid = chunkGrid;
            _editsByChunk = new List<int>[chunkGrid.ChunkCount];
            for (int i = 0; i < _editsByChunk.Length; i++)
            {
                _editsByChunk[i] = new List<int>();
            }

            for (int i = 0; i < _edits.Count; i++)
            {
                IndexEdit(i, _edits[i]);
            }
        }

        /// <summary>Radius of the base (unedited) surface at the given angle, including height noise.</summary>
        public float SurfaceRadiusAt(float angleRadians)
        {
            return _baseRadius + TerrainNoise.SampleHeight(angleRadians, _noiseCache, _noiseSettings);
        }

        /// <summary>
        /// Signed distance at the given planet-local position: negative = solid, positive = air,
        /// zero = surface. This is the single source of truth for terrain shape.
        /// </summary>
        public float Sample(Vector2 localPosition)
        {
            float angle = Core.RadialMath.AngleOf(localPosition);
            float distance = localPosition.magnitude - SurfaceRadiusAt(angle);

            for (int i = 0; i < _edits.Count; i++)
            {
                TerrainEdit edit = _edits[i];

                // No distance-based skip here: SampleContribution is now an unbounded linear
                // field (unclamped past the brush radius), so far from the brush a dig's
                // contribution runs off toward -infinity (Max below naturally ignores it) and a
                // build's runs off toward +infinity (Min naturally ignores it) — the CSG combine
                // self-limits without an explicit early-out, and combining every edit
                // unconditionally keeps the field continuous everywhere, which is what lets
                // MarchingSquaresMesher's linear edge interpolation reconstruct a smooth circle
                // right up to and across the brush boundary.
                float contribution = edit.SampleContribution(localPosition);

                // CSG-style combine, not a sum: a dig only ever pushes distance toward air (never
                // past the brush's own target), a build only ever pushes it toward solid. This
                // makes repeated/overlapping edits at the same spot idempotent (re-digging an
                // already-empty spot has no further effect) instead of "melting" deeper the more
                // strokes overlap, which a straight distance += contribution sum would do.
                distance = edit.IsAdditive ? Mathf.Max(distance, contribution) : Mathf.Min(distance, contribution);
            }

            return distance;
        }

        /// <summary>
        /// Same as <see cref="Sample(Vector2)"/>, but scans only the edits registered against
        /// <paramref name="chunkIndex"/> instead of every edit ever applied — valid only because
        /// an edit whose rectangular footprint cannot reach a chunk's bounding box can never be
        /// the Max/Min-selected contributor there. Requires
        /// <see cref="EnableChunkIndexing"/> to have been called first.
        /// </summary>
        public float Sample(Vector2 localPosition, int chunkIndex)
        {
            if (_editsByChunk == null)
            {
                throw new InvalidOperationException("Sample(Vector2, int) requires EnableChunkIndexing to have been called first.");
            }

            float angle = Core.RadialMath.AngleOf(localPosition);
            float distance = localPosition.magnitude - SurfaceRadiusAt(angle);

            List<int> editIndices = _editsByChunk[chunkIndex];
            for (int i = 0; i < editIndices.Count; i++)
            {
                TerrainEdit edit = _edits[editIndices[i]];
                float contribution = edit.SampleContribution(localPosition);
                distance = edit.IsAdditive ? Mathf.Max(distance, contribution) : Mathf.Min(distance, contribution);
            }

            return distance;
        }

        /// <summary>Applies and persists a modification. Never mutates a mesh or collider directly.</summary>
        public void ApplyEdit(TerrainEdit edit)
        {
            _edits.Add(edit);

            if (_editsByChunk != null)
            {
                IndexEdit(_edits.Count - 1, edit);
            }
        }

        private void IndexEdit(int editIndex, TerrainEdit edit)
        {
            // Rectangular overlap test: find all chunks whose bounding box overlaps the
            // brush's circular footprint. This is conservative (a circular brush inside a
            // square rect may touch slightly more chunks than necessary), but it is fast,
            // correct, and never misses an affected chunk.
            float brushMinX = edit.LocalPosition.x - edit.Radius;
            float brushMaxX = edit.LocalPosition.x + edit.Radius;
            float brushMinY = edit.LocalPosition.y - edit.Radius;
            float brushMaxY = edit.LocalPosition.y + edit.Radius;

            _chunkGrid.ChunksInRect(brushMinX, brushMaxX, brushMinY, brushMaxY, _chunkMembershipBuffer);

            for (int i = 0; i < _chunkMembershipBuffer.Count; i++)
            {
                _editsByChunk[_chunkMembershipBuffer[i]].Add(editIndex);
            }
        }

        /// <summary>Removes all persisted edits, leaving only the base sphere.</summary>
        public void ClearEdits()
        {
            _edits.Clear();
        }

        /// <summary>Replaces all persisted edits, e.g. when loading a save file.</summary>
        public void LoadEdits(IEnumerable<TerrainEdit> edits)
        {
            if (edits == null)
            {
                throw new ArgumentNullException(nameof(edits));
            }

            _edits.Clear();
            _edits.AddRange(edits);
        }
    }
}
