using System;
using System.Collections.Generic;
using UnityEngine;

namespace SDFTerrain.Terrain
{
    /// <summary>
    /// Divides a planet's bounding square into a fixed grid of equal rectangular chunks and
    /// provides indexing, neighbor lookup, and dirty propagation over them. Grid dimensions are
    /// fixed at construction — chunk streaming/LOD (variable resolution) is a later optimization
    /// task. Chunks are stored in a 1D array indexed by row * cols + col.
    /// </summary>
    public class ChunkGrid
    {
        private readonly TerrainChunk[] _chunks;
        private readonly float _chunkSize;
        private readonly float _gridMinX;
        private readonly float _gridMinY;

        public int ChunkCount => _chunks.Length;
        public int Cols => _cols;
        public int Rows => _rows;
        public float ChunkSize => _chunkSize;

        private readonly int _cols;
        private readonly int _rows;

        /// <summary>
        /// Creates a square-grid chunk system covering the planet's bounding box.
        /// The grid spans from -gridExtent to +gridExtent in both axes, where gridExtent =
        /// cols * chunkSize, guaranteeing the planet of the given radius is fully covered.
        /// </summary>
        /// <param name="radius">Planet radius (used to compute grid coverage).</param>
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

            _chunks = new TerrainChunk[_cols * _rows];
            for (int row = 0; row < _rows; row++)
            {
                for (int col = 0; col < _cols; col++)
                {
                    int index = row * _cols + col;
                    float minX = _gridMinX + col * chunkSize;
                    float maxX = minX + chunkSize;
                    float minY = _gridMinY + row * chunkSize;
                    float maxY = minY + chunkSize;
                    _chunks[index] = new TerrainChunk(index, col, row, minX, maxX, minY, maxY);
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

            _chunks = new TerrainChunk[_cols * _rows];
            for (int row = 0; row < _rows; row++)
            {
                for (int col = 0; col < _cols; col++)
                {
                    int index = row * _cols + col;
                    float minX = _gridMinX + col * chunkSize;
                    float maxX = minX + chunkSize;
                    float minY = _gridMinY + row * chunkSize;
                    float maxY = minY + chunkSize;
                    _chunks[index] = new TerrainChunk(index, col, row, minX, maxX, minY, maxY);
                }
            }
        }

        public TerrainChunk GetChunk(int index)
        {
            if (index < 0 || index >= _chunks.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(index), index, "Chunk index out of range.");
            }

            return _chunks[index];
        }

        /// <summary>Returns the chunk whose bounding box contains the given position.</summary>
        public TerrainChunk GetChunkAt(Vector2 position)
        {
            int col = Mathf.FloorToInt((position.x - _gridMinX) / _chunkSize);
            int row = Mathf.FloorToInt((position.y - _gridMinY) / _chunkSize);

            // Clamp to grid bounds — positions outside the grid are handled by the edge chunk.
            col = Mathf.Clamp(col, 0, _cols - 1);
            row = Mathf.Clamp(row, 0, _rows - 1);

            return _chunks[row * _cols + col];
        }

        /// <summary>Returns the chunk at the given grid coordinates with bounds validation.</summary>
        public TerrainChunk GetChunkAtGrid(int col, int row)
        {
            if (col < 0 || col >= _cols || row < 0 || row >= _rows)
            {
                throw new ArgumentOutOfRangeException(
                    $"Grid coordinates ({col}, {row}) out of range. Grid is {_cols}x{_rows}.");
            }

            return _chunks[row * _cols + col];
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
        /// Returns the neighboring chunk in the given direction, or null if the chunk is at the
        /// grid edge (the planet is circular within a rectangular grid, not a torus).
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

            if (neighborCol < 0 || neighborCol >= _cols || neighborRow < 0 || neighborRow >= _rows)
            {
                return null;
            }

            return _chunks[neighborRow * _cols + neighborCol];
        }

        public void MarkDirtyAt(Vector2 position)
        {
            GetChunkAt(position).MarkDirty();
        }

        /// <summary>
        /// Appends the index of every chunk whose bounding box overlaps the given rectangle to
        /// <paramref name="result"/> (cleared first). Computes the overlapping column/row range
        /// directly — no iteration over all chunks.
        /// </summary>
        public void ChunksInRect(float minX, float maxX, float minY, float maxY, List<int> result)
        {
            result.Clear();

            int colStart = Mathf.FloorToInt((minX - _gridMinX) / _chunkSize);
            int colEnd = Mathf.CeilToInt((maxX - _gridMinX) / _chunkSize) - 1;
            int rowStart = Mathf.FloorToInt((minY - _gridMinY) / _chunkSize);
            int rowEnd = Mathf.CeilToInt((maxY - _gridMinY) / _chunkSize) - 1;

            colStart = Mathf.Clamp(colStart, 0, _cols - 1);
            colEnd = Mathf.Clamp(colEnd, 0, _cols - 1);
            rowStart = Mathf.Clamp(rowStart, 0, _rows - 1);
            rowEnd = Mathf.Clamp(rowEnd, 0, _rows - 1);

            for (int row = rowStart; row <= rowEnd; row++)
            {
                for (int col = colStart; col <= colEnd; col++)
                {
                    result.Add(row * _cols + col);
                }
            }
        }

        public IEnumerable<TerrainChunk> DirtyChunks()
        {
            foreach (TerrainChunk chunk in _chunks)
            {
                if (chunk.IsDirty)
                {
                    yield return chunk;
                }
            }
        }

        public void ClearAllDirty()
        {
            foreach (TerrainChunk chunk in _chunks)
            {
                chunk.ClearDirty();
            }
        }
    }
}
