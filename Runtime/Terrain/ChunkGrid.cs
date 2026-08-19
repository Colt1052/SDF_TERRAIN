using System;
using System.Collections.Generic;
using UnityEngine;

namespace SDFTerrain.Terrain
{
    /// <summary>
    /// Manages a collection of rectangular chunks that cover a planet's terrain. Starts with a
    /// fixed grid covering the planet's bounding box, but can dynamically expand to create new
    /// chunks when edits or lookups target positions outside the original grid. Chunks are stored
    /// in a dictionary keyed by packed (col, row) coordinates and assigned sequential indices
    /// via an auto-incrementing counter.
    /// </summary>
    public class ChunkGrid
    {
        private readonly Dictionary<long, TerrainChunk> _chunks = new Dictionary<long, TerrainChunk>();
        private readonly float _chunkSize;
        private readonly float _gridMinX;
        private readonly float _gridMinY;
        private int _nextIndex;

        public int ChunkCount => _chunks.Count;
        public int Cols => _cols;
        public int Rows => _rows;
        public float ChunkSize => _chunkSize;

        /// <summary>Iterate over all chunks (original + dynamically created).</summary>
        public IEnumerable<TerrainChunk> AllChunks => _chunks.Values;

        private readonly int _cols;
        private readonly int _rows;

        /// <summary>Packs (col, row) into a unique long key for dictionary lookup.</summary>
        static long MakeKey(int col, int row)
        {
            return (((long)col) << 32) | ((long)row & 0xffffffffL);
        }

        /// <summary>
        /// Creates a grid of chunks covering the planet's bounding box and extends it
        /// dynamically for edits outside that region.
        /// The initial grid spans from -gridExtent to +gridExtent in both axes, where gridExtent =
        /// cols * chunkSize, guaranteeing the planet of the given radius is fully covered.
        /// </summary>
        /// <param name="radius">Planet radius (used to compute initial grid coverage).</param>
        /// <param name="chunkSize">Side length of each square chunk in world units.</param>
        public ChunkGrid(float radius, float chunkSize)
        {
            if (radius <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(radius), radius, "Radius must be positive.");
            }

            if (chunkSize <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(chunkSize), chunkSize, "Chunk size must be positive.");
            }

            _chunkSize = chunkSize;
            _cols = Mathf.CeilToInt((2f * radius) / chunkSize);
            _rows = Mathf.CeilToInt((2f * radius) / chunkSize);

            _gridMinX = -(_cols * chunkSize) / 2f;
            _gridMinY = -(_rows * chunkSize) / 2f;

