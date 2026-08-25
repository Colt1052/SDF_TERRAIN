using System;
using System.Collections.Generic;
using UnityEngine;
using SDFTerrain.Terrain;

namespace SDFTerrain.Materials
{
    /// <summary>
    /// Authoritative material state layer that runs alongside a <see cref="TerrainField"/>.
    /// Owns the material edit history and provides spatially-indexed queries.
    ///
    /// Query model: Given a position, checks material edits in reverse order (last applied first).
    /// The first edit containing the position wins. If no edit applies, falls back to the
    /// procedural geological material determined by the <see cref="GeologicalProfile"/>.
    ///
    /// Edits are indexed by chunk grid cells so sampling is O(local_edits) not O(total_history).
    /// </summary>
    public class MaterialLayer
    {
        private readonly List<MaterialEdit> _edits = new List<MaterialEdit>();
        private readonly GeologicalProfile _profile;
        private readonly MaterialDatabase _database;

        // Keyed by packed (col, row) so edits can be indexed for grid cells even when no
        // chunk object exists.
        private Dictionary<long, List<int>> _editsByChunkKey;
        private ChunkGrid _chunkGrid;

        private int _nextOrder;

        /// <summary>Number of material edits applied so far.</summary>
        public int EditCount => _edits.Count;

        /// <summary>All edits (for serialization).</summary>
        public IReadOnlyList<MaterialEdit> Edits => _edits;

        /// <summary>The material database used for material lookups.</summary>
        public MaterialDatabase GetDatabase() => _database;

        public MaterialLayer(GeologicalProfile profile, MaterialDatabase database)
        {
            if (profile == null)
                throw new ArgumentNullException(nameof(profile));
            if (database == null)
                throw new ArgumentNullException(nameof(database));

            _profile = profile;
            _database = database;
        }

        /// <summary>
        /// Enables per-chunk edit membership tracking against the given grid, so
        /// <see cref="Sample"/> can scan only edits that can actually affect a chunk
        /// instead of every edit ever applied.
        /// </summary>
        public void EnableChunkIndexing(ChunkGrid chunkGrid)
        {
            if (chunkGrid == null)
                throw new ArgumentNullException(nameof(chunkGrid));

            _chunkGrid = chunkGrid;
            _editsByChunkKey = new Dictionary<long, List<int>>();

            // Initialize entries for all existing chunks.
            foreach (TerrainChunk chunk in chunkGrid.AllChunks)
            {
                _editsByChunkKey[PackKey(chunk.Col, chunk.Row)] = new List<int>();
            }

            // Index existing edits.
            for (int i = 0; i < _edits.Count; i++)
            {
                IndexEdit(i, _edits[i]);
            }
        }

        /// <summary>
        /// Samples the material at <paramref name="localPosition"/>.
        ///
        /// If the position is in air (SDF > 0), returns MaterialId.Air.
        /// Otherwise, finds the last material edit containing the position.
        /// If no edit applies, falls back to the geological profile.
        /// </summary>
        public MaterialSample Sample(TerrainField field, Vector2 localPosition)
        {
            return Sample(field, localPosition, -1);
        }

        /// <summary>
        /// Samples the material at <paramref name="localPosition"/>, scanning only the edits
        /// indexed against <paramref name="chunkIndex"/>. Requires <see cref="EnableChunkIndexing"/>.
        /// </summary>
        public MaterialSample Sample(TerrainField field, Vector2 localPosition, int chunkIndex)
        {
            if (field == null)
                throw new ArgumentNullException(nameof(field));

            float sdf = field.Sample(localPosition);

            // In air — return air material
            if (sdf > 0f)
            {
                return new MaterialSample(MaterialId.Air, 1f, false);
            }

            // Check material edits — scan last-first for the highest-order override.
            MaterialId editMaterial = SampleEdit(localPosition, chunkIndex);

            if (editMaterial.IsValid)
            {
                return new MaterialSample(editMaterial, 1f, true);
            }

            // Fall back to geological profile.
            GeologicalSampleResult geoResult = GeologicalLayerGenerator.Sample(field, localPosition, _profile, _database);
            MaterialId naturalMaterial = _database.GetMaterialId(geoResult.MaterialId);

            return new MaterialSample(naturalMaterial, 1f, true);
        }

