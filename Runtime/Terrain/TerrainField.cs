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
        // Keyed by packed (col, row) so edits can be indexed for grid cells even when no
        // chunk object exists (e.g., delete brushes outside the current grid).
        private Dictionary<long, List<int>> _editsByChunkKey;

        /// <summary>Packs (col, row) into a unique long key, matching ChunkGrid.MakeKey.</summary>
        static long PackKey(int col, int row)
        {
            return (((long)col) << 32) | ((long)row & 0xffffffffL);
        }

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
            _editsByChunkKey = new Dictionary<long, List<int>>();

            // Initialize entries for all existing chunks, keyed by packed (col, row).
            foreach (TerrainChunk chunk in chunkGrid.AllChunks)
            {
                _editsByChunkKey[PackKey(chunk.Col, chunk.Row)] = new List<int>();
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
        /// Finds the nearest terrain surface point within <paramref name="radius"/> of
        /// <paramref name="localPosition"/>. The surface is defined as the zero-crossing of the
        /// SDF (where <see cref="Sample"/> returns zero).
        /// Uses angular sampling around the query point to locate the closest surface, then
        /// refines radially toward the exact zero-crossing.
        /// </summary>
        /// <param name="localPosition">Planet-local query position.</param>
        /// <param name="radius">Maximum search distance.</param>
        /// <param name="rayCount">Number of search rays cast around the query point. Higher = more
        /// accurate surface detection, lower = faster but more random strike placement.</param>
        /// <param name="nearestSurface">The nearest surface point found, or <c>localPosition</c> if
        /// no surface is within <paramref name="radius"/>.</param>
        /// <returns>True if a terrain surface was found within the search radius.</returns>
        public bool FindNearestSurface(Vector2 localPosition, float radius, int rayCount, out Vector2 nearestSurface)
        {
            if (radius <= 0f)
            {
                nearestSurface = localPosition;
                return false;
            }

            if (rayCount <= 0)
            {
                rayCount = 1;
            }

            Vector2 closestPoint = localPosition;
            float closestSurfaceDistance = Mathf.Infinity;

            // Randomize the starting rotation so repeated strikes don't follow a predictable pattern.
            float randomOffset = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
            for (int i = 0; i < rayCount; i++)
            {
                float angle = randomOffset + (i / (float)rayCount) * Mathf.PI * 2f;
                Vector2 direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));

                // Step outward from the query point along this direction, looking for a sign change.
                float stepSize = radius / 8f;
                float lastSampled = Sample(localPosition);

                for (float dist = stepSize; dist <= radius; dist += stepSize)
                {
                    Vector2 probe = localPosition + direction * dist;
                    float currentSampled = Sample(probe);

                    // Detect a sign change between the last two samples — surface lies between them.
                    if ((lastSampled < 0f && currentSampled >= 0f) ||
                        (lastSampled >= 0f && currentSampled < 0f))
                    {
                        // Linear interpolation to estimate the zero-crossing.
                        float totalDelta = Mathf.Abs(currentSampled - lastSampled);
                        float t = totalDelta > 0f ? Mathf.Abs(lastSampled) / totalDelta : 0.5f;
                        Vector2 surfaceEstimate = Vector2.Lerp(localPosition + direction * (dist - stepSize), probe, t);

                        float surfaceDist = Vector2.Distance(localPosition, surfaceEstimate);
                        if (surfaceDist < closestSurfaceDistance)
                        {
                            closestSurfaceDistance = surfaceDist;
                            closestPoint = surfaceEstimate;
                        }
                        // Found a surface crossing on this ray — no need to continue stepping.
                        break;
                    }

                    lastSampled = currentSampled;
                }
            }

            bool found = closestSurfaceDistance < radius && closestSurfaceDistance < Mathf.Infinity;
            nearestSurface = found ? closestPoint : localPosition;
            return found;
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
            if (_editsByChunkKey == null)
            {
                throw new InvalidOperationException("Sample(Vector2, int) requires EnableChunkIndexing to have been called first.");
            }

            float angle = Core.RadialMath.AngleOf(localPosition);
            float distance = localPosition.magnitude - SurfaceRadiusAt(angle);

            TerrainChunk chunk = _chunkGrid.GetChunk(chunkIndex);
            long key = PackKey(chunk.Col, chunk.Row);

            if (!_editsByChunkKey.TryGetValue(key, out List<int> editIndices))
            {
                // Chunk has no indexed edits — return base field value.
                return distance;
            }

            for (int i = 0; i < editIndices.Count; i++)
            {
                TerrainEdit edit = _edits[editIndices[i]];
                float contribution = edit.SampleContribution(localPosition);
                distance = edit.IsAdditive ? Mathf.Max(distance, contribution) : Mathf.Min(distance, contribution);
            }

            return distance;
        }

        /// <summary>
        /// Smooths the terrain by reducing the radius of existing edits near the given position.
        /// Smoothing conceptually erases past edits rather than sculpting new terrain, so it
        /// reduces edit.Radius (clamped to never go negative) using a smoothstep falloff profile.
        /// An edit shrunk to Radius &lt;= 0 contributes nothing at sample time and can be reclaimed
        /// by <see cref="PruneDeadEdits"/>.
        /// </summary>
        /// <param name="localPosition">Planet-local center of the smooth operation.</param>
        /// <param name="radius">Affected area and the maximum reduction amount.</param>
        public void SmoothEdits(Vector2 localPosition, float radius)
        {
            if (radius <= 0f)
            {
                return;
            }

            for (int i = 0; i < _edits.Count; i++)
            {
                TerrainEdit edit = _edits[i];

                float distanceFromBrush = Vector2.Distance(localPosition, edit.LocalPosition);
                if (distanceFromBrush >= radius)
                {
                    continue;
                }

                float falloff = 1f - Mathf.SmoothStep(0f, radius, distanceFromBrush);
                // Reduce the edit's radius toward zero.
                edit.Radius = Mathf.Max(0f, edit.Radius - falloff * radius);
                _edits[i] = edit;
            }
        }

        /// <summary>Applies and persists a modification. Never mutates a mesh or collider directly.</summary>
        /// <param name="edit">The edit to apply.</param>
        public void ApplyEdit(TerrainEdit edit)
        {
            _edits.Add(edit);

            if (_editsByChunkKey != null)
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

            float chunkSize = _chunkGrid.ChunkSize;
            float gridMinX = -(_chunkGrid.Cols * chunkSize) / 2f;
            float gridMinY = -(_chunkGrid.Rows * chunkSize) / 2f;

            int colStart = Mathf.FloorToInt((brushMinX - gridMinX) / chunkSize);
            int colEnd = Mathf.CeilToInt((brushMaxX - gridMinX) / chunkSize) - 1;
            int rowStart = Mathf.FloorToInt((brushMinY - gridMinY) / chunkSize);
            int rowEnd = Mathf.CeilToInt((brushMaxY - gridMinY) / chunkSize) - 1;

            for (int row = rowStart; row <= rowEnd; row++)
            {
                for (int col = colStart; col <= colEnd; col++)
                {
                    long key = PackKey(col, row);
                    if (!_editsByChunkKey.TryGetValue(key, out List<int> list))
                    {
                        list = new List<int>();
                        _editsByChunkKey[key] = list;
                    }
                    list.Add(editIndex);
                }
            }
        }

        /// <summary>
        /// Removes edits that can no longer contribute (radius collapsed to zero by smoothing,
        /// or otherwise reduced to a no-op). Compacts the edit list and remaps all chunk indices
        /// atomically so the chunk-indexed sampler remains valid. Call periodically (e.g. after
        /// a batch of smooth operations, or when the edit count exceeds a threshold) to prevent
        /// unbounded list growth.
        /// </summary>
        /// <returns>The number of edits removed.</returns>
        public int PruneDeadEdits()
        {
            int writeIndex = 0;
            int pruned = 0;

            // First pass: compact _edits, tracking which old indices survived and where they moved.
            int count = _edits.Count;
            int[] oldToNew = new int[count];
            for (int i = 0; i < count; i++)
            {
                if (_edits[i].Radius <= 0f)
                {
                    oldToNew[i] = -1; // marked for removal
                    pruned++;
                }
                else
                {
                    oldToNew[i] = writeIndex;
                    if (writeIndex != i)
                    {
                        _edits[writeIndex] = _edits[i];
                    }
                    writeIndex++;
                }
            }

            while (_edits.Count > writeIndex)
            {
                _edits.RemoveAt(_edits.Count - 1);
            }

            // Second pass: remap chunk indices and remove references to pruned edits.
            if (_editsByChunkKey != null && pruned > 0)
            {
                foreach (List<int> chunkList in _editsByChunkKey.Values)
                {
                    int w = 0;
                    for (int r = 0; r < chunkList.Count; r++)
                    {
                        int oldIndex = chunkList[r];
                        int newIndex = oldToNew[oldIndex];
                        if (newIndex >= 0)
                        {
                            chunkList[w] = newIndex;
                            w++;
                        }
                    }
                    while (chunkList.Count > w)
                    {
                        chunkList.RemoveAt(chunkList.Count - 1);
                    }
                }
            }

            return pruned;
        }

        /// <summary>Removes all persisted edits, leaving only the base sphere.</summary>
        public void ClearEdits()
        {
            _edits.Clear();

            if (_editsByChunkKey != null)
            {
                foreach (List<int> list in _editsByChunkKey.Values)
                {
                    list.Clear();
                }
            }
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

            // Rebuild chunk indexing for the new edits, if indexing is active.
            if (_editsByChunkKey != null)
            {
                foreach (List<int> list in _editsByChunkKey.Values)
                {
                    list.Clear();
                }

                for (int i = 0; i < _edits.Count; i++)
                {
                    IndexEdit(i, _edits[i]);
                }
            }
        }
    }
}