            for (int row = 0; row < _rows; row++)
            {
                for (int col = 0; col < _cols; col++)
                {
                    float minX = _gridMinX + col * chunkSize;
                    float maxX = minX + chunkSize;
                    float minY = _gridMinY + row * chunkSize;
                    float maxY = minY + chunkSize;
                    _chunks[MakeKey(col, row)] = new TerrainChunk(_nextIndex++, col, row, minX, maxX, minY, maxY);
                }
            }
        }

        /// <summary>
        /// Legacy constructor: derives chunkSize from an approximate chunk count.
        /// Creates a roughly square grid that produces approximately the requested number of chunks.
        /// </summary>
        [Obsolete("Use ChunkGrid(float radius, float chunkSize) instead.")]
        public ChunkGrid(int chunkCount, float radius)
        {
            if (chunkCount <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(chunkCount), chunkCount, "Chunk count must be positive.");
            }

            if (radius <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(radius), radius, "Radius must be positive.");
            }

            float chunkSize = (2f * radius) / Mathf.Sqrt(chunkCount);
            _chunkSize = chunkSize;
            _cols = Mathf.CeilToInt((2f * radius) / chunkSize);
            _rows = Mathf.CeilToInt((2f * radius) / chunkSize);

            _gridMinX = -(_cols * chunkSize) / 2f;
            _gridMinY = -(_rows * chunkSize) / 2f;

            for (int row = 0; row < _rows; row++)
            {
                for (int col = 0; col < _cols; col++)
                {
                    float minX = _gridMinX + col * chunkSize;
                    float maxX = minX + chunkSize;
                    float minY = _gridMinY + row * chunkSize;
                    float maxY = minY + chunkSize;
                    _chunks[MakeKey(col, row)] = new TerrainChunk(_nextIndex++, col, row, minX, maxX, minY, maxY);
                }
            }
        }

        public TerrainChunk GetChunk(int index)
        {
            // Linear search by index — used primarily by debug views and tests.
            // ChunkTerrainRenderer uses _chunkViews dictionary keyed by index, not this method.
            foreach (TerrainChunk chunk in _chunks.Values)
            {
                if (chunk.Index == index)
                    return chunk;
            }
            throw new ArgumentOutOfRangeException(nameof(index), index, "Chunk index not found.");
        }

        /// <summary>Returns the chunk whose bounding box contains the given position.</summary>
        public TerrainChunk GetChunkAt(Vector2 position)
        {
            int col = Mathf.FloorToInt((position.x - _gridMinX) / _chunkSize);
            int row = Mathf.FloorToInt((position.y - _gridMinY) / _chunkSize);

            long key = MakeKey(col, row);
            if (_chunks.TryGetValue(key, out TerrainChunk existing))
                return existing;

            // Position outside existing chunks — create a new one.
            return CreateChunk(col, row);
        }

        /// <summary>Returns the chunk at the given grid coordinates with bounds validation.</summary>
        public TerrainChunk GetChunkAtGrid(int col, int row)
        {
            long key = MakeKey(col, row);
            if (!_chunks.TryGetValue(key, out TerrainChunk chunk))
            {
                throw new ArgumentOutOfRangeException(
                    $"No chunk at grid coordinates ({col}, {row}).");
            }
            return chunk;
        }

        /// <summary>Gets an existing chunk or creates a new one at the given grid coordinates.</summary>
        public TerrainChunk GetOrCreateChunkAtGrid(int col, int row)
        {
            long key = MakeKey(col, row);
            if (_chunks.TryGetValue(key, out TerrainChunk chunk))
                return chunk;

            return CreateChunk(col, row);
        }

        /// <summary>Direction for 4-neighbor grid lookups.</summary>
        public enum ChunkNeighbor
        {
            Top,
            Bottom,
            Left,
            Right
        }

        /// <summary>
        /// Returns the neighboring chunk in the given direction, or null if no neighbor exists
        /// (the planet is circular within a rectangular grid, not a torus).
        /// </summary>
        public TerrainChunk GetNeighbor(TerrainChunk chunk, ChunkNeighbor direction)
        {
            if (chunk == null)
            {
                throw new ArgumentNullException(nameof(chunk));
            }

            int neighborCol = chunk.Col;
            int neighborRow = chunk.Row;

            switch (direction)
            {
                case ChunkNeighbor.Top:
                    neighborRow++;
                    break;
                case ChunkNeighbor.Bottom:
                    neighborRow--;
                    break;
                case ChunkNeighbor.Left:
                    neighborCol--;
                    break;
                case ChunkNeighbor.Right:
                    neighborCol++;
                    break;
            }

            long key = MakeKey(neighborCol, neighborRow);
            if (_chunks.TryGetValue(key, out TerrainChunk neighbor))
                return neighbor;

            return null;
        }

        public void MarkDirtyAt(Vector2 position)
        {
            GetChunkAt(position).MarkDirty();
        }

        /// <summary>
        /// Appends the index of every chunk whose bounding box overlaps the given rectangle to
        /// <paramref name="result"/> (cleared first). By default creates new chunks if the
        /// rectangle extends beyond the current chunk coverage; set <paramref name="createChunks"/>
        /// to false to return only existing chunks. Computes the overlapping column/row range
        /// directly — no iteration over all chunks.
        /// </summary>
        public void ChunksInRect(float minX, float maxX, float minY, float maxY, List<int> result, bool createChunks = true)
        {
            result.Clear();

            int colStart = Mathf.FloorToInt((minX - _gridMinX) / _chunkSize);
            int colEnd = Mathf.CeilToInt((maxX - _gridMinX) / _chunkSize) - 1;
            int rowStart = Mathf.FloorToInt((minY - _gridMinY) / _chunkSize);
            int rowEnd = Mathf.CeilToInt((maxY - _gridMinY) / _chunkSize) - 1;

            for (int row = rowStart; row <= rowEnd; row++)
            {
                for (int col = colStart; col <= colEnd; col++)
                {
                    long key = MakeKey(col, row);
                    if (_chunks.TryGetValue(key, out TerrainChunk chunk))
                    {
                        result.Add(chunk.Index);
                    }
                    else if (createChunks)
                    {
                        chunk = CreateChunk(col, row);
                        result.Add(chunk.Index);
                    }
                }
            }
        }

        public IEnumerable<TerrainChunk> DirtyChunks()
        {
            foreach (TerrainChunk chunk in _chunks.Values)
            {
                if (chunk.IsDirty)
                    yield return chunk;
            }
        }

        public void ClearAllDirty()
        {
            foreach (TerrainChunk chunk in _chunks.Values)
            {
                chunk.ClearDirty();
            }
        }

        private TerrainChunk CreateChunk(int col, int row)
        {
            float minX = _gridMinX + col * _chunkSize;
            float maxX = minX + _chunkSize;
            float minY = _gridMinY + row * _chunkSize;
            float maxY = minY + _chunkSize;

            long key = MakeKey(col, row);
            TerrainChunk chunk = new TerrainChunk(_nextIndex++, col, row, minX, maxX, minY, maxY);
            _chunks[key] = chunk;
            return chunk;
        }
    }
}
