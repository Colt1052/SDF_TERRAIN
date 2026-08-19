using System;
using System.Collections.Generic;
using SDFTerrain.Core;

namespace SDFTerrain.Terrain
{
    /// <summary>
    /// Divides a planet's circumference into a fixed number of equal angular chunks and provides
    /// indexing, neighbor lookup, and dirty propagation over them. Chunk count is fixed at
    /// construction — chunk streaming/LOD (variable resolution) is a later optimization task.
    /// </summary>
    public class ChunkGrid
    {
        private readonly TerrainChunk[] _chunks;
        private readonly float _chunkAngularSize;

        public int ChunkCount => _chunks.Length;

        public ChunkGrid(int chunkCount)
        {
            if (chunkCount <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(chunkCount), chunkCount, "Chunk count must be positive.");
            }

            _chunks = new TerrainChunk[chunkCount];
            _chunkAngularSize = (2f * (float)Math.PI) / chunkCount;

            // Each chunk's start is set to the exact same float value as the previous chunk's
            // end, so adjacent chunks share a bit-identical boundary angle (no float-precision
            // gap/overlap at the seam).
            float previousEnd = 0f;
            for (int i = 0; i < chunkCount; i++)
            {
                float start = i * _chunkAngularSize;
                float end = start + _chunkAngularSize;
                //float start = previousEnd;
                //float end = (i == chunkCount - 1) ? 2f * (float)Math.PI : (i + 1) * _chunkAngularSize;
                _chunks[i] = new TerrainChunk(i, start, end);
                //previousEnd = end;
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

        /// <summary>Returns the chunk whose angular range contains the given angle (in radians).</summary>
        public TerrainChunk GetChunkAt(float angleRadians)
        {
            float wrapped = RadialMath.WrapAngle(angleRadians);
            int index = (int)(wrapped / _chunkAngularSize);
            // Guard against floating-point rounding pushing the index to ChunkCount at the seam.
            index = Math.Min(index, _chunks.Length - 1);
            return _chunks[index];
        }

        /// <summary>Returns the chunk immediately clockwise of the given index, wrapping at the seam.</summary>
        public TerrainChunk GetNextChunk(int index)
        {
            return GetChunk((index + 1) % _chunks.Length);
        }

        /// <summary>Returns the chunk immediately counter-clockwise of the given index, wrapping at the seam.</summary>
        public TerrainChunk GetPreviousChunk(int index)
        {
            return GetChunk((index - 1 + _chunks.Length) % _chunks.Length);
        }

        public void MarkDirtyAt(float angleRadians)
        {
            GetChunkAt(angleRadians).MarkDirty();
        }

        /// <summary>
        /// Appends the index of every chunk overlapping the angular range [minAngle, maxAngle] to
        /// <paramref name="result"/> (cleared first). Walks chunk boundaries rather than an
        /// arbitrary angle stride so it can never skip a chunk regardless of chunk count or
        /// brush radius. A full-circle range (span >= 2*PI) appends every chunk. Shared by dirty
        /// marking and per-chunk edit membership so both use identical membership semantics.
        /// </summary>
        public void ChunksInRange(float minAngle, float maxAngle, List<int> result)
        {
            result.Clear();

            if (maxAngle - minAngle >= 2f * (float)Math.PI)
            {
                for (int i = 0; i < _chunks.Length; i++)
                {
                    result.Add(i);
                }

                return;
            }

            TerrainChunk startChunk = GetChunkAt(RadialMath.WrapAngle(minAngle));
            TerrainChunk endChunk = GetChunkAt(RadialMath.WrapAngle(maxAngle));

            int index = startChunk.Index;
            while (true)
            {
                result.Add(index);
                if (index == endChunk.Index)
                {
                    break;
                }

                index = (index + 1) % _chunks.Length;
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
