using System;
using UnityEngine;

namespace SDFTerrain.Terrain
{
    /// <summary>
    /// Samples a <see cref="TerrainField"/> over a single <see cref="TerrainChunk"/>'s rectangular
    /// bounding box onto a fixed-size, axis-aligned Cartesian lattice (world units), producing a
    /// grid consumable by <see cref="Meshing.MarchingSquaresMesher"/>'s position-grid overload.
    ///
    /// Each chunk samples the field freely within its bounding box — no clipping mask is applied.
    /// Lattice points outside the planet's surface naturally read as air (positive SDF), so
    /// Marching Squares produces no contour in all-air regions. Chunks that lie partially outside
    /// the planet simply render nothing for those regions.
    ///
    /// The lattice covers exactly the chunk's bounding box. Because every chunk samples the *same*
    /// global Cartesian lattice, adjacent chunks share boundary lattice points with identical
    /// terrain values — Marching Squares produces contiguous mesh edges at every seam. No margin,
    /// seam cache, or wedge mask is needed.
    /// </summary>
    public static class CartesianChunkFieldSampler
    {
        public readonly struct Result
        {
            public readonly float[,] Samples;
            public readonly Vector2[,] Positions;

            public Result(float[,] samples, Vector2[,] positions)
            {
                Samples = samples;
                Positions = positions;
            }
        }

        /// <summary>
        /// Samples the given chunk's rectangular bounding box onto a Cartesian lattice with the
        /// given cell size (world units). The lattice covers exactly the chunk's bounding box;
        /// adjacent chunks share boundary lattice points with identical terrain values, so
        /// Marching Squares produces contiguous mesh edges at every seam.
        /// </summary>
        public static Result Sample(TerrainField field, TerrainChunk chunk, float cellSize)
        {
            if (field == null)
            {
                throw new ArgumentNullException(nameof(field));
            }

            if (chunk == null)
            {
                throw new ArgumentNullException(nameof(chunk));
            }

            if (cellSize <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(cellSize), cellSize, "Cell size must be positive.");
            }

            // Compute lattice index bounds: exactly the chunk bounding box.
            int ixMin = Mathf.FloorToInt(chunk.MinX / cellSize);
            int ixMax = Mathf.CeilToInt(chunk.MaxX / cellSize);
            int iyMin = Mathf.FloorToInt(chunk.MinY / cellSize);
            int iyMax = Mathf.CeilToInt(chunk.MaxY / cellSize);

            int width = ixMax - ixMin + 1;
            int height = iyMax - iyMin + 1;
            var samples = new float[width, height];
            var positions = new Vector2[width, height];

            for (int i = 0; i < width; i++)
            {
                float x = (ixMin + i) * cellSize;

                for (int j = 0; j < height; j++)
                {
                    float y = (iyMin + j) * cellSize;
                    var position = new Vector2(x, y);

                    // Chunk-agnostic Sample(), not the chunk-indexed overload: adjacent chunks
                    // share boundary lattice points that sample the same field at the same
                    // position. Using the global sample ensures bit-identical values regardless
                    // of which chunk samples them. The chunk-indexed overload decides edit
                    // membership per chunk via a coarse rectangular test, which could treat a
                    // shared boundary point differently from each chunk.
                    float terrainValue = field.Sample(position);

                    positions[i, j] = position;
                    samples[i, j] = terrainValue;
                }
            }

            return new Result(samples, positions);
        }
    }
}
