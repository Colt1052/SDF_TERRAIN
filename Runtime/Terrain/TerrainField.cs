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

        // Reverse index: edit index -> packed (col, row) keys of chunks the edit's bounding box
        // overlaps.  Used by batch operations (isolation pruning, baking, region-scoped
        // redundant-edit searches) rather than per-sample hot paths.
        private Dictionary<int, HashSet<long>> _editChunkKeys;

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
        /// Returns the base (unedited) signed distance at the given planet-local position.
        /// This is the sphere-with-noise SDF without any player edits applied. Use for
        /// geological depth calculations so that material layers are determined by the
        /// natural terrain rather than the current edit state.
        /// </summary>
        public float SampleBase(Vector2 localPosition)
        {
            float angle = Core.RadialMath.AngleOf(localPosition);
            return localPosition.magnitude - SurfaceRadiusAt(angle);
        }

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
            _editChunkKeys = new Dictionary<int, HashSet<long>>();

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
        /// Computes the SDF at <paramref name="localPosition"/>, optionally skipping one edit
        /// by index. Used by area-based redundancy checks to compare the field with and without
        /// a specific edit. Pass <paramref name="excludeEditIndex"/> as -1 to include all edits.
        /// </summary>
        private float SampleAt(Vector2 localPosition, int excludeEditIndex)
        {
            float angle = Core.RadialMath.AngleOf(localPosition);
            float distance = localPosition.magnitude - SurfaceRadiusAt(angle);

            for (int i = 0; i < _edits.Count; i++)
            {
                if (i == excludeEditIndex)
                    continue;

                TerrainEdit edit = _edits[i];
                float contribution = edit.SampleContribution(localPosition);
                distance = edit.IsAdditive ? Mathf.Max(distance, contribution) : Mathf.Min(distance, contribution);
            }

            return distance;
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
            return SampleAt(localPosition, -1);
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

                // Distance to the shape (circle center or capsule segment).
                float distanceFromBrush = edit.DistanceToShape(localPosition);

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
            // Remove existing edits that are entirely subsumed by this new edit.
            // Must run BEFORE adding to _edits — otherwise the new edit contains itself
            // and gets removed immediately.
            RemoveContainedEdits(edit);

            _edits.Add(edit);

            if (_editsByChunkKey != null)
            {
                IndexEdit(_edits.Count - 1, edit);

                // Incremental baking: check if any affected chunks are now fully covered
                // by the union of their edits, and if so, replace with a single rectangle.
                TryBakeCoveredChunks();
            }
        }

        /// <summary>
        /// Incremental baking: after a new edit is applied, checks all affected chunks
        /// to see if the union of their edits now completely covers the chunk. For each
        /// fully-covered uniform chunk, replaces all contributing edits with a single
        /// Rectangle edit spanning the chunk bounds.
        ///
        /// This is the per-edit version of <see cref="ConsolidateUniformRegions"/> that
        /// runs automatically whenever a new edit lands.
        /// </summary>
        private void TryBakeCoveredChunks()
        {
            // Collect all chunks that were affected by the latest edit.
            // The edit was just indexed, so _editChunkKeys contains its chunk keys.
            if (_editChunkKeys.Count == 0)
                return;

            // The most recently added edit has index _edits.Count - 1.
            int latestEditIndex = _edits.Count - 1;
            if (!_editChunkKeys.TryGetValue(latestEditIndex, out var affectedKeys))
                return;

            foreach (var key in affectedKeys)
            {
                int col = (int)(key >> 32);
                int row = (int)(key & 0xffffffffL);

                if (!_chunkGrid.HasChunkAtGrid(col, row))
                    continue;

                TerrainChunk chunk = _chunkGrid.GetChunkAtGrid(col, row);

                // Fetch the list of edits registered against this chunk.
                if (!_editsByChunkKey.TryGetValue(key, out var editIndices))
                    continue;

                if (editIndices.Count == 0)
                    continue;

                // Skip chunks that are already baked: a Rectangle edit in this chunk's
                // list that actually covers the entire chunk.  Merely having a Rectangle
                // in the list isn't enough — radius expansion during indexing can cause
                // rectangles from *neighboring* chunks to appear in this chunk's list.
                bool alreadyBaked = RectangleCoversChunk(chunk, editIndices);
                if (alreadyBaked)
                    continue;

                // Step 1: Sample the chunk to determine if it's uniform.
                // We need a cell size — use the chunk size as a reasonable default.
                // The sampler uses a lattice based on cell size; using chunk size gives
                // us one sample per chunk which is sufficient for uniformity detection.
                float cellSize = _chunkGrid.ChunkSize;
                var sampleResult = CartesianChunkFieldSampler.Sample(this, chunk, cellSize);

                if (!sampleResult.IsUniform)
                    continue; // Not uniform → can't bake

                // Step 2: Check if the union of edits covers the chunk.
                if (!EditUnionCoversChunk(chunk, editIndices))
                    continue; // Edits don't fully cover → skip

                // Step 3: Create a rectangular edit that covers the chunk exactly.
                // For solid regions: non-additive (placement/Min) to push SDF down.
                // For air regions: additive (removal/Max) to push SDF up.
                Vector2 bottomLeft = new Vector2(chunk.MinX, chunk.MinY);
                Vector2 topRight = new Vector2(chunk.MaxX, chunk.MaxY);
                float diagonal = Mathf.Sqrt(
                    (chunk.MaxX - chunk.MinX) * (chunk.MaxX - chunk.MinX) +
                    (chunk.MaxY - chunk.MinY) * (chunk.MaxY - chunk.MinY));
                float radius = diagonal * 2f;
                bool isAdditive = !sampleResult.IsSolid;

                TerrainEdit bakeEdit = new TerrainEdit(
                    bottomLeft, topRight, radius, isAdditive, BrushShape.Rectangle, clamped: true);

                // Step 4: Remove all the old edits for this chunk and add the bake edit.
                // We need to remove them from _edits and remap all chunk indices.
                BakeChunk(chunk, editIndices, bakeEdit);
            }

        }

        /// <summary>
        /// Adds a single <paramref name="bakeEdit"/> covering <paramref name="chunk"/> and
        /// replaces the target chunk's entry in the per-chunk edit index with just this new edit.
        /// The original edits are NOT removed from <c>_edits</c> — they may still be needed by
        /// other chunks that share them. The baked chunk samples only the baked rect, which
        /// produces identical terrain (we verified the chunk is uniform and fully covered before
        /// calling). The baked rect is clamped, so it has no effect outside its own bounds.
        /// </summary>
        private void BakeChunk(TerrainChunk chunk, List<int> editIndices, TerrainEdit bakeEdit)
        {
            // Add the baked edit to the global list.
            int bakeIndex = _edits.Count;
            _edits.Add(bakeEdit);

            // Replace only the target chunk's edit list — other chunks that share the
            // original edits keep them untouched.
            long key = PackKey(chunk.Col, chunk.Row);
            _editsByChunkKey[key].Clear();
            _editsByChunkKey[key].Add(bakeIndex);

            // Register the baked edit in the reverse index (for pruning/baking bookkeeping).
            _editChunkKeys[bakeIndex] = new HashSet<long> { key };
        }

        /// <summary>
        /// Finds and removes existing edits that are geometrically redundant because their
        /// entire footprint is contained within <paramref name="newEdit"/>.
        ///
        /// An existing edit is removed when all of these hold:
        /// <list type="bullet">
        /// <description>Same <c>BrushShape</c> as <paramref name="newEdit"/>.</description>
        /// <description>Same <c>IsAdditive</c> sign (both carve or both build).</description>
        /// <description>Same <c>Clamped</c> flag (identical boundary behavior).</description>
        /// <description>Radius less than or equal to <paramref name="newEdit"/>'s radius.</description>
        /// <description>Its bounding box is entirely within <paramref name="newEdit"/>'s bounding box.</description>
        /// </list>
        ///
        /// For same-shape edits, bounding-box containment guarantees that the new edit's shape
        /// dominates the existing edit's shape everywhere inside the contained bounding box,
        /// making removal safe. Mixed-shape containment is intentionally conservative — bbox
        /// containment does not imply shape dominance for different primitives.
        /// </summary>
        /// <param name="newEdit">The newly placed edit to check containment against.</param>
        /// <returns>The number of redundant edits removed.</returns>
        public int RemoveContainedEdits(TerrainEdit newEdit)
        {
            newEdit.GetBoundingBox(out float nMinX, out float nMaxX, out float nMinY, out float nMaxY);

            int count = _edits.Count;
            List<int> toRemove = null;

            for (int i = 0; i < count; i++)
            {
                TerrainEdit existing = _edits[i];

                // Same shape: required so bbox containment implies shape dominance.
                if (existing.Shape != newEdit.Shape)
                    continue;

                // Same sign: opposite-sign edits interact non-trivially (Max vs Min CSG).
                if (existing.IsAdditive != newEdit.IsAdditive)
                    continue;

                // Same clamped flag: different boundary behavior would change terrain values
                // at the contained edit's edge.
                if (existing.Clamped != newEdit.Clamped)
                    continue;

                // Larger radius cannot be subsumed by a smaller one.
                if (existing.Radius > newEdit.Radius)
                    continue;

                // Bounding-box containment check (shape-agnostic via GetBoundingBox).
                existing.GetBoundingBox(out float eMinX, out float eMaxX, out float eMinY, out float eMaxY);

                if (eMinX >= nMinX && eMaxX <= nMaxX && eMinY >= nMinY && eMaxY <= nMaxY)
                {
                    if (toRemove == null)
                        toRemove = new List<int>();
                    toRemove.Add(i);
                }
            }

            if (toRemove == null || toRemove.Count == 0)
                return 0;

            // Build old-to-new index mapping.
            int[] oldToNew = new int[count];
            for (int i = 0; i < count; i++)
            {
                bool removed = false;
                for (int k = 0; k < toRemove.Count; k++)
                {
                    if (toRemove[k] == i) { removed = true; break; }
                }
                oldToNew[i] = removed ? -1 : i - CountLessThan(toRemove, i);
            }

            // Compact _edits.
            int w = 0;
            for (int i = 0; i < count; i++)
            {
                if (oldToNew[i] >= 0)
                {
                    if (w != i) _edits[w] = _edits[i];
                    w++;
                }
            }
            while (_edits.Count > w)
                _edits.RemoveAt(_edits.Count - 1);

            // Remap chunk indices.
            if (_editsByChunkKey != null)
            {
                foreach (List<int> chunkList in _editsByChunkKey.Values)
                {
                    int cw = 0;
                    for (int cr = 0; cr < chunkList.Count; cr++)
                    {
                        int ni = oldToNew[chunkList[cr]];
                        if (ni >= 0)
                        {
                            chunkList[cw] = ni;
                            cw++;
                        }
                    }
                    while (chunkList.Count > cw)
                        chunkList.RemoveAt(chunkList.Count - 1);
                }
            }

            // Rebuild reverse index from surviving edits.
            if (_editChunkKeys != null)
            {
                var rebuilt = new Dictionary<int, HashSet<long>>();
                foreach (var kvp in _editChunkKeys)
                {
                    int ni = oldToNew[kvp.Key];
                    if (ni >= 0)
                        rebuilt[ni] = kvp.Value;
                }
                _editChunkKeys = rebuilt;
            }

            return toRemove.Count;
        }

        int CountLessThan(List<int> sorted, int value)
        {
            int lo = 0, hi = sorted.Count;
            while (lo < hi)
            {
                int mid = (lo + hi) >> 1;
                if (sorted[mid] < value) lo = mid + 1;
                else hi = mid;
            }
            return lo;
        }

        private void IndexEdit(int editIndex, TerrainEdit edit)
        {
            // Rectangular overlap test: find all chunks whose bounding box overlaps the
            // edit's footprint. This is conservative (a circular or capsule footprint inside
            // a square rect may touch slightly more chunks than necessary), but it is fast,
            // correct, and never misses an affected chunk.
            edit.GetBoundingBox(out float brushMinX, out float brushMaxX,
                out float brushMinY, out float brushMaxY);

            // Expand by Radius so the edit is indexed against all chunks its SDF cone
            // can reach. Rectangle shapes don't include Radius in their bbox (the
            // bbox is the shape extent), so expand here for indexing purposes.
            if (edit.Shape == BrushShape.Rectangle)
            {
                brushMinX -= edit.Radius;
                brushMaxX += edit.Radius;
                brushMinY -= edit.Radius;
                brushMaxY += edit.Radius;
            }

            float chunkSize = _chunkGrid.ChunkSize;
            float gridMinX = -(_chunkGrid.Cols * chunkSize) / 2f;
            float gridMinY = -(_chunkGrid.Rows * chunkSize) / 2f;

            int colStart = Mathf.FloorToInt((brushMinX - gridMinX) / chunkSize);
            int colEnd = Mathf.CeilToInt((brushMaxX - gridMinX) / chunkSize) - 1;
            int rowStart = Mathf.FloorToInt((brushMinY - gridMinY) / chunkSize);
            int rowEnd = Mathf.CeilToInt((brushMaxY - gridMinY) / chunkSize) - 1;

            // Build the reverse index for this edit.
            HashSet<long> chunkKeys = null;

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

                    if (chunkKeys == null)
                        chunkKeys = new HashSet<long>();
                    chunkKeys.Add(key);
                }
            }

            if (chunkKeys != null)
                _editChunkKeys[editIndex] = chunkKeys;
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

            // Rebuild reverse index from surviving edits.
            if (_editChunkKeys != null && pruned > 0)
            {
                var rebuilt = new Dictionary<int, HashSet<long>>();
                for (int i = 0; i < count; i++)
                {
                    int newIndex = oldToNew[i];
                    if (newIndex >= 0 && _editChunkKeys.TryGetValue(i, out var keys))
                    {
                        rebuilt[newIndex] = keys;
                    }
                }
                _editChunkKeys = rebuilt;
            }

            return pruned;
        }

        /// <summary>
        /// Measures the solid area inside a circular region using grid sampling.
        /// This is the same approach used by <see cref="ChunkTerrainRenderer.ApplyBrush"/>
        /// and <see cref="BrushAreaDelta"/> for tracking area deltas.
        /// </summary>
        /// <param name="center">Center of the circular region.</param>
        /// <param name="radius">Radius of the region.</param>
        /// <param name="sampleResolution">Grid resolution along each axis.</param>
        /// <param name="excludeEditIndex">If non-negative, skips the edit at this index during
        /// sampling. Used by <see cref="PruneRedundantEdits"/> to compare area with and without
        /// a candidate edit. Pass -1 (default) to include all edits.</param>
        public float GetSolidAreaInCircle(Vector2 center, float radius, int sampleResolution = 16, int excludeEditIndex = -1)
        {
            if (radius <= 0f) return 0f;
            if (sampleResolution < 2) sampleResolution = 2;

            float step = (2f * radius) / sampleResolution;
            float areaPerSample = step * step;
            int solidCount = 0;

            for (float y = -radius; y <= radius; y += step)
            {
                for (float x = -radius; x <= radius; x += step)
                {
                    Vector2 pos = center + new Vector2(x, y);
                    if (Vector2.Distance(pos, center) > radius) continue;
                    if (SampleAt(pos, excludeEditIndex) <= 0f)
                        solidCount++;
                }
            }

            return solidCount * areaPerSample;
        }

        /// <summary>
        /// Measures the solid area inside a rectangular region using grid sampling.
        /// Used for capsule-shaped edits where the footprint is not circular.
        /// </summary>
        private float GetSolidAreaInRect(float minX, float maxX, float minY, float maxY,
            int samplesPerAxis, int excludeEditIndex)
        {
            if (samplesPerAxis < 2) samplesPerAxis = 2;

            float stepX = (maxX - minX) / samplesPerAxis;
            float stepY = (maxY - minY) / samplesPerAxis;
            float areaPerSample = stepX * stepY;
            int solidCount = 0;

            for (float y = minY; y <= maxY; y += stepY)
            {
                for (float x = minX; x <= maxX; x += stepX)
                {
                    Vector2 pos = new Vector2(x, y);
                    if (SampleAt(pos, excludeEditIndex) <= 0f)
                        solidCount++;
                }
            }

            return solidCount * areaPerSample;
        }

        /// <summary>
        /// Finds and removes edits that no longer affect the terrain geometry. An edit is
        /// redundant when the solid area within its footprint is the same with and without
        /// that edit — meaning the edit does not shift any zero-crossing and is effectively
        /// a no-op. For example, a small dig entirely inside a larger mined cavity, or a
        /// build entirely buried under later terrain.
        /// </summary>
        /// <param name="samplesPerAxis">Grid resolution for area estimation. Higher = more
        /// accurate, slower. Default 16 is sufficient for most brush sizes.</param>
        /// <returns>The number of redundant edits removed.</returns>
        public int PruneRedundantEdits(int samplesPerAxis = 16)
        {
            if (samplesPerAxis < 2) samplesPerAxis = 2;

            int count = _edits.Count;
            bool[] isRedundant = new bool[count];
            int redundantCount = 0;

            for (int i = 0; i < count; i++)
            {
                TerrainEdit edit = _edits[i];

                // Skip zero-radius edits — they are handled by PruneDeadEdits.
                if (edit.Radius <= 0f)
                    continue;

                // Compute solid area within the edit's footprint, with and without this edit.
                float areaWith;
                float areaWithout;
                float footprintArea;

                if (edit.Shape == BrushShape.Capsule && edit.LocalPosition != edit.EndPosition)
                {
                    // Use bounding box for capsule edits.
                    edit.GetBoundingBox(out float minX, out float maxX, out float minY, out float maxY);
                    areaWith = GetSolidAreaInRect(minX, maxX, minY, maxY, samplesPerAxis, -1);
                    areaWithout = GetSolidAreaInRect(minX, maxX, minY, maxY, samplesPerAxis, i);
                    float width = maxX - minX;
                    float height = maxY - minY;
                    footprintArea = width * height;
                }
                else
                {
                    // Use circular region for circle edits (and degenerate capsules).
                    areaWith = GetSolidAreaInCircle(edit.LocalPosition, edit.Radius, samplesPerAxis, -1);
                    areaWithout = GetSolidAreaInCircle(edit.LocalPosition, edit.Radius, samplesPerAxis, i);
                    footprintArea = Mathf.PI * edit.Radius * edit.Radius;
                }

                // If the area difference is negligible (less than 0.1% of footprint), the edit
                // is redundant. This tolerance accounts for grid-sampling quantization.
                float tolerance = footprintArea * 0.001f;
                if (Mathf.Abs(areaWith - areaWithout) < tolerance)
                {
                    isRedundant[i] = true;
                    redundantCount++;
                }
            }

            if (redundantCount == 0)
                return 0;

            // Compact _edits and remap chunk indices (same pattern as PruneDeadEdits).
            int writeIndex = 0;
            int[] oldToNew = new int[count];

            for (int i = 0; i < count; i++)
            {
                if (isRedundant[i])
                {
                    oldToNew[i] = -1;
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

            // Remap chunk indices and remove references to pruned edits.
            if (_editsByChunkKey != null)
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

            // Rebuild reverse index from surviving edits.
            if (_editChunkKeys != null)
            {
                var rebuilt = new Dictionary<int, HashSet<long>>();
                for (int i = 0; i < count; i++)
                {
                    int newIndex = oldToNew[i];
                    if (newIndex >= 0 && _editChunkKeys.TryGetValue(i, out var keys))
                    {
                        rebuilt[newIndex] = keys;
                    }
                }
                _editChunkKeys = rebuilt;
            }

            return redundantCount;
        }

        /// <summary>
        /// Removes edits that are "isolated" — edits whose affected chunks are all
        /// considered inactive by the caller's predicate.
        ///
        /// An edit is isolated when <paramref name="isChunkActive"/> returns <c>false</c>
        /// for every chunk key in the edit's footprint.  The caller decides what
        /// "active" means (e.g., chunk is loaded in memory, chunk has geometry,
        /// chunk is within render distance).
        /// </summary>
        /// <param name="isChunkActive">A predicate that receives a packed (col, row) chunk key
        /// and returns <c>true</c> if that chunk should be considered active.  An edit is
        /// removed only if ALL its chunk keys return <c>false</c>.</param>
        /// <returns>The number of isolated edits removed.</returns>
        public int PruneIsolatedEdits(Func<long, bool> isChunkActive)
        {
            if (_editChunkKeys == null)
            {
                throw new InvalidOperationException("PruneIsolatedEdits requires EnableChunkIndexing to have been called first.");
            }

            int count = _edits.Count;
            bool[] isIsolated = new bool[count];
            int isolatedCount = 0;

            for (int i = 0; i < count; i++)
            {
                if (!_editChunkKeys.TryGetValue(i, out var keys) || keys.Count == 0)
                {
                    // Edit has no chunk keys (shouldn't normally happen, but treat as
                    // isolated — if we can't prove it affects an active chunk, it's not doing work).
                    isIsolated[i] = true;
                    isolatedCount++;
                    continue;
                }

                bool anyActive = false;
                foreach (var key in keys)
                {
                    if (isChunkActive(key))
                    {
                        anyActive = true;
                        break;
                    }
                }

                if (!anyActive)
                {
                    isIsolated[i] = true;
                    isolatedCount++;
                }
            }

            if (isolatedCount == 0)
                return 0;

            // Compact _edits and build index remap table.
            int writeIndex = 0;
            int[] oldToNew = new int[count];

            for (int i = 0; i < count; i++)
            {
                if (isIsolated[i])
                {
                    oldToNew[i] = -1;
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

            // Remap chunk indices.
            if (_editsByChunkKey != null)
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

            // Rebuild reverse index from surviving edits.
            if (_editChunkKeys != null)
            {
                var rebuilt = new Dictionary<int, HashSet<long>>();
                for (int i = 0; i < count; i++)
                {
                    int newIndex = oldToNew[i];
                    if (newIndex >= 0 && _editChunkKeys.TryGetValue(i, out var keys))
                    {
                        rebuilt[newIndex] = keys;
                    }
                }
                _editChunkKeys = rebuilt;
            }

            return isolatedCount;
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

            _editChunkKeys?.Clear();
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

                _editChunkKeys.Clear();

                for (int i = 0; i < _edits.Count; i++)
                {
                    IndexEdit(i, _edits[i]);
                }
            }
        }

        /// <summary>
        /// Detects uniform chunks whose edits fully cover the chunk and replaces each with a
        /// single rectangular edit. Call this to consolidate the edit list after heavy brushing
        /// that creates large uniform regions. Each chunk bakes independently — neighboring chunks
        /// are not combined.
        /// </summary>
        /// <param name="cellSize">Lattice cell size for sampling (must match the value used by
        /// <see cref="CartesianChunkFieldSampler"/>).</param>
        public void ConsolidateUniformRegions(float cellSize)
        {
            if (_editsByChunkKey == null)
            {
                throw new InvalidOperationException("ConsolidateUniformRegions requires EnableChunkIndexing to have been called first.");
            }

            if (cellSize <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(cellSize), cellSize, "Cell size must be positive.");
            }

            // Create per-chunk rectangular edits for uniform covered regions.
            CreateUniformRegionEdits(cellSize);
        }

        private void CreateUniformRegionEdits(float cellSize)
        {
            // Step 1: Sample all chunks to identify uniform ones covered by their edits.
            var candidates = new List<TerrainChunk>();

            foreach (TerrainChunk chunk in _chunkGrid.AllChunks)
            {
                var result = CartesianChunkFieldSampler.Sample(this, chunk, cellSize);

                if (!result.IsUniform)
                    continue;

                long key = PackKey(chunk.Col, chunk.Row);
                if (!_editsByChunkKey.TryGetValue(key, out List<int> editIndices))
                    continue; // No edits → skip (uniform base terrain, not edit-covered)

                if (!EditUnionCoversChunk(chunk, editIndices))
                    continue; // Edits don't fully cover → not a candidate

                candidates.Add(chunk);
            }

            if (candidates.Count == 0)
                return;

            // Step 2: Replace each candidate's edits with a single rectangle covering that chunk.
            // Process individually — no flood-fill grouping of neighboring chunks.
            foreach (TerrainChunk chunk in candidates)
            {
                // Re-fetch current edit indices — previous bakes may have modified the list.
                long key = PackKey(chunk.Col, chunk.Row);
                if (!_editsByChunkKey.TryGetValue(key, out List<int> editIndices))
                    continue;

                if (editIndices.Count == 0)
                    continue;

                // Skip if already baked: a Rectangle edit that actually covers this chunk.
                bool alreadyBaked = RectangleCoversChunk(chunk, editIndices);
                if (alreadyBaked)
                    continue;

                // Determine solidity from current samples.
                var sampleResult = CartesianChunkFieldSampler.Sample(this, chunk, cellSize);

                Vector2 bottomLeft = new Vector2(chunk.MinX, chunk.MinY);
                Vector2 topRight = new Vector2(chunk.MaxX, chunk.MaxY);
                float diagonal = Mathf.Sqrt(
                    (chunk.MaxX - chunk.MinX) * (chunk.MaxX - chunk.MinX) +
                    (chunk.MaxY - chunk.MinY) * (chunk.MaxY - chunk.MinY));
                float radius = diagonal * 2f;

                TerrainEdit bakeEdit = new TerrainEdit(
                    bottomLeft, topRight, radius, !sampleResult.IsSolid, BrushShape.Rectangle, clamped: true);

                // Remove old edits and add the baked edit. BakeChunk handles index remapping.
                BakeChunk(chunk, editIndices, bakeEdit);
            }
        }

        /// <summary>
        /// Checks whether any single Rectangle-shaped edit in the given list fully covers
        /// the chunk. Returns true if a Rectangle edit's bounds entirely encompass the chunk
        /// bounds. Unlike <see cref="EditUnionCoversChunk"/>, this does not check circle or
        /// capsule edits, and it only looks for a single dominating rectangle rather than
        /// verifying coverage via grid sampling.
        /// </summary>
        private bool RectangleCoversChunk(TerrainChunk chunk, List<int> editIndices)
        {
            for (int i = 0; i < editIndices.Count; i++)
            {
                TerrainEdit edit = _edits[editIndices[i]];
                if (edit.Shape != BrushShape.Rectangle)
                    continue;

                float rectMinX = Mathf.Min(edit.LocalPosition.x, edit.EndPosition.x);
                float rectMaxX = Mathf.Max(edit.LocalPosition.x, edit.EndPosition.x);
                float rectMinY = Mathf.Min(edit.LocalPosition.y, edit.EndPosition.y);
                float rectMaxY = Mathf.Max(edit.LocalPosition.y, edit.EndPosition.y);

                if (rectMinX <= chunk.MinX && rectMaxX >= chunk.MaxX &&
                    rectMinY <= chunk.MinY && rectMaxY >= chunk.MaxY)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Checks whether the union of the given edits' shapes fully covers the chunk.
        /// Uses shape distance (not bounding boxes) so circles and capsules correctly
        /// cover their actual footprint rather than conservative square bboxes.
        /// </summary>
        private bool EditUnionCoversChunk(TerrainChunk chunk, List<int> editIndices)
        {
            if (editIndices.Count == 0)
                return false;

            // Quick check: if a single edit's shape covers the chunk entirely, we're done.
            // For rectangles this is exact; for circles/capsules it's a conservative shortcut.
            for (int i = 0; i < editIndices.Count; i++)
            {
                TerrainEdit edit = _edits[editIndices[i]];
                if (edit.Shape == BrushShape.Rectangle)
                {
                    float rectMinX = Mathf.Min(edit.LocalPosition.x, edit.EndPosition.x);
                    float rectMaxX = Mathf.Max(edit.LocalPosition.x, edit.EndPosition.x);
                    float rectMinY = Mathf.Min(edit.LocalPosition.y, edit.EndPosition.y);
                    float rectMaxY = Mathf.Max(edit.LocalPosition.y, edit.EndPosition.y);
                    if (rectMinX <= chunk.MinX && rectMaxX >= chunk.MaxX &&
                        rectMinY <= chunk.MinY && rectMaxY >= chunk.MaxY)
                        return true;
                }
            }

            // Grid-based coverage test: divide the chunk into a small grid and check
            // if every cell is covered by at least one edit's shape (distance <= radius).
            int gridRes = 4;
            float cellW = (chunk.MaxX - chunk.MinX) / gridRes;
            float cellH = (chunk.MaxY - chunk.MinY) / gridRes;

            for (int gx = 0; gx < gridRes; gx++)
            {
                for (int gy = 0; gy < gridRes; gy++)
                {
                    float cx = chunk.MinX + (gx + 0.5f) * cellW;
                    float cy = chunk.MinY + (gy + 0.5f) * cellH;
                    bool covered = false;

                    for (int i = 0; i < editIndices.Count; i++)
                    {
                        TerrainEdit edit = _edits[editIndices[i]];
                        float dist = edit.DistanceToShape(new Vector2(cx, cy));
                        if (dist <= edit.Radius)
                        {
                            covered = true;
                            break;
                        }
                    }

                    if (!covered)
                        return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Merges adjacent rectangle edits that share an edge and have the same
        /// solidity (IsAdditive). This reduces the total edit count and improves
        /// sampling performance by combining many small rectangles into fewer large ones.
        ///
        /// Two rectangles are adjacent if one is exactly to the left/right/above/below
        /// the other (4-directional, no diagonals), and they align perfectly on the
        /// shared edge (same extent on the perpendicular axis).
        ///
        /// Uses a fixed-point sweep: repeatedly finds mergeable pairs until no more merges
        /// are possible. After each pass, the edit list is compacted and indices are rebuilt.
        /// </summary>
        private void MergeAdjacentRectangleEdits()
        {
            // Collect all Rectangle-shaped edits with their indices
            List<int> rectangleIndices = new List<int>();
            for (int i = 0; i < _edits.Count; i++)
            {
                if (_edits[i].Shape == BrushShape.Rectangle)
                    rectangleIndices.Add(i);
            }

            if (rectangleIndices.Count < 2)
                return; // Nothing to merge

            // Sort for deterministic processing order
            rectangleIndices.Sort((a, b) =>
            {
                TerrainEdit editA = _edits[a];
                TerrainEdit editB = _edits[b];
                // Sort by position then size for determinism
                int cmp = editA.LocalPosition.x.CompareTo(editB.LocalPosition.x);
                if (cmp != 0) return cmp;
                cmp = editA.LocalPosition.y.CompareTo(editB.LocalPosition.y);
                if (cmp != 0) return cmp;
                cmp = editA.EndPosition.x.CompareTo(editB.EndPosition.x);
                if (cmp != 0) return cmp;
                return editA.EndPosition.y.CompareTo(editB.EndPosition.y);
            });

            // Try to merge pairs until no more merges are found (fixed-point).
            // Each successful merge reduces the edit count by 1, guaranteeing termination.
            bool madeMerge;
            do
            {
                madeMerge = false;

                for (int i = 0; i < rectangleIndices.Count && !madeMerge; i++)
                {
                    int idxA = rectangleIndices[i];
                    TerrainEdit editA = _edits[idxA];

                    for (int j = i + 1; j < rectangleIndices.Count && !madeMerge; j++)
                    {
                        int idxB = rectangleIndices[j];
                        TerrainEdit editB = _edits[idxB];

                        // Must have same solidity to merge
                        if (editA.IsAdditive != editB.IsAdditive)
                            continue;

                        // Normalize the rectangles (get min/max bounds)
                        float aMinX = Mathf.Min(editA.LocalPosition.x, editA.EndPosition.x);
                        float aMaxX = Mathf.Max(editA.LocalPosition.x, editA.EndPosition.x);
                        float aMinY = Mathf.Min(editA.LocalPosition.y, editA.EndPosition.y);
                        float aMaxY = Mathf.Max(editA.LocalPosition.y, editA.EndPosition.y);

                        float bMinX = Mathf.Min(editB.LocalPosition.x, editB.EndPosition.x);
                        float bMaxX = Mathf.Max(editB.LocalPosition.x, editB.EndPosition.x);
                        float bMinY = Mathf.Min(editB.LocalPosition.y, editB.EndPosition.y);
                        float bMaxY = Mathf.Max(editB.LocalPosition.y, editB.EndPosition.y);

                        bool adjacent = false;

                        // Check horizontal adjacency: A is left of B
                        if (Mathf.Approximately(aMaxX, bMinX) &&
                            Mathf.Approximately(aMinY, bMinY) &&
                            Mathf.Approximately(aMaxY, bMaxY))
                        {
                            adjacent = true;
                        }
                        // Check horizontal adjacency: B is left of A
                        else if (Mathf.Approximately(bMaxX, aMinX) &&
                                 Mathf.Approximately(bMinY, aMinY) &&
                                 Mathf.Approximately(bMaxY, aMaxY))
                        {
                            adjacent = true;
                        }
                        // Check vertical adjacency: A is below B
                        else if (Mathf.Approximately(aMaxY, bMinY) &&
                                 Mathf.Approximately(aMinX, bMinX) &&
                                 Mathf.Approximately(aMaxX, bMaxX))
                        {
                            adjacent = true;
                        }
                        // Check vertical adjacency: B is below A
                        else if (Mathf.Approximately(bMaxY, aMinY) &&
                                 Mathf.Approximately(bMinX, aMinX) &&
                                 Mathf.Approximately(bMaxX, aMaxX))
                        {
                            adjacent = true;
                        }

                        if (!adjacent)
                            continue;

                        // Create merged rectangle spanning the union of both bounds.
                        float mergedMinX = Mathf.Min(aMinX, bMinX);
                        float mergedMaxX = Mathf.Max(aMaxX, bMaxX);
                        float mergedMinY = Mathf.Min(aMinY, bMinY);
                        float mergedMaxY = Mathf.Max(aMaxY, bMaxY);

                        TerrainEdit mergedEdit = new TerrainEdit(
                            new Vector2(mergedMinX, mergedMinY),
                            new Vector2(mergedMaxX, mergedMaxY),
                            editA.Radius,
                            editA.IsAdditive,
                            BrushShape.Rectangle,
                            clamped: true);

                        // Remove both old edits and insert the merged one.
                        // Remove higher index first to avoid shifting the lower index.
                        int higherIndex = Mathf.Max(idxA, idxB);
                        int lowerIndex = Mathf.Min(idxA, idxB);
                        _edits.RemoveAt(higherIndex);
                        _edits.RemoveAt(lowerIndex);
                        _edits.Insert(lowerIndex, mergedEdit);

                        // Rebuild rectangleIndices list after mutation
                        rectangleIndices.Clear();
                        for (int k = 0; k < _edits.Count; k++)
                        {
                            if (_edits[k].Shape == BrushShape.Rectangle)
                                rectangleIndices.Add(k);
                        }

                        // Re-sort for determinism on the next pass
                        rectangleIndices.Sort((a, b) =>
                        {
                            TerrainEdit ea = _edits[a];
                            TerrainEdit eb = _edits[b];
                            int c = ea.LocalPosition.x.CompareTo(eb.LocalPosition.x);
                            if (c != 0) return c;
                            c = ea.LocalPosition.y.CompareTo(eb.LocalPosition.y);
                            if (c != 0) return c;
                            c = ea.EndPosition.x.CompareTo(eb.EndPosition.x);
                            if (c != 0) return c;
                            return ea.EndPosition.y.CompareTo(eb.EndPosition.y);
                        });

                        madeMerge = true;
                    }
                }

                // After a successful merge, rebuild chunk indices to reflect the new state.
                if (madeMerge)
                {
                    RebuildChunkIndices();
                }
            } while (madeMerge);
        }

        /// <summary>
        /// Rebuilds the chunk edit index and reverse index based on the current
        /// state of the _edits list. This should be called after any operation
        /// that modifies the edit list.
        /// </summary>
        private void RebuildChunkIndices()
        {
            if (_editsByChunkKey == null) return;

            // Clear existing indices
            foreach (var list in _editsByChunkKey.Values)
                list.Clear();
            _editChunkKeys?.Clear();

            // Re-index all edits
            for (int i = 0; i < _edits.Count; i++)
            {
                IndexEdit(i, _edits[i]);
            }
        }
    }
}
