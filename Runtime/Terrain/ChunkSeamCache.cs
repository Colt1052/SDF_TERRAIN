using System;
using UnityEngine;
using SDFTerrain.Core;

namespace SDFTerrain.Terrain
{
    /// <summary>
    /// Precomputes, once per <see cref="ChunkGrid"/>, the single canonical direction vector for
    /// each shared boundary ray between angularly-adjacent chunks. <see
    /// cref="CartesianChunkFieldSampler"/> previously had each chunk independently call
    /// <see cref="RadialMath.DirectionAt"/> on its own StartAngle/EndAngle — two separate float
    /// pipelines that are only equal if both chunks' boundary angles happen to be bit-identical.
    /// Feeding both neighboring chunks the exact same cached direction for their shared ray
    /// removes that assumption: the wedge-mask term for any lattice cell straddling the seam is
    /// now computed from one shared number rather than two independently-derived ones, so it is
    /// bit-identical on both sides by construction, not by coincidence.
    ///
    /// A chunk's StartAngle/EndAngle never change after <see cref="ChunkGrid"/> construction (per
    /// CLAUDE.md, chunk count/geometry is fixed; only chunk *contents* are dirtied and rebuilt),
    /// so this cache is built once and never invalidated.
    /// </summary>
    public class ChunkSeamCache
    {
        private readonly Vector2[] _seamDirections;

        /// <summary>
        /// Builds the seam direction cache for the given grid. Seam <c>i</c> is the boundary ray
        /// between chunk <c>i</c> and chunk <c>(i + 1) % ChunkCount</c>, i.e. chunk <c>i</c>'s
        /// EndAngle / chunk <c>i + 1</c>'s StartAngle.
        /// </summary>
        public ChunkSeamCache(ChunkGrid chunkGrid)
        {
            if (chunkGrid == null)
            {
                throw new ArgumentNullException(nameof(chunkGrid));
            }

            _seamDirections = new Vector2[chunkGrid.ChunkCount];
            for (int i = 0; i < chunkGrid.ChunkCount; i++)
            {
                _seamDirections[i] = RadialMath.DirectionAt(chunkGrid.GetChunk(i).EndAngle);
            }
        }

        /// <summary>The shared direction vector for the given chunk's EndAngle boundary.</summary>
        public Vector2 GetEndDirection(int chunkIndex)
        {
            return _seamDirections[chunkIndex];
        }

        /// <summary>
        /// The shared direction vector for the given chunk's StartAngle boundary — identical to
        /// the previous chunk's EndAngle direction, since that is the same physical ray.
        /// </summary>
        public Vector2 GetStartDirection(int chunkIndex)
        {
            int previousIndex = (chunkIndex - 1 + _seamDirections.Length) % _seamDirections.Length;
            return _seamDirections[previousIndex];
        }
    }
}