        private MaterialId SampleEdit(Vector2 position, int chunkIndex)
        {
            if (_edits.Count == 0)
                return MaterialId.Unknown;

            // Chunk-indexed scan — only local edits.
            if (_editsByChunkKey != null)
            {
                if (_chunkGrid != null && _chunkGrid.TryGetChunk(chunkIndex, out TerrainChunk chunk))
                {
                    long key = PackKey(chunk.Col, chunk.Row);

                    if (_editsByChunkKey.TryGetValue(key, out List<int> editIndices))
                    {
                        // Scan in reverse order — last applicable edit wins.
                        for (int i = editIndices.Count - 1; i >= 0; i--)
                        {
                            MaterialEdit edit = _edits[editIndices[i]];
                            if (edit.Contains(position))
                            {
                                return edit.MaterialId;
                            }
                        }
                    }
                }

                // If chunkIndex is invalid or has no indexed edits, fall through to full scan.
            }

            // Full scan fallback — reverse order.
            for (int i = _edits.Count - 1; i >= 0; i--)
            {
                MaterialEdit edit = _edits[i];
                if (edit.Contains(position))
                {
                    return edit.MaterialId;
                }
            }

            return MaterialId.Unknown;
        }

        /// <summary>
        /// Applies a material override at a circular region. This is the primary way to record
        /// that the player has placed a specific material at a location.
        /// </summary>
        public void ApplyEdit(Vector2 localPosition, float radius, MaterialId materialId)
        {
            if (!materialId.IsValid)
                throw new ArgumentException("MaterialId must be valid.", nameof(materialId));

            MaterialEdit edit = new MaterialEdit(localPosition, radius, materialId, _nextOrder++);
            _edits.Add(edit);

            if (_editsByChunkKey != null)
            {
                IndexEdit(_edits.Count - 1, edit);
            }
        }

        private void IndexEdit(int editIndex, MaterialEdit edit)
        {
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
        /// Removes material edits whose circular region no longer overlaps any solid terrain.
        /// This cleans up orphaned edits after terrain has been mined away.
        /// </summary>
        public int PruneEditsOutsideTerrain(TerrainField field)
        {
            int pruned = 0;
            for (int i = _edits.Count - 1; i >= 0; i--)
            {
                MaterialEdit edit = _edits[i];

                // Sample the center of the edit. If it's air, this edit likely no longer overlaps
                // any solid terrain. For a more thorough check, sample multiple points.
                float sdf = field.Sample(edit.LocalPosition);
                if (sdf > edit.Radius)
                {
                    // The entire circle is inside the air region (SDF > radius means even the
                    // closest point on the circle boundary is in air).
                    _edits.RemoveAt(i);
                    pruned++;
                }
            }

            // Rebuild indexing if active.
            if (_editsByChunkKey != null)
            {
                _editsByChunkKey.Clear();

                // Re-initialize for all chunks.
                if (_chunkGrid != null)
                {
                    foreach (TerrainChunk chunk in _chunkGrid.AllChunks)
                    {
                        _editsByChunkKey[PackKey(chunk.Col, chunk.Row)] = new List<int>();
                    }
                }

                for (int i = 0; i < _edits.Count; i++)
                {
                    IndexEdit(i, _edits[i]);
                }
            }

            return pruned;
        }

        /// <summary>Removes all material edits.</summary>
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

        /// <summary>Replaces all material edits (e.g., when loading a save file).</summary>
        public void LoadEdits(IEnumerable<MaterialEdit> edits)
        {
            if (edits == null)
                throw new ArgumentNullException(nameof(edits));

            _edits.Clear();
            _edits.AddRange(edits);

            // Restore order counter.
            _nextOrder = 0;
            foreach (MaterialEdit edit in _edits)
            {
                if (edit.Order >= _nextOrder)
                {
                    _nextOrder = edit.Order + 1;
                }
            }

            // Rebuild chunk indexing.
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

        static long PackKey(int col, int row)
        {
            return (((long)col) << 32) | ((long)row & 0xffffffffL);
        }
    }
}
